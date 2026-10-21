using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>
/// Style 1: tape 80020552 and label 62754732 in colorways BLACK (10) and Collegiate Navy (11); the label's BLACK cell is
/// already filled. History: the tape in BLACK colorways was "BLK01" (adidas 8 lines) and "BLACK" (another customer 30);
/// in navy colorways too few lines; the label has always been "WHT".
/// </summary>
public sealed class FakeColourSuggestionRepository : IColourSuggestionRepository
{
    public List<ColourCellRequest> Applied { get; } = [];

    public Task<(IReadOnlyList<ColourCell> Cells, IReadOnlyList<ColourByName> ByName, IReadOnlyList<ColourByMaterial> ByMaterial)?> SourcesAsync(
        int styleId, CancellationToken ct = default)
    {
        if (styleId == 404) return Task.FromResult<(IReadOnlyList<ColourCell>, IReadOnlyList<ColourByName>, IReadOnlyList<ColourByMaterial>)?>(null);
        IReadOnlyList<ColourCell> cells =
        [
            new(1, 10, "80020552", "JX1", "BLACK", "BLACK", null, null),
            new(1, 11, "80020552", "JX2", "Collegiate Navy", "COLLEGIATENAVY", null, null),
            new(2, 10, "62754732", "JX1", "BLACK", "BLACK", "WHT", "white"),
            new(2, 11, "62754732", "JX2", "Collegiate Navy", "COLLEGIATENAVY", null, null),
        ];
        IReadOnlyList<ColourByName> byName =
        [
            new("80020552", "BLACK", "BLK01", "black", 8, 8),
            new("80020552", "BLACK", "BLACK", null, 30, 0),
            new("80020552", "COLLEGIATENAVY", "NVY", null, 1, 1),
        ];
        IReadOnlyList<ColourByMaterial> byMaterial =
        [
            new("80020552", "BLK01", "black", 8, 8), new("80020552", "BLACK", null, 30, 0), new("80020552", "NVY", null, 1, 1),
            new("62754732", "WHT", "white", 40, 12),
        ];
        return Task.FromResult<(IReadOnlyList<ColourCell>, IReadOnlyList<ColourByName>, IReadOnlyList<ColourByMaterial>)?>((cells, byName, byMaterial));
    }

    public Task<int> ApplyAsync(int styleId, IReadOnlyList<ColourCellRequest> cells, string changedBy, CancellationToken ct = default)
    {
        Applied.AddRange(cells);
        return Task.FromResult(cells.Count);
    }
}

public sealed class ColourSuggestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    [Fact]
    public async Task Suggests_the_customers_colour_for_the_colorway_name_and_one_colour_materials_only_for_empty_cells()
    {
        var s = (await new ColourSuggestionService(new FakeColourSuggestionRepository()).SuggestAsync(1))!;
        Assert.Equal((4, 3), (s.Cells, s.Empty));

        // Tape in BLACK: adidas's own colour wins over the more common other customer's, 8 of 8 -> ticked.
        var tapeBlack = s.Suggestions.Single(x => x.BomLineId == 1 && x.ColorwayId == 10);
        Assert.Equal(("BLK01", "colorway", true, 100, true), (tapeBlack.MaterialColorCode, tapeBlack.Basis, tapeBlack.SameCustomer, tapeBlack.Confidence, tapeBlack.Recommended));

        // Tape in navy: one line seen, and the tape comes in several colours -> nothing.
        Assert.DoesNotContain(s.Suggestions, x => x.BomLineId == 1 && x.ColorwayId == 11);

        // The label always comes in white -> suggested for its empty navy cell; its filled BLACK cell is left alone.
        var label = Assert.Single(s.Suggestions, x => x.BomLineId == 2);
        Assert.Equal((11, "WHT", "material"), (label.ColorwayId, label.MaterialColorCode, label.Basis));
    }

    [Fact]
    public async Task Editors_get_and_fill_colours_viewers_cannot()
    {
        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/styles/1/colour-suggestion")).StatusCode);

        var editor = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync("/api/v1/styles/404/colour-suggestion")).StatusCode);
        var s = await editor.GetFromJsonAsync<ColourSuggestionDto>("/api/v1/styles/1/colour-suggestion");
        Assert.NotEmpty(s!.Suggestions);

        var cells = s.Suggestions.Select(x => new { x.BomLineId, x.ColorwayId, x.MaterialColorCode, x.MaterialColorDescription }).ToList();
        var response = await editor.PostAsJsonAsync("/api/v1/styles/1/colour-suggestion/apply", new { cells });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(cells.Count, factory.ColourSuggestions.Applied.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/v1/styles/1/colour-suggestion/apply", new { cells = Array.Empty<object>() })).StatusCode);
    }

    private async Task<HttpClient> SignInAsync(params string[] roles)
    {
        var name = "cs" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}
