using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LT.ODM.Api.Controllers;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleAi;
using LT.ODM.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace LT.ODM.Api.Tests;

/// <summary>Progress of slow AI calls for the browser: streamed words for text, estimated times for renders.</summary>
public sealed class AiProgressTests
{
    [Fact]
    public void Words_are_counted_across_streamed_pieces()
    {
        var entry = new AiProgressEntry();
        Assert.Equal(("waiting", 0), (entry.Snapshot().Phase, entry.Snapshot().Words));

        entry.Wrote("{\"summary\":\"The sh");
        entry.Wrote("ell fabric chan");
        entry.Wrote("ged to 93% cotton\"}");

        // summary, The, shell, fabric, changed, to, 93, cotton: a word split across pieces counts once.
        Assert.Equal(("writing", 8), (entry.Snapshot().Phase, entry.Snapshot().Words));
    }

    private static string Chunk(string content) => "data: " + new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = content } }),
    }.ToJsonString() + "\n\n";

    private sealed class Streamer(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    [Fact]
    public async Task A_streamed_answer_reports_to_the_followed_request()
    {
        var server = new AiConnection("OpenVINO", AiConnectionKinds.OpenAiCompatible, "http://ai.test/v1", null, "qwen2.5-7b", true, 30, "settings");
        var entry = new AiProgressEntry();
        AiProgress.Follow(entry);
        try
        {
            await OpenAiApi.GenerateJsonAsync(new HttpClient(new Streamer(Chunk("{\"headline\":\"Fabric ") + Chunk("swapped\"}"))), server,
                NullLogger.Instance, "test", "s", "u", new JsonObject { ["type"] = "object" }, 0.1, CancellationToken.None);
        }
        finally { AiProgress.Follow(null); }

        Assert.Equal(("writing", 3), (entry.Snapshot().Phase, entry.Snapshot().Words));
    }

    [Fact]
    public async Task A_followed_request_is_visible_to_its_user_only_while_it_runs()
    {
        var registry = new AiProgressRegistry();
        var id = Guid.NewGuid().ToString();
        AiProgressEntry? seenInside = null, seenByOther = null;

        await registry.FollowAsync("user-1", id, () =>
        {
            AiProgress.Current!.Wrote("one two");
            seenInside = registry.Find("user-1", id.ToUpperInvariant());
            seenByOther = registry.Find("user-2", id);
            return Task.CompletedTask;
        });

        Assert.Equal(2, seenInside!.Snapshot().Words);
        Assert.Null(seenByOther);
        Assert.Null(registry.Find("user-1", id));     // gone once the request ended
        Assert.Null(AiProgress.Current);
        Assert.False(AiProgressRegistry.ValidId("../../etc"));
    }

    [Fact]
    public void Render_estimates_use_the_median_of_the_last_five()
    {
        var timings = new RenderTimings();
        Assert.Null(timings.Estimate("OpenVINO", "flux", true));

        foreach (var s in new[] { 900, 300, 320, 310, 330, 305 })   // a cold start, then usual renders
            timings.Add("OpenVINO", "flux", true, TimeSpan.FromSeconds(s));

        Assert.Equal(310, timings.Estimate("OpenVINO", "flux", true));   // the cold start dropped out, extremes ignored
        Assert.Null(timings.Estimate("OpenVINO", "flux", false));        // without the sketch is timed apart
    }
}
