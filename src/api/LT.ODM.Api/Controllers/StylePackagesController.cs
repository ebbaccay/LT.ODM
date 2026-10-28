using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Style Packages: library styles offered to a client (db/procedures/pack.procedures.sql). Reading: Style Library readers.
/// Changing: Admin, Merchandiser, on any package. Only styles of the package's brand are added; another brand's style comes
/// back as OtherBrand (offered as a copy, roadmap 5.12). No prices here: costs attach to the item later (roadmap 5.19).
/// </summary>
[ApiController]
[Route("api/v1/style-packages")]
[Authorize(Roles = StyleRoles.Readers)]
public sealed class StylePackagesController(IPackageRepository packages, IShareTokenProtector tokenProtector) : ControllerBase
{
    private const int MaxStyles = 500;
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    // ----- Reads -----

    /// <summary>Packages, latest change first. mine = only packages the signed-in user owns.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? search, string? customer, string? season, string? status, bool mine = false, int skip = 0,
        int take = 50, CancellationToken ct = default)
    {
        string? st = null;
        if (!string.IsNullOrWhiteSpace(status) && (st = PackageValues.Parse(PackageValues.Statuses, status)) is null)
            return Invalid("status", "Choose a status from the list.");
        return Ok(await packages.ListAsync(new PackageListQuery(Clean(search, 100), Clean(customer, 32), Clean(season, 16), st, mine, CurrentUserName,
            Math.Max(0, skip), Math.Clamp(take, 1, 200)), ct));
    }

    [HttpGet("{packageId:int}")]
    public async Task<IActionResult> Get(int packageId, CancellationToken ct)
        => await packages.GetAsync(packageId, ct) is { } p ? Ok(p) : NotFound();

    /// <summary>
    /// The line sheet for the client: format pdf (cards with pictures) or xlsx (one row per style with its picture). Offered colourways and the
    /// note for the client only; internal notes and styles of another brand are left out.
    /// </summary>
    [HttpGet("{packageId:int}/line-sheet.{format}")]
    public async Task<IActionResult> LineSheet(int packageId, string format, [FromServices] IPackageLineSheetWriter writer, CancellationToken ct)
    {
        if (format is not ("pdf" or "xlsx")) return NotFound();
        if (await packages.GetAsync(packageId, ct) is not { } package) return NotFound();
        var name = $"{FileNameSafe(package.Package.Name)}_line_sheet";
        return format == "pdf"
            ? File(await writer.WritePdfAsync(package, CurrentUserName, DateTime.UtcNow, ct), "application/pdf", name + ".pdf")
            : File(await writer.WriteExcelAsync(package, CurrentUserName, DateTime.UtcNow, ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
    }

    /// <summary>
    /// What to offer a customer's brand, from the library alone (rules, no AI): its own styles not carried over since, garments
    /// it rarely bought with LT's examples, and styles on fabrics it already buys. packageId leaves out styles already in it.
    /// </summary>
    [HttpGet("offer")]
    public async Task<IActionResult> Offer(string? customer, int? packageId, int top = 12, CancellationToken ct = default)
    {
        if (Clean(customer, 32) is not { } code) return Invalid("customer", "Choose the customer to suggest styles for.");
        try
        {
            return Ok(await packages.OfferAsync(code, packageId, Math.Clamp(top, 1, 50), ct));
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    // ----- Hit rate (roadmap 5.17) -----

    /// <summary>
    /// Styles offered in packages sent from the day <paramref name="from"/> up to and including <paramref name="to"/>
    /// (yyyy-MM-dd, UTC; empty = no limit), optionally for one customer's brand: answers through share links and outcomes,
    /// in total and by garment, client and where the style was added from.
    /// </summary>
    [HttpGet("hit-rate")]
    public async Task<IActionResult> HitRate(string? customer, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from is { } f && to is { } t && t < f) return Invalid("to", "The end date is before the start date.");
        return Ok(await packages.HitRateAsync(Clean(customer, 32), from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct));
    }

    /// <summary>What became of a style offered: Sampling, Ordered, Dropped, or empty (not known yet).</summary>
    [HttpPut("{packageId:int}/items/{packageItemId:int}/outcome")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> SetOutcome(int packageId, int packageItemId, SetOutcomeRequest request, CancellationToken ct)
    {
        string? outcome = null;
        if (!string.IsNullOrWhiteSpace(request.Outcome) && (outcome = PackageValues.Parse(PackageValues.Outcomes, request.Outcome)) is null)
            return Task.FromResult(Invalid("outcome", "Choose Sampling, Ordered or Dropped."));
        return Run([], async () =>
        {
            await packages.SetOutcomeAsync(packageId, packageItemId, outcome, CurrentUserName, ct);
            return NoContent();
        });
    }

    // ----- Share links (the client opens the package without signing in; /share/{token}) -----

    /// <summary>A package's links with their views and the client's answers.</summary>
    [HttpGet("{packageId:int}/shares")]
    public Task<IReadOnlyList<PackageShareDto>> Shares(int packageId, CancellationToken ct) => packages.ListSharesAsync(packageId, ct);

    /// <summary>
    /// Makes a link for the client (no sign-in): kept as its hash (lookups) and encrypted (to copy it again). A draft package
    /// becomes Sent. The link works for ExpiresInDays (1-90, default 30) or until revoked.
    /// </summary>
    [HttpPost("{packageId:int}/shares")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> CreateShare(int packageId, CreateShareRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var label = Clean(request.Label, 151);
        if (label is null) errors["label"] = ["Say who the link is for (e.g. the buying team)."];
        else if (label.Length > 150) errors["label"] = ["Keep it under 150 characters."];
        if (request.ExpiresInDays is { } d && d is < 1 or > 90) errors["expiresInDays"] = ["A link works for 1 to 90 days."];
        return Run(errors, async () =>
        {
            var (token, hash) = ShareTokens.New();
            var expires = DateTime.UtcNow.AddDays(request.ExpiresInDays ?? 30);
            var shareId = await packages.CreateShareAsync(packageId, hash, tokenProtector.Protect(token), label!, expires, CurrentUserName, ct);
            return Ok(new CreatedShareDto(shareId, token, expires));
        });
    }

    /// <summary>
    /// An active link again, to re-send it (editors). 404 when it is revoked or expired, or was made before links were kept
    /// (then make a new one).
    /// </summary>
    [HttpGet("{packageId:int}/shares/{shareId:int}/link")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> ShareLink(int packageId, int shareId, CancellationToken ct)
    {
        if (await packages.ShareTokenAsync(packageId, shareId, ct) is not { } protectedToken || tokenProtector.Unprotect(protectedToken) is not { } token)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "This link cannot be shown again. Make a new link for the client.");
        Response.Headers.CacheControl = "no-store";
        return Ok(new ShareLinkDto(token));
    }

    [HttpPost("{packageId:int}/shares/{shareId:int}/revoke")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> RevokeShare(int packageId, int shareId, CancellationToken ct)
        => Run([], async () =>
        {
            await packages.RevokeShareAsync(packageId, shareId, CurrentUserName, ct);
            return NoContent();
        });

    /// <summary>The packages a style is in (style page).</summary>
    [HttpGet("for-style/{styleId:int}")]
    public Task<IReadOnlyList<StylePackageDto>> ForStyle(int styleId, CancellationToken ct) => packages.ForStyleAsync(styleId, ct);

    // ----- Packages -----

    [HttpPost]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Create(SavePackageRequest request, CancellationToken ct)
        => Run(Validate(request), async () => Ok(new { packageId = await packages.SaveAsync(null, Normalise(request) with { RowVer = null }, CurrentUserName, ct) }));

    [HttpPut("{packageId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Update(int packageId, SavePackageRequest request, CancellationToken ct)
    {
        var errors = Validate(request);
        if (request.RowVer is not { Length: 8 }) errors["rowVer"] = ["Reload the package and try again."];
        return Run(errors, async () => Ok(new { packageId = await packages.SaveAsync(packageId, Normalise(request), CurrentUserName, ct) }));
    }

    [HttpDelete("{packageId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Delete(int packageId, string? rowVer, CancellationToken ct)
    {
        byte[]? value = null;
        try { if (!string.IsNullOrEmpty(rowVer)) value = Convert.FromBase64String(rowVer); } catch (FormatException) { }
        if (value is not { Length: 8 } v) return Task.FromResult(Invalid("rowVer", "Reload the package and try again."));
        return Run([], async () =>
        {
            await packages.DeleteAsync(packageId, v, CurrentUserName, ct);
            return NoContent();
        });
    }

    // ----- Items -----

    /// <summary>Adds styles to the end of the package; one result per style (Added, AlreadyIn, OtherBrand, NotFound).</summary>
    [HttpPost("{packageId:int}/items")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> AddItems(int packageId, AddPackageItemsRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.StyleIds is not { Count: > 0 and <= MaxStyles }) errors["styleIds"] = [$"Choose 1 to {MaxStyles} styles."];
        string? from = null;
        if (!string.IsNullOrWhiteSpace(request.AddedFrom) && (from = PackageValues.Parse(PackageValues.AddedFrom, request.AddedFrom)) is null)
            errors["addedFrom"] = ["Unknown place the styles were added from."];
        return Run(errors, async () => Ok(await packages.AddItemsAsync(packageId, request.StyleIds!, from, CurrentUserName, ct)));
    }

    /// <summary>
    /// Offers another customer's style as a new style of the package's customer (season and style number from the user) and
    /// adds it. Only know-how goes across (BOM fabrics, trims, accessories; product facts); see pack.usp_PackageItem_AddCopy.
    /// </summary>
    [HttpPost("{packageId:int}/items/copy")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> AddCopy(int packageId, CopyToPackageRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var season = request.SeasonCode?.Trim().ToUpperInvariant();
        var styleNo = request.StyleNo?.Trim();
        if (string.IsNullOrEmpty(season)) errors["seasonCode"] = ["Choose the season of the new style."];
        else if (season.Length > 16) errors["seasonCode"] = ["A season code has at most 16 characters."];
        if (string.IsNullOrEmpty(styleNo)) errors["styleNo"] = ["Give the new style its own style number."];
        else if (styleNo.Length > 100) errors["styleNo"] = ["A style number has at most 100 characters."];
        return Run(errors, async () => Ok(await packages.AddCopyAsync(packageId, request.StyleId, season!, styleNo!, CurrentUserName, ct)));
    }

    /// <summary>Notes and offered colourways of one style in the package.</summary>
    [HttpPut("{packageId:int}/items/{packageItemId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> SaveItem(int packageId, int packageItemId, SavePackageItemRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.RowVer is not { Length: 8 }) errors["rowVer"] = ["Reload the package and try again."];
        if (request.PitchNote?.Trim().Length > 1000) errors["pitchNote"] = ["Keep the note for the client under 1,000 characters."];
        if (request.InternalNote?.Trim().Length > 1000) errors["internalNote"] = ["Keep the internal note under 1,000 characters."];
        if (!request.AllColorways && request.ColorwayCodes is not { Count: > 0 }) errors["colorwayCodes"] = ["Choose at least one colourway, or offer all colourways."];
        if (request.ColorwayCodes is { Count: > 200 }) errors["colorwayCodes"] = ["Choose at most 200 colourways."];
        return Run(errors, async () =>
        {
            await packages.SaveItemAsync(packageId, packageItemId, request with { ColorwayCodes = request.AllColorways ? [] : request.ColorwayCodes }, CurrentUserName, ct);
            return NoContent();
        });
    }

    [HttpPost("{packageId:int}/items/remove")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> RemoveItems(int packageId, PackageItemIdsRequest request, CancellationToken ct)
        => Run(ItemIds(request), async () =>
        {
            await packages.RemoveItemsAsync(packageId, request.PackageItemIds!, CurrentUserName, ct);
            return NoContent();
        });

    /// <summary>Puts the items in this order; items not listed keep their order after these.</summary>
    [HttpPut("{packageId:int}/items/order")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> ReorderItems(int packageId, PackageItemIdsRequest request, CancellationToken ct)
        => Run(ItemIds(request), async () =>
        {
            await packages.ReorderItemsAsync(packageId, request.PackageItemIds!, CurrentUserName, ct);
            return NoContent();
        });

    // ----- Helpers -----

    private static Dictionary<string, string[]> Validate(SavePackageRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name)) errors["name"] = ["Give the package a name."];
        else if (r.Name.Trim().Length > 150) errors["name"] = ["Keep the name under 150 characters."];
        if (string.IsNullOrWhiteSpace(r.CustomerCode)) errors["customerCode"] = ["Choose the customer the package is for."];
        else if (r.CustomerCode.Trim().Length > 32) errors["customerCode"] = ["A customer code has at most 32 characters."];
        if (r.SeasonCode?.Trim().Length > 16) errors["seasonCode"] = ["A season code has at most 16 characters."];
        if (r.Description?.Trim().Length > 2000) errors["description"] = ["Keep the description under 2,000 characters."];
        if (!string.IsNullOrWhiteSpace(r.Status) && PackageValues.Parse(PackageValues.Statuses, r.Status) is null) errors["status"] = ["Choose a status from the list."];
        if (r.OwnerName?.Trim().Length > 64) errors["ownerName"] = ["A user name has at most 64 characters."];
        return errors;
    }

    /// <summary>Trimmed codes (customer and season upper case, as the library stores them) and the status as the database spells it.</summary>
    private static SavePackageRequest Normalise(SavePackageRequest r)
        => r with
        {
            Name = r.Name!.Trim(), CustomerCode = r.CustomerCode!.Trim().ToUpperInvariant(), SeasonCode = Clean(r.SeasonCode, 16)?.ToUpperInvariant(),
            Description = Clean(r.Description, 2000), Status = PackageValues.Parse(PackageValues.Statuses, r.Status) ?? "Draft", OwnerName = Clean(r.OwnerName, 64),
        };

    private static Dictionary<string, string[]> ItemIds(PackageItemIdsRequest r)
        => r.PackageItemIds is { Count: > 0 and <= MaxStyles } ? [] : new() { ["packageItemIds"] = [$"Choose 1 to {MaxStyles} styles in the package."] };

    private static string FileNameSafe(string text) => string.Concat(text.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    private static string? Clean(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();

    private IActionResult Invalid(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    /// <summary>Validation errors -> 400 ValidationProblem; database rules -> 400 / 404 / 409 Problem with the reason.</summary>
    private async Task<IActionResult> Run(Dictionary<string, string[]> errors, Func<Task<IActionResult>> action)
    {
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        try
        {
            return await action();
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}
