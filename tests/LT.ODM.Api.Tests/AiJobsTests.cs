using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LT.ODM.Api.Jobs;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>ai.Job in memory, with the procedures' rules.</summary>
public sealed class FakeAiJobRepository : IAiJobRepository
{
    private readonly object _lock = new();
    private readonly List<AiJobRow> _rows = [];
    public int KeepDays { get; private set; } = 7;

    public IReadOnlyList<AiJobRow> Rows { get { lock (_lock) return _rows.ToList(); } }

    public Task<int> AddAsync(string kind, string lane, AiJobOwner owner, string? input, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var id = _rows.Count + 1;
            _rows.Add(new AiJobRow(id, kind, lane, AiJobStatuses.Queued, owner.Name, owner.Id, owner.TaskKey, owner.Label, owner.Url, input, null, null, null,
                false, DateTime.UtcNow, null, null, null));
            return Task.FromResult(id);
        }
    }

    public Task<bool> StartAsync(int jobId, CancellationToken ct = default)
        => Task.FromResult(Change(jobId, r => r.Status == AiJobStatuses.Queued, r => r with { Status = AiJobStatuses.Running, StartedUtc = DateTime.UtcNow }));

    public Task FinishAsync(int jobId, string status, string? result, string? error, int? errorStatus, CancellationToken ct = default)
    {
        Change(jobId, r => r.FinishedUtc is null, r => r.Status == AiJobStatuses.Cancelled
            ? r with { FinishedUtc = DateTime.UtcNow }
            : r with { Status = status, Result = result, Error = error, ErrorStatus = errorStatus, FinishedUtc = DateTime.UtcNow });
        return Task.CompletedTask;
    }

    public Task<AiJobRow?> GetAsync(int jobId, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_rows.FirstOrDefault(r => r.JobId == jobId));
    }

    public Task<IReadOnlyList<AiJobRow>> MineAsync(string ownerId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<AiJobRow>>(_rows.Where(r => r.OwnerId == ownerId && r.TaskKey is not null
                && (r.Status is AiJobStatuses.Queued or AiJobStatuses.Running || !r.Seen && r.Status != AiJobStatuses.Cancelled)).Reverse().ToList());
    }

    public Task<(IReadOnlyList<AiJobRow> Items, int Total)> ListAsync(string? status, int skip, int take, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var list = _rows.Where(r => status is null || r.Status == status || status == AiJobStatuses.Active && r.Status is AiJobStatuses.Queued or AiJobStatuses.Running)
                .Reverse().Select(r => r with { Input = null, Result = null }).ToList();   // the list leaves input and answer out
            return Task.FromResult<(IReadOnlyList<AiJobRow>, int)>((list.Skip(skip).Take(take).ToList(), list.Count));
        }
    }

    public Task SeenAsync(int jobId, string ownerId, CancellationToken ct = default)
    {
        Change(jobId, r => r.OwnerId == ownerId, r => r with { Seen = true });
        return Task.CompletedTask;
    }

    public Task<bool> CancelAsync(int jobId, string cancelledBy, CancellationToken ct = default)
        => Task.FromResult(Change(jobId, r => r.Status is AiJobStatuses.Queued or AiJobStatuses.Running,
            r => r with { Status = AiJobStatuses.Cancelled, CancelledBy = cancelledBy, FinishedUtc = r.Status == AiJobStatuses.Queued ? DateTime.UtcNow : r.FinishedUtc }));

    public Task<int> InterruptAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> PurgeAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> KeepDaysAsync(CancellationToken ct = default) => Task.FromResult(KeepDays);

    public Task SaveKeepDaysAsync(int days, string changedBy, CancellationToken ct = default)
    {
        KeepDays = days;
        return Task.CompletedTask;
    }

    private bool Change(int jobId, Func<AiJobRow, bool> when, Func<AiJobRow, AiJobRow> change)
    {
        lock (_lock)
        {
            var i = _rows.FindIndex(r => r.JobId == jobId);
            if (i < 0 || !when(_rows[i])) return false;
            _rows[i] = change(_rows[i]);
            return true;
        }
    }
}

