using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed partial class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<(string UserName, string Email)> CreateUserAsync(string password = Password)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var userName = $"user{id}";
        var email = $"{userName}@company.test";
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(userName, email, $"Test {id}", hasher.Hash(password), false, ["Viewer"]);
        return (userName, email);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password, bool remember = false)
        => client.PostAsJsonAsync("/api/v1/auth/login", new { login, password, rememberMe = remember });

    [Fact]
    public async Task Api_requires_sign_in_by_default()
    {
        var response = await factory.CreateHttpsClient().GetAsync("/api/v1/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_returns_access_token_and_HttpOnly_refresh_cookie()
    {
        var (userName, _) = await CreateUserAsync();
        var client = factory.CreateHttpsClient();

        var response = await LoginAsync(client, userName, Password, remember: true);

        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("ltodm_rt=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        var me = await client.GetFromJsonAsync<UserProfileDto>("/api/v1/auth/me");
        Assert.Equal(userName, me!.UserName);

        var config = await client.GetStringAsync("/api/v1/client-config");
        Assert.Contains("\"agGridLicenseKey\":\"test-key\"", config);
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_get_the_same_answer()
    {
        var (userName, _) = await CreateUserAsync();
        var client = factory.CreateHttpsClient();

        var unknown = await LoginAsync(client, "nobody-here", Password);
        var wrong = await LoginAsync(client, userName, "Wrong!Password9");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await Title(unknown), await Title(wrong));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var (userName, _) = await CreateUserAsync();
        var client = factory.CreateHttpsClient();

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, userName, "Wrong!Password9")).StatusCode);

        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(client, userName, "Wrong!Password9")).StatusCode);
        // Even the right password is refused while locked.
        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(client, userName, Password)).StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_old_token_cannot_be_replayed_quietly()
    {
        var (userName, _) = await CreateUserAsync();
        var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client, userName, Password);
        var firstCookie = CookieValue(login);

        var refresh = await client.PostAsync("/api/v1/auth/refresh", null);
        refresh.EnsureSuccessStatusCode();
        Assert.NotEqual(firstCookie, CookieValue(refresh));

        // Replaying the first token (e.g. a second tab) inside the grace window: 409, client retries with the new cookie.
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", $"ltodm_rt={firstCookie}");
        var replayClient = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Conflict, (await replayClient.SendAsync(replay)).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var (userName, _) = await CreateUserAsync();
        var client = factory.CreateHttpsClient();
        await LoginAsync(client, userName, Password);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_for_unknown_email_and_sends_nothing()
    {
        var before = factory.Emails.Sent.Count;
        var response = await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody@company.test" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before, factory.Emails.Sent.Count);
    }

    [Fact]
    public async Task Reset_password_flow_enforces_policy_and_is_single_use()
    {
        var (userName, email) = await CreateUserAsync();
        var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email })).StatusCode);
        var mail = factory.Emails.Sent.Last(m => m.To == email);
        var token = Uri.UnescapeDataString(TokenInLink().Match(mail.TextBody).Groups[1].Value);
        Assert.StartsWith("https://ltodm.test/reset-password?token=", TokenInLink().Match(mail.TextBody).Value);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/reset-password/validate", new { token })).StatusCode);

        var weak = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Summer2026!!" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("too common", await weak.Content.ReadAsStringAsync());

        var reused = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = Password });
        Assert.Contains("used this password recently", await reused.Content.ReadAsStringAsync());

        const string newPassword = "Vb3$Lq8!Tn6@Wd";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, userName, newPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, userName, Password)).StatusCode);

        // The link works once.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Gx5%Hm2^Jp9&Rk" })).StatusCode);
    }

    [Fact]
    public async Task Change_password_checks_current_password()
    {
        var (userName, _) = await CreateUserAsync();
        var client = factory.CreateHttpsClient();
        var login = await (await LoginAsync(client, userName, Password)).Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var wrong = await client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = "Not!MyPassword1", newPassword = "Vb3$Lq8!Tn6@Wd" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("currentPassword", await wrong.Content.ReadAsStringAsync());

        var ok = await client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = Password, newPassword = "Vb3$Lq8!Tn6@Wd" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        // All sessions are signed out after a password change.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    private static string CookieValue(HttpResponseMessage response)
        => Regex.Match(response.Headers.GetValues("Set-Cookie").Single(), "ltodm_rt=([^;]+)").Groups[1].Value;

    private static async Task<string?> Title(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString();

    [GeneratedRegex(@"https://\S+token=(\S+)")]
    private static partial Regex TokenInLink();
}
