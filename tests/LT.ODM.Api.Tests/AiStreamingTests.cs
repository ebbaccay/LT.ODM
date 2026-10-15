using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LT.ODM.Application.Ai;
using LT.ODM.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace LT.ODM.Api.Tests;

/// <summary>
/// OpenAI-compatible answers are streamed and read only up to the end of the first JSON value: some servers (OpenVINO
/// Model Server 2026.4 with Qwen2.5, seen 2026-10-13) do not end the answer after the JSON and would run to the timeout.
/// </summary>
public sealed class AiStreamingTests
{
    private static readonly AiConnection Server = new("In-house", AiConnectionKinds.OpenAiCompatible, "http://ai.test/v1", null, "qwen2.5-7b", true, 30, "settings");

    private static string Chunk(string content) => "data: " + new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = content } }),
    }.ToJsonString() + "\n\n";

    /// <summary>An event stream: the given chunks, then (endless) noise that is produced only if someone keeps reading.</summary>
    private sealed class SseStream(IEnumerable<string> chunks, bool endless) : Stream
    {
        private readonly IEnumerator<string> _parts = chunks.GetEnumerator();
        private byte[] _current = [];
        private int _pos;
        public int NoiseChunksRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _current.Length)
            {
                if (_parts.MoveNext()) _current = Encoding.UTF8.GetBytes(_parts.Current);
                else if (endless) { _current = Encoding.UTF8.GetBytes(Chunk("GuidId: 1234567890abcdef ")); NoiseChunksRead++; }
                else return 0;
                _pos = 0;
            }
            var n = Math.Min(count, _current.Length - _pos);
            Array.Copy(_current, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public JsonNode? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            return respond();
        }
    }

    private static HttpResponseMessage EventStream(Stream body)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static Task<string> Ask(Handler handler)
        => OpenAiApi.GenerateJsonAsync(new HttpClient(handler), Server, NullLogger.Instance, "test", "system", "user",
            new JsonObject { ["type"] = "object" }, 0.1, CancellationToken.None);

    [Fact]
    public async Task Reading_stops_at_the_end_of_the_json_even_when_the_server_goes_on()
    {
        var stream = new SseStream([Chunk("{\"season\":\"SS27\","), Chunk("\"gender\":\"men"), Chunk("\"}"), Chunk("GuidId: 12")], endless: true);
        var handler = new Handler(() => EventStream(stream));

        var answer = await Ask(handler).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("{\"season\":\"SS27\",\"gender\":\"men\"}", answer);
        Assert.Equal(0, stream.NoiseChunksRead);                                   // nothing read after the JSON
        Assert.True((bool)handler.LastBody!["stream"]!);
        Assert.Equal(OpenAiApi.MaxAnswerTokens, (int)handler.LastBody!["max_tokens"]!);
    }

    [Fact]
    public async Task Braces_inside_strings_and_reasoning_do_not_end_the_answer_early()
    {
        var stream = new SseStream(
        [
            Chunk("<think>the user wants {filters} and [codes]"), Chunk("</think>"),
            Chunk("{\"note\":\"a } in text \\\" and { more\","), Chunk("\"list\":[{\"a\":1},{\"b\":2}]}"),
        ], endless: true);

        var answer = await Ask(new Handler(() => EventStream(stream))).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("{\"note\":\"a } in text \\\" and { more\",\"list\":[{\"a\":1},{\"b\":2}]}", answer);
        Assert.Equal(0, stream.NoiseChunksRead);
    }

    [Fact]
    public async Task Servers_that_ignore_streaming_are_read_as_before_and_lose_trailing_text()
    {
        var body = new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = "{\"a\":1} and then more words" } }),
        };
        var handler = new Handler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") });

        Assert.Equal("{\"a\":1}", await Ask(handler));
    }

    [Fact]
    public async Task An_error_in_the_stream_or_a_stream_without_json_is_reported()
    {
        var error = new SseStream(["data: {\"error\":\"Mediapipe execution failed\"}\n\n"], endless: false);
        await Assert.ThrowsAsync<AiServiceException>(() => Ask(new Handler(() => EventStream(error))));

        // A stream that ends without any JSON: the caller gets the text, which then fails to parse (no endless wait).
        var noJson = new SseStream([Chunk("I cannot help with that."), "data: [DONE]\n\n"], endless: false);
        Assert.Equal("I cannot help with that.", await Ask(new Handler(() => EventStream(noJson))));
    }

    [Fact]
    public async Task A_model_stuck_inside_one_value_is_stopped_with_a_readable_error()
    {
        // What OpenVINO Model Server 2026.4 + Qwen2.5 did with a vague instruction: the gender value never closes.
        var stream = new SseStream([Chunk("{\"season\":\"SS27\",\"gender\":\"men','material':'recycled polyester'}")], endless: true);

        var error = await Assert.ThrowsAsync<AiServiceException>(() => Ask(new Handler(() => EventStream(stream))).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Contains("ran on", error.Message);
        Assert.InRange(stream.NoiseChunksRead * 25, OpenAiApi.MaxStringLength - 100, OpenAiApi.MaxStringLength + 100);   // stopped near the limit
    }

    /// <summary>A stream the server breaks off mid-answer (it stopped or ran out of memory, seen 2026-10-13).</summary>
    private sealed class BrokenStream : Stream
    {
        private bool _sent;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_sent) throw new IOException("Unable to read data from the transport connection: An established connection was aborted.");
            _sent = true;
            var part = Encoding.UTF8.GetBytes(Chunk("{\"gender\":\"M"));
            Array.Copy(part, 0, buffer, offset, part.Length);
            return part.Length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_connection_broken_mid_answer_is_a_readable_ai_error_and_caps_are_sent()
    {
        var error = await Assert.ThrowsAsync<AiServiceException>(() => Ask(new Handler(() => EventStream(new BrokenStream()))));
        Assert.Contains("stopped answering", error.Message);

        var handler = new Handler(() => EventStream(new SseStream([Chunk("{\"a\":1}")], endless: false)));
        await OpenAiApi.GenerateJsonAsync(new HttpClient(handler), Server, NullLogger.Instance, "test", "s", "u", new JsonObject { ["type"] = "object" }, 0.1,
            CancellationToken.None, maxTokens: 500);
        Assert.Equal(500, (int)handler.LastBody!["max_tokens"]!);
    }

    [Theory]
    [InlineData("{\"a\":1}GuidId: 123", "{\"a\":1}")]
    [InlineData("Here you go: [1,2,{\"x\":\"]\"}] thanks", "[1,2,{\"x\":\"]\"}]")]
    [InlineData("{\"unfinished\": [1, 2", "{\"unfinished\": [1, 2")]
    public void Trailing_text_after_the_json_is_dropped(string raw, string expected)
        => Assert.Equal(expected, OpenAiApi.CleanJson(raw));
}
