using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Api.Jobs;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Auth;
using LT.ODM.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>AI call limits: places at each AI server, quick questions first, users in turn (AiCallLimiter), and the switch.</summary>
public sealed class AiCallLimiterTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static AiConnection Server(int limit, string endpoint = "http://ai.test/v1/")
        => new("OpenVINO", AiConnectionKinds.OpenAiCompatible, endpoint, null, "qwen", true, 60, "settings", limit);

    /// <summary>Asks for a place as this caller (each in its own flow, as separate requests and tasks are).</summary>
    private static Task<IDisposable> Ask(AiCallLimiter limiter, AiConnection c, string user, AiCallPriority priority, AiProgressEntry? progress = null,
        CancellationToken ct = default)
        => Task.Run(async () =>
        {
            AiCallContext.Set(new AiCaller(user, priority));
            AiProgress.Follow(progress);
            return await limiter.EnterAsync(c, ct);
        });

    [Fact]
    public async Task A_server_gets_its_limit_and_the_rest_wait_quick_questions_first_users_in_turn()
    {
        var limiter = new AiCallLimiter();
        var c = Server(1);
        var first = await Ask(limiter, c, "anna", AiCallPriority.Background);

        // Anna queues two more tasks, then Ben one, then Carl asks the assistant (someone waiting on the screen).
        var annaProgress = new AiProgressEntry();
        var anna2 = Ask(limiter, c, "anna", AiCallPriority.Background, annaProgress);
        await Task.Delay(50);
        var anna3 = Ask(limiter, c, "anna", AiCallPriority.Background);
        await Task.Delay(50);
        var ben = Ask(limiter, c, "ben", AiCallPriority.Background);
        await Task.Delay(50);
        var carl = Ask(limiter, c, "carl", AiCallPriority.Interactive);
        await Task.Delay(50);

        var load = Assert.Single(limiter.Load());
        Assert.Equal((1, 1, 1, 3), (load.Limit, load.Running, load.WaitingInteractive, load.WaitingBackground));
        Assert.Equal(("queued", 2), (annaProgress.Snapshot().Phase, annaProgress.Snapshot().Ahead));   // behind Carl and Ben

        var order = new List<string>();
        async Task<string> Next(params (string Name, Task<IDisposable> Task)[] waiting)
        {
            var done = await Task.WhenAny(waiting.Select(w => w.Task)).WaitAsync(TimeSpan.FromSeconds(5));
            var name = waiting.First(w => w.Task == done).Name;
            order.Add(name);
            return name;
        }

        first.Dispose();
        Assert.Equal("carl", await Next(("anna2", anna2), ("anna3", anna3), ("ben", ben), ("carl", carl)));   // interactive first
        (await carl).Dispose();
        Assert.Equal("ben", await Next(("anna2", anna2), ("anna3", anna3), ("ben", ben)));   // Anna was served last: Ben's turn
        (await ben).Dispose();
        Assert.Equal("anna2", await Next(("anna2", anna2), ("anna3", anna3)));
        Assert.Equal("waiting", annaProgress.Snapshot().Phase);   // sent
        (await anna2).Dispose();
        (await anna3).Dispose();
        Assert.Equal(0, Assert.Single(limiter.Load()).Running);
    }

    [Fact]
    public async Task Connections_to_the_same_address_share_one_limit_and_no_limit_lets_everything_through()
    {
        var limiter = new AiCallLimiter();
        var text = Server(2, "http://AI.test/v1");
        var image = Server(1, "http://ai.test/v1/") with { Name = "OpenVINO images" };
        var a = await Ask(limiter, text, "anna", AiCallPriority.Background);
        var b = Ask(limiter, image, "ben", AiCallPriority.Background);   // same server: the lowest limit (1) applies
        await Task.Delay(100);
        Assert.False(b.IsCompleted);
        a.Dispose();
        (await b.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();

        var unlimited = Server(0, "http://other.test/v1");
        var calls = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Ask(limiter, unlimited, "u" + i, AiCallPriority.Background)));
        Assert.Equal(5, calls.Length);
        Assert.DoesNotContain(limiter.Load(), s => s.Name == "OpenVINO" && s.Running > 0);
    }

    [Fact]
    public async Task A_call_cancelled_while_waiting_leaves_the_queue_and_takes_no_place()
    {
        var limiter = new AiCallLimiter();
        var c = Server(1);
        var running = await Ask(limiter, c, "anna", AiCallPriority.Background);
        using var cts = new CancellationTokenSource();
        var waiting = Ask(limiter, c, "ben", AiCallPriority.Background, ct: cts.Token);
        var after = Ask(limiter, c, "carl", AiCallPriority.Background);
        await Task.Delay(50);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        running.Dispose();
        (await after.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.Equal((0, 0), (limiter.Load()[0].Running, limiter.Load()[0].WaitingBackground));
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "lim" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash("Mh7!Rv92#Kp4w"), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = "Mh7!Rv92#Kp4w", rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    [Fact]
    public async Task A_user_may_have_only_so_many_tasks_while_the_limits_are_on_and_only_admins_change_the_limits()
    {
        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/admin/ai/limits", new { limitCalls = false, maxTasksPerUser = 3 })).StatusCode);
        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/ai/limits", new { limitCalls = true, maxTasksPerUser = 0 })).StatusCode);

        // Held tasks: owner "holder" fills one place; with 1 per user the next is refused (429), with the limits off it is not.
        var queue = factory.Services.GetRequiredService<AiJobQueue>();
        var gate = new TaskCompletionSource();
        var owner = new AiJobOwner("holder", "holder-id", null, null, null);
        Task<object?> Hold(IServiceProvider _, CancellationToken ct) => gate.Task.WaitAsync(ct).ContinueWith(_ => (object?)null, ct);
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/admin/ai/limits", new { limitCalls = true, maxTasksPerUser = 1 })).StatusCode);
            await queue.QueueAsync("test", AiJobLanes.Text, owner, null, Hold);
            await Assert.ThrowsAsync<AiJobLimitException>(() => queue.QueueAsync("test", AiJobLanes.Text, owner, null, Hold));

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/admin/ai/limits", new { limitCalls = false, maxTasksPerUser = 1 })).StatusCode);
            await queue.QueueAsync("test", AiJobLanes.Text, owner, null, Hold);
        }
        finally
        {
            gate.SetResult();
            await admin.PutAsJsonAsync("/api/v1/admin/ai/limits", new { limitCalls = true, maxTasksPerUser = 3 });
        }
        var policy = (await factory.AiSettings.GetAsync()).Policy;
        Assert.Equal((true, 3), (policy.LimitCalls, policy.MaxTasksPerUser));
    }
}
