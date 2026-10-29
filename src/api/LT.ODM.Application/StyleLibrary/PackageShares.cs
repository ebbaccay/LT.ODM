using System.Security.Cryptography;

namespace LT.ODM.Application.StyleLibrary;

// ----- Share links (roadmap 5.16): the client opens a package without signing in -----

/// <summary>
/// Share link secrets: 32 random bytes written as 43 URL-safe characters. Looked up by their SHA-256; also kept encrypted
/// with the web server's keys (<see cref="IShareTokenProtector"/>) so an editor can copy an active link again. The database
/// alone cannot read a link.
/// </summary>
public static class ShareTokens
{
    public const int Length = 43;

    public static (string Token, byte[] Hash) New()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));

    /// <summary>Has the shape of a link secret (checked before the database is asked).</summary>
    public static bool LooksValid(string? token)
        => token is { Length: Length } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>Who the link is for (e.g. "Skechers men's buying team") and how many days it works (1-90; default 30).</summary>
public sealed record CreateShareRequest(string? Label, int? ExpiresInDays);

/// <summary>Encrypts link secrets with the web server's keys (ASP.NET Data Protection); null when a value cannot be read back.</summary>
public interface IShareTokenProtector
{
    string Protect(string token);
    string? Unprotect(string protectedToken);
}

/// <summary>A link's secret, for the page address /share/{Token} (made now, or copied again).</summary>
public sealed record CreatedShareDto(int ShareId, string Token, DateTime ExpiresUtc);

/// <summary>A package's link with its use: views (openings of the page) and the client's answers.</summary>
public sealed record PackageShareDto(
    int ShareId, string Label, DateTime ExpiresUtc, DateTime? RevokedUtc, string? RevokedBy, int Views, DateTime? FirstViewedUtc, DateTime? LastViewedUtc,
    string CreatedBy, DateTime CreatedUtc, bool Active, int Likes, int Passes, int Comments, bool CanCopy = false);

/// <summary>An active link's secret, copied again (the page address is /share/{Token}).</summary>
public sealed record ShareLinkDto(string Token);

/// <summary>A client's answer on a style through one link (shown on the package page). Sample: they asked for a sample (SampleNote: sizes, colours, quantity).</summary>
public sealed record PackageFeedbackDto(
    int ShareId, string Label, string? Verdict, string? Comment, string? Name, DateTime UpdatedUtc, bool Sample = false, string? SampleNote = null);

/// <summary>A live link, as found by its secret.</summary>
public sealed record OpenedShare(int ShareId, int PackageId, string Label, DateTime ExpiresUtc, IReadOnlyList<ShareAnswer> Answers);

/// <summary>The client's answer on one style through this link; Sample: a sample asked for, with SampleNote.</summary>
public sealed record ShareAnswer(
    int PackageItemId, string? Verdict, string? Comment, string? Name, DateTime UpdatedUtc, bool Sample = false, string? SampleNote = null);

/// <summary>
/// A notice to an LT user (the bell): a client answered through a share link. Gathers the answers given within 10 minutes
/// while unread (likes, passes, new or changed comments, sample requests); ClientName is the name the client typed.
/// </summary>
public sealed record PackageNoticeDto(
    int NoticeId, int PackageId, string PackageName, string ShareLabel, int Likes, int Passes, int Comments, string? ClientName, DateTime FirstUtc,
    DateTime LastUtc, bool IsRead = false, int Samples = 0);

/// <summary>A notice and the user it is for (to push it to them).</summary>
public sealed record PackageNoticeFor(string UserName, PackageNoticeDto Notice);

/// <summary>Sends notices to the users' open app pages (live), keyed by user name.</summary>
public interface IPackageNotifier
{
    Task NotifyAsync(IReadOnlyList<PackageNoticeFor> notices, CancellationToken ct = default);
}

/// <summary>
/// The person's whole answer on a style: Like / Pass / null, a comment, and Sample (asks for a sample; SampleNote: sizes, colours,
/// quantity). All empty removes the answer. Name: typed by the client.
/// </summary>
public sealed record ShareFeedbackRequest(string? Verdict, string? Comment, string? Name, bool? Sample = null, string? SampleNote = null);

// ----- What the client sees (never internal notes, never another brand's style) -----

