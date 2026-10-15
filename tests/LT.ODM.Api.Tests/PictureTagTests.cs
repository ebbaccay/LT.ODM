using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>style.ImageTag in memory.</summary>
public sealed class FakePictureTagRepository : IPictureTagRepository
{
    public List<PictureToTagDto> ToTag { get; } = [];
    public List<PictureTagsDto> Saved { get; } = [];

    public Task<IReadOnlyList<PictureToTagDto>> ToTagAsync(string model, string version, int take, CancellationToken ct = default)
    {
        var batch = ToTag.Take(take).ToList();
        ToTag.RemoveAll(batch.Contains);
        return Task.FromResult<IReadOnlyList<PictureToTagDto>>(batch);
    }

    public Task<int> SaveAsync(IReadOnlyList<PictureTagsDto> items, string version, CancellationToken ct = default)
    {
        Saved.AddRange(items);
        return Task.FromResult(items.Count);
    }

    public Task<IReadOnlyList<PictureTagCountDto>> SummaryAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PictureTagCountDto>>([]);
}

public sealed class PictureTagTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    /// <summary>Each tag value its own direction, so a picture "is" exactly what its vector points at.</summary>
    private static Dictionary<string, float[]> Prompts()
    {
        var codes = PictureTagVocabulary.Codes.ToList();
        return codes.Select((c, i) => (c, v: Unit(i, codes.Count))).ToDictionary(x => x.c, x => x.v);
    }

    private static float[] Unit(int i, int n)
    {
        var v = new float[n];
        v[i] = 1;
        return v;
    }

    private static float[] Picture(Dictionary<string, float[]> prompts, params (string Tag, float Weight)[] parts)
    {
        var v = new float[prompts.First().Value.Length];
        foreach (var (tag, w) in parts)
            for (var i = 0; i < v.Length; i++) v[i] += w * prompts[tag][i];
        return v;
    }

    [Fact]
    public void Only_confident_tags_are_kept_and_bottoms_get_no_hood_or_closure()
    {
        var p = Prompts();
        var hoodie = PictureTagVocabulary.Tags(Picture(p, ("hood:hooded", 0.07f), ("closure:fullZip", 0.032f), ("closure:noZip", 0.026f),
            ("pattern:print", 0.06f), ("colour:navy", 0.069f), ("colour:blue", 0.067f)), bottom: false, p);
        var tags = hoodie.Select(t => $"{t.Attribute}:{t.Value}").ToList();
        Assert.Contains("hood:hooded", tags);
        Assert.DoesNotContain(tags, t => t.StartsWith("closure:"));                   // 0.006 ahead: not sure, not tagged
        Assert.Contains("colour:navy", tags);
        Assert.Contains("colour:blue", tags);                                         // two close colours: both

        var shorts = PictureTagVocabulary.Tags(Picture(p, ("hood:noHood", 0.05f), ("colour:blue", 0.04f)), bottom: true, p);
        Assert.Equal(["colour:blue"], shorts.Select(t => $"{t.Attribute}:{t.Value}"));

        Assert.True(PictureTagVocabulary.IsBottom("SHORTS (1/4)"));
        Assert.False(PictureTagVocabulary.IsBottom("T-SHIRT (SHORT SLEEVE)"));        // the bracket is a detail, not the garment
    }

    [Fact]
    public void The_phrase_list_reads_looks_after_product_types()
    {
        var l = new StyleLookupsDto([], [], [], [new("HOODED SWEAT", "HOODED SWEAT"), new("JACKET", "JACKET"), new("TSHIRTSL", "T-SHIRT (SHORT SLEEVE)")],
            [], [], [], [], [], []);
        var r = StyleQueryParser.Parse("navy hooded jackets with a full zip", l);
        Assert.True(r.Complete);
        Assert.Equal(["JACKET"], r.Filters.ProductTypes);
        Assert.Equal(["closure:fullZip", "hood:hooded", "colour:navy"], r.Filters.Looks!.Order().ToList().OrderBy(x => x.StartsWith("colour")).ToList());

        r = StyleQueryParser.Parse("hooded sweats", l);
        Assert.Equal(["HOODED SWEAT"], r.Filters.ProductTypes);                       // a product type, not a picture look
        Assert.Empty(r.Filters.Looks!);

        r = StyleQueryParser.Parse("black printed t-shirts", l);
        Assert.Equal(["colour:black", "pattern:print"], r.Filters.Looks!.Order());
    }

    [Fact]
    public async Task Tagging_runs_from_stored_vectors_without_an_ai_server()
    {
        var repo = new FakePictureTagRepository();
        var model = new FakeImageEmbeddingModel { IsAvailable = true };
        repo.ToTag.Add(new PictureToTagDto(1, Enumerable.Repeat(0.036f, ImageSearchRules.ImageDimensions).ToArray(), "JACKET"));
        Assert.Equal(1, await new PictureTagger(model, repo).TagAsync());
        Assert.Equal(1, Assert.Single(repo.Saved).ImageVectorId);

        model.TextAvailable = false;   // no text part: nothing to compare with, nothing tagged
        repo.ToTag.Add(new PictureToTagDto(2, [1f], null));
        Assert.Equal(0, await new PictureTagger(model, repo).TagAsync());
    }

    [Fact]
    public async Task Look_alike_questions_search_pictures_at_once_without_ai()
    {
        factory.ImageModel.IsAvailable = true;
        factory.TextAi.Calls.Clear();
        try
        {
            var client = await SignInAsync("Viewer");
            var a = await (await client.PostAsJsonAsync("/api/v1/ai/library-assistant/ask", new { question = "Show me styles that look like a navy hooded jacket?" }))
                .Content.ReadFromJsonAsync<LibraryAnswerDto>();
            Assert.Equal(("rules", "look_alike", "navy hooded jacket"), (a!.Source, a.Tool, a.Arguments["look"]));
            Assert.Empty(factory.TextAi.Calls);
            Assert.Contains("navy hooded jacket", factory.ImageModel.Texts);
        }
        finally
        {
            factory.ImageModel.IsAvailable = false;
        }
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "pt" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}