public sealed class AiJobsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "job" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    /// <summary>The job an endpoint queued, once it has finished.</summary>
    public static async Task<AiJobDto> FinishedAsync(HttpClient client, HttpResponseMessage started)
    {
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var id = (await started.Content.ReadFromJsonAsync<AiJobStartedDto>())!.JobId;
        for (var i = 0; i < 200; i++)
        {
            var job = await client.GetFromJsonAsync<AiJobDto>($"/api/v1/ai/jobs/{id}");
            if (job!.Status is not (AiJobStatuses.Queued or AiJobStatuses.Running)) return job;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Job {id} did not finish.");
    }

    [Fact]
    public async Task A_slow_ai_call_answers_202_and_its_job_carries_the_answer_for_its_owner_only()
    {
        factory.TextAi.Answers["a change summary"] = """{ "headline": "Fabric swapped", "summary": "The shell fabric changed.", "highlights": [], "checks": [] }""";
        var owner = await SignInAsync("Viewer");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/style-compare/summary") { Content = JsonContent.Create(new { styleId = 7, fromStyleId = 8 }) };
        request.Headers.Add(AiJobEndpoints.KeyHeader, "compare:7:8");
        request.Headers.Add(AiJobEndpoints.LabelHeader, Uri.EscapeDataString("2027-SS 春夏"));   // not ASCII: sent encoded
        request.Headers.Add(AiJobEndpoints.UrlHeader, Uri.EscapeDataString("https://evil.example/x"));   // not a page of the app: dropped
        var job = await FinishedAsync(owner, await owner.SendAsync(request));

        Assert.True(job.Status == AiJobStatuses.Done, job.Error);
        Assert.Equal((AiJobKinds.CompareSummary, AiJobLanes.Text), (job.Kind, job.Lane));
        Assert.Equal(("compare:7:8", "2027-SS 春夏", null), (job.TaskKey, job.Label, job.Url));
        Assert.NotNull(job.Result!.Value.Deserialize<CompareSummaryDto>(Json));

        // Someone else: as if it did not exist. An Admin: sees it.
        var other = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/ai/jobs/{job.JobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/ai/jobs/{job.JobId}/cancel", null)).StatusCode);
        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/ai/jobs/{job.JobId}")).StatusCode);

        // The tray offers it again after a reload until its page has shown it.
        Assert.Contains(await owner.GetFromJsonAsync<List<AiJobDto>>("/api/v1/ai/jobs") ?? [], j => j.JobId == job.JobId);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/ai/jobs/{job.JobId}/seen", null)).StatusCode);
        Assert.DoesNotContain(await owner.GetFromJsonAsync<List<AiJobDto>>("/api/v1/ai/jobs") ?? [], j => j.JobId == job.JobId);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/v1/ai/jobs/{job.JobId}/cancel", null)).StatusCode);   // finished
    }

    [Fact]
    public async Task A_failure_is_recorded_with_the_status_the_request_would_have_answered()
    {
        var client = await SignInAsync("Merchandiser");
        var job = await FinishedAsync(client, await client.PostAsJsonAsync("/api/v1/ai/style-render/404/prompt", new { colorwayId = (int?)null }));
        Assert.Equal((AiJobStatuses.Failed, 404), (job.Status, job.ErrorStatus));
        Assert.False(string.IsNullOrWhiteSpace(job.Error));
    }

    [Fact]
    public async Task Only_admins_list_every_job_and_set_how_long_they_are_kept()
    {
        factory.TextAi.Answers["a change summary"] = """{ "headline": "Fabric swapped", "summary": "The shell fabric changed.", "highlights": [], "checks": [] }""";
        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/admin/ai/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/admin/ai/jobs/keep-days", new { days = 30 })).StatusCode);
        await FinishedAsync(viewer, await viewer.PostAsJsonAsync("/api/v1/ai/style-compare/summary", new { styleId = 7, fromStyleId = 8 }));

        var admin = await SignInAsync("Admin");
        var page = await admin.GetFromJsonAsync<AiJobPageDto>("/api/v1/admin/ai/jobs?status=Done");
        Assert.True(page!.Total >= 1);
        Assert.All(page.Items, j => Assert.Equal(AiJobStatuses.Done, j.Status));
        Assert.All(page.Items, j => Assert.Null(j.Result));   // the list leaves answers out

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/ai/jobs/keep-days", new { days = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/admin/ai/jobs/keep-days", new { days = 30 })).StatusCode);
        Assert.Equal(30, (await admin.GetFromJsonAsync<AiJobPageDto>("/api/v1/admin/ai/jobs"))!.KeepDays);
        await admin.PutAsJsonAsync("/api/v1/admin/ai/jobs/keep-days", new { days = 7 });
    }

    [Fact]
    public async Task Cancelling_a_running_job_stops_its_work()
    {
        var gate = new TaskCompletionSource();
        var queue = factory.Services.GetRequiredService<AiJobQueue>();
        var id = await queue.QueueAsync("test", AiJobLanes.Image, new AiJobOwner("tester", "tester-id", null, null, null), null, async (_, ct) =>
        {
            gate.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        });
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var jobs = factory.Services.GetRequiredService<IAiJobRepository>();
        Assert.True(await jobs.CancelAsync(id, "admin"));
        Assert.True(queue.Stop(id));
        for (var i = 0; i < 200 && (await jobs.GetAsync(id))!.FinishedUtc is null; i++) await Task.Delay(25);
        var row = (await jobs.GetAsync(id))!;
        Assert.Equal((AiJobStatuses.Cancelled, "admin"), (row.Status, row.CancelledBy));
        Assert.NotNull(row.FinishedUtc);
    }
}