/// <summary>A colourway offered. ImageFile: the stored picture's file name (served through the link), or null.</summary>
public sealed record SharedColorwayDto(string Code, string? Name, string? ImageFile);

/// <summary>A style on the client's page. Fabrics carry " (recycled)" when written so; ImageFile is the photo, else the sketch.</summary>
public sealed record SharedItemDto(
    int PackageItemId, string StyleNo, string SeasonCode, string? ModelName, string? Description, string Facts, string? ImageFile, int? LeadTimeDays,
    IReadOnlyList<string> Fabrics, IReadOnlyList<string> Trims, string? Recycled, IReadOnlyList<SharedColorwayDto> Colorways, string? Note,
    ShareAnswer? Answer);

/// <summary>A concept's story page (roadmap 5.22): the text for the client, trend tags, its pictures for the client (files), and its styles here.</summary>
public sealed record SharedStoryDto(string Name, string Story, string? Tags, IReadOnlyList<string> ImageFiles, IReadOnlyList<int> PackageItemIds);

public sealed record SharedPackageDto(
    string Name, string CustomerName, string? SeasonCode, string? Description, string Label, DateTime ExpiresUtc, IReadOnlyList<SharedItemDto> Items,
    IReadOnlyList<SharedStoryDto>? Stories = null);

public static class SharedPackageView
{
    private const string ImagePrefix = "/api/v1/styles/images/";

    /// <summary>
    /// The client's page: the line sheet's content (<see cref="LineSheetContent"/>) with the answers of the person visiting
    /// (the name typed, any case; none = the link's shared answers).
    /// </summary>
    public static SharedPackageDto Build(PackageDetailDto package, OpenedShare share, string? voter = null)
    {
        var p = package.Package;
        var key = VoterKey(voter);
        var answers = share.Answers.Where(a => VoterKey(a.Name) == key).GroupBy(a => a.PackageItemId).ToDictionary(g => g.Key, g => g.First());
        var items = LineSheetContent.Items(package);
        var stories = LineSheetContent.Stories(package).Select(s => new SharedStoryDto(s.Name, s.Story, s.Tags,
            s.Pictures.Select(x => StoryImageFile(x.ImageUrl)).OfType<string>().ToList(), s.PackageItemIds)).ToList();
        return new SharedPackageDto(p.Name, p.CustomerName, p.SeasonCode, p.Description, share.Label, share.ExpiresUtc,
            items.Select(i => new SharedItemDto(
                i.PackageItemId, i.StyleNo, i.SeasonCode, i.ModelName, i.Description, LineSheetContent.Facts(i), ImageFile(i.ImageUrl ?? i.SketchUrl),
                i.GarmentLeadTimeDays is { } d && d > 0 ? d : null,
                i.Materials.Where(m => m.Kind == "Fabric").Select(m => LineSheetContent.MaterialText(m) + (m.Recycled ? " (recycled)" : "")).ToList(),
                i.Materials.Where(m => m.Kind == "Trim").Select(LineSheetContent.MaterialText).ToList(),
                LineSheetContent.Recycled(i),
                LineSheetContent.Colorways(i).Select(c => new SharedColorwayDto(c.ColorwayCode, c.ColorwayName, ImageFile(c.ImageUrl))).ToList(),
                i.PitchNote, answers.GetValueOrDefault(i.PackageItemId))).ToList(), stories);
    }

    private const string StoryImagePrefix = "/api/v1/concepts/images/";

    /// <summary>The stored file name of a concept picture address, or null for any other address.</summary>
    public static string? StoryImageFile(string? url)
        => url is not null && url.StartsWith(StoryImagePrefix, StringComparison.OrdinalIgnoreCase) ? url[StoryImagePrefix.Length..].Split('?')[0] : null;

    public static string StoryImageUrl(string file) => StoryImagePrefix + file;

    /// <summary>One answer per person: the typed name trimmed, in lower case ("" = no name).</summary>
    public static string VoterKey(string? name) => (name ?? "").Trim().ToLowerInvariant();

    /// <summary>The stored file name of a style image address, or null for any other address.</summary>
    public static string? ImageFile(string? url)
        => url is not null && url.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase) ? url[ImagePrefix.Length..].Split('?')[0] : null;

    public static string ImageUrl(string file) => ImagePrefix + file;
}
