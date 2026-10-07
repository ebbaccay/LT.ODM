using System.Net;

namespace LT.ODM.Api.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_is_anonymous_reports_unhealthy_when_sql_unreachable_and_is_not_cached()
    {
        var response = await factory.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unhealthy", body);
        Assert.DoesNotContain("Exception", body);
    }

    [Fact]
    public async Task Client_config_requires_sign_in()
    {
        var response = await factory.CreateHttpsClient().GetAsync("/api/v1/client-config");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Password_policy_is_public()
    {
        var response = await factory.CreateHttpsClient().GetAsync("/api/v1/auth/password-policy");
        response.EnsureSuccessStatusCode();
        Assert.Contains("\"minLength\":12", await response.Content.ReadAsStringAsync());
    }
}
