using LT.ODM.Application.ConceptStudio;

namespace LT.ODM.Application.SignInPage;

/// <summary>
/// Look of the sign-in pages (Settings > Sign-in page): an optional background picture with focus point, blur and a
/// tint for legibility, the frosted-glass card, and which parts of the intro panel show. The texts are translation
/// keys (auth.shell.*, auth.login.*) changed through Settings > Translations, so they follow the UI language.
/// The top-level picture settings are the light-mode look (and the look in dark mode when <see cref="Dark"/> is null).
/// </summary>
/// <param name="BackgroundImage">Stored file name in the sign-in image store, or null for the default colour glow.</param>
/// <param name="Focus">Part of the picture kept in view when it is cropped: center, top, bottom, left, right.</param>
/// <param name="BackgroundBlur">Blur of the picture, 0-24 px.</param>
/// <param name="Overlay">Tint over the picture: none, dark, light, brand.</param>
/// <param name="OverlayStrength">Tint strength, 0-90 %.</param>
/// <param name="ShowGlow">The soft colour blobs and faint grid (over the picture too, when there is one).</param>
/// <param name="GlassOpacity">How solid the card is, 10-95 % (lower = more see-through).</param>
/// <param name="GlassBlur">Frosting of what shows through the card, 0-40 px.</param>
/// <param name="ShowIntro">The headline panel left of the card (wide screens only).</param>
/// <param name="ShowFeatures">The four feature tiles under the headline.</param>
/// <param name="IntroText">Colour of the headline panel text: auto (follows light/dark mode), light, dark.</param>
/// <param name="Dark">A separate picture look for dark mode, or null to use the light-mode look in both modes.</param>
public sealed record SignInAppearance(
    string? BackgroundImage = null,
    string Focus = "center",
    int BackgroundBlur = 0,
    string Overlay = "none",
    int OverlayStrength = 30,
    bool ShowGlow = true,
    int GlassOpacity = 55,
    int GlassBlur = 40,
    bool ShowIntro = true,
    bool ShowFeatures = true,
    string IntroText = "auto",
    SignInModeLook? Dark = null)
{
    public static SignInAppearance Default { get; } = new();

    /// <summary>Pictures in use (light and dark), for the stale-upload clean-up.</summary>
    public IReadOnlyList<string> Images => new[] { BackgroundImage, Dark?.BackgroundImage }.OfType<string>().ToList();
}

/// <summary>
/// The parts of the look that depend on the picture, set separately for dark mode: a dark picture needs light
/// headline text and usually a different tint, and the dark card over a light picture needs to be more solid.
/// <see cref="BackgroundImage"/> null = no picture in dark mode (the default dark gradient).
/// </summary>
public sealed record SignInModeLook(
    string? BackgroundImage = null,
    string Focus = "center",
    int BackgroundBlur = 0,
    string Overlay = "none",
    int OverlayStrength = 30,
    string IntroText = "auto",
    int GlassOpacity = 55);

/// <summary>What the sign-in page and the settings screen read: the appearance with the pictures' addresses.</summary>
public sealed record SignInAppearanceDto(
    string? BackgroundUrl, string? BackgroundImage, string Focus, int BackgroundBlur, string Overlay, int OverlayStrength, bool ShowGlow,
    int GlassOpacity, int GlassBlur, bool ShowIntro, bool ShowFeatures, string IntroText, SignInModeLookDto? Dark, string? UpdatedBy,
    DateTime? UpdatedUtc)
{
    public const string ImageUrlPrefix = "/api/v1/sign-in-page/images/";

    public static string? Url(string? image) => image is null ? null : ImageUrlPrefix + image;

    public static SignInAppearanceDto From(SignInAppearance a, string? updatedBy, DateTime? updatedUtc)
        => new(Url(a.BackgroundImage), a.BackgroundImage, a.Focus, a.BackgroundBlur, a.Overlay, a.OverlayStrength, a.ShowGlow,
            a.GlassOpacity, a.GlassBlur, a.ShowIntro, a.ShowFeatures, a.IntroText,
            a.Dark is { } d ? new SignInModeLookDto(Url(d.BackgroundImage), d.BackgroundImage, d.Focus, d.BackgroundBlur, d.Overlay, d.OverlayStrength, d.IntroText, d.GlassOpacity) : null,
            updatedBy, updatedUtc);
}

public sealed record SignInModeLookDto(
    string? BackgroundUrl, string? BackgroundImage, string Focus, int BackgroundBlur, string Overlay, int OverlayStrength, string IntroText, int GlassOpacity);

public static class SignInPageRules
{
    public static readonly string[] Focuses = ["center", "top", "bottom", "left", "right"];
    public static readonly string[] Overlays = ["none", "dark", "light", "brand"];
    public static readonly string[] IntroTexts = ["auto", "light", "dark"];

    /// <summary>Longest side of a stored background, in px: sharp on large screens, still quick to load.</summary>
    public const int MaxImageDimension = 2560;

    /// <summary>Field -> messages; empty when the appearance can be saved. Dark-mode fields are named dark.&lt;field&gt;.</summary>
    public static Dictionary<string, string[]> Validate(SignInAppearance a)
    {
        var errors = new Dictionary<string, string[]>();
        Picture(errors, "", a.Focus, a.BackgroundBlur, a.Overlay, a.OverlayStrength, a.IntroText, a.GlassOpacity);
        Range(errors, "glassBlur", a.GlassBlur, 0, 40);
        if (a.Dark is { } d) Picture(errors, "dark.", d.Focus, d.BackgroundBlur, d.Overlay, d.OverlayStrength, d.IntroText, d.GlassOpacity);
        return errors;
    }

    private static void Picture(Dictionary<string, string[]> errors, string prefix, string focus, int blur, string overlay, int strength, string introText, int glassOpacity)
    {
        if (!Focuses.Contains(focus)) errors[prefix + "focus"] = ["Choose center, top, bottom, left or right."];
        if (!Overlays.Contains(overlay)) errors[prefix + "overlay"] = ["Choose none, dark, light or brand."];
        if (!IntroTexts.Contains(introText)) errors[prefix + "introText"] = ["Choose auto, light or dark."];
        Range(errors, prefix + "backgroundBlur", blur, 0, 24);
        Range(errors, prefix + "overlayStrength", strength, 0, 90);
        Range(errors, prefix + "glassOpacity", glassOpacity, 10, 95);
    }

    private static void Range(Dictionary<string, string[]> errors, string field, int value, int min, int max)
    {
        if (value < min || value > max) errors[field] = [$"Use a value from {min} to {max}."];
    }
}

/// <summary>The saved appearance (one per installation), with who saved it last.</summary>
public interface ISignInAppearanceStore
{
    Task<(SignInAppearance Appearance, string? UpdatedBy, DateTime? UpdatedUtc)> GetAsync(CancellationToken ct = default);
    Task SaveAsync(SignInAppearance appearance, string updatedBy, CancellationToken ct = default);
}

/// <summary>Sign-in background pictures. Served without sign-in (the page shows before it), so it holds nothing else.</summary>
public interface ISignInImageStore : IImageStore
{
    /// <summary>Removes every stored picture except those in <paramref name="keep"/> (uploads tried and replaced, never used).</summary>
    void DeleteAllExcept(IReadOnlyCollection<string> keep);
}
