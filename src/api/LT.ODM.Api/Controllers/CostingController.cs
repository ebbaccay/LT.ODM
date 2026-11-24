using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Costing;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Costing (roadmap 3.16): prices per IO from the ERP, by style. Reading: Admin, Merchandiser, Designer, Costing; setting quotes: not designers; the cost groups and net balance only for
/// Admin and Costing, because FOB minus the costs gives the net balance. Loading the ERP export: Admin. A style's prices are the style page's Prices by IO.
/// </summary>
[ApiController]
[Route("api/v1/costing")]
[Authorize(Roles = CostingRoles.Readers)]
public sealed class CostingController(ICostingRepository costing, ICostingWorkbookReader reader, IOptions<CostingOptions> options) : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int MaxIssuesShown = 50;
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";
    private bool ShowCost => User.IsInRole(CostingRoles.CostAdmin) || User.IsInRole(CostingRoles.CostTeam);
    private int SmallOrderQty => options.Value.SmallOrderQty;
    private const int MaxHintStyles = 100;

    private static StylePriceSummaryDto FobOnly(StylePriceSummaryDto s)
        => s with { AvgFabric = null, AvgTrims = null, AvgMaking = null, AvgOther = null, AvgNet = null };

    private static IoPriceDto FobOnly(IoPriceDto i)
        => i with { Fabric = null, Trims = null, Making = null, Other = null, NetBalance = null, PercToFob = null };

    [HttpGet("styles")]
    public async Task<CostingPageDto> List(string? search, string? customer, string? season, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var result = await costing.ListAsync(new CostingListQuery(search, customer, season, (Math.Max(page, 1) - 1) * pageSize, pageSize), SmallOrderQty, ct);
        return result with { ShowCost = ShowCost, Items = ShowCost ? result.Items : result.Items.Select(FobOnly).ToList() };
    }

    [HttpGet("styles/{styleId:int}")]
    public async Task<StylePricesDto> Style(int styleId, CancellationToken ct)
    {
        var result = await costing.GetStyleAsync(styleId, SmallOrderQty, ct);
        return ShowCost
            ? result with { ShowCost = true }
            : result with { Summaries = result.Summaries.Select(FobOnly).ToList(), Ios = result.Ios.Select(FobOnly).ToList() };
    }

    /// <summary>What each style's IO history says it costs (Similar styles, Concepts). At most 100 styles; styles with no prices anywhere are left out.</summary>
    [HttpPost("prices")]
    public async Task<IActionResult> Prices(PriceHintsRequest request, CancellationToken ct)
    {
        var ids = request.StyleIds?.Distinct().ToList() ?? [];
        if (ids.Count > MaxHintStyles) return Bad($"Ask for at most {MaxHintStyles} styles.");
        return Ok(ids.Count == 0 ? [] : await costing.PriceHintsAsync(ids, SmallOrderQty, ct));
    }

    /// <summary>A package's quotes: for each style what its IO history suggests, and its quotes; the client's price band.</summary>
    [HttpGet("/api/v1/style-packages/{packageId:int}/quotes")]
    public async Task<PackageQuotesDto> Quotes(int packageId, CancellationToken ct)
    {
        var quotes = await costing.PackageQuotesAsync(packageId, SmallOrderQty, ct);
        return ShowCost ? quotes : quotes with { Items = quotes.Items.Select(i => i with { Estimate = i.Estimate?.FobOnly() }).ToList() };
    }

    /// <summary>
    /// An estimated FOB for a garment (the product category codes of Concepts) for a client, from similar priced styles and the plant's cost per minute.
    /// 204 when no priced style is of that garment. The cost parts only for Admin and Costing.
    /// </summary>
    [HttpGet("estimate")]
    public async Task<IActionResult> Estimate(string? category, string? customer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 20) return Bad("Choose a product category.");
        var estimate = await costing.EstimateAsync(category.Trim().ToUpperInvariant(), string.IsNullOrWhiteSpace(customer) ? null : customer.Trim(), SmallOrderQty, ct);
        return estimate is null ? NoContent() : Ok(ShowCost ? estimate : estimate.FobOnly());
    }

    /// <summary>A new version of the style's quote (the same price and currency changes nothing).</summary>
    [HttpPut("/api/v1/style-packages/{packageId:int}/items/{packageItemId:int}/quote")]
    [Authorize(Roles = CostingRoles.QuoteEditors)]
    public async Task<IActionResult> SaveQuote(int packageId, int packageItemId, SaveQuoteRequest request, CancellationToken ct)
    {
        var errors = QuoteRules.Validate(request);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        try
        {
            await costing.SaveQuoteAsync(packageId, packageItemId, Math.Round(request.Fob!.Value, 5, MidpointRounding.AwayFromZero), request.Currency!.Trim().ToUpperInvariant(),
                request.BasisKind!, string.IsNullOrWhiteSpace(request.BasisText) ? null : request.BasisText.Trim(), CurrentUserName, ct);
            return NoContent();
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    // ----- Home dashboard panels -----

    /// <summary>Net balance share this many points below the style's own puts an IO on the overview's list.</summary>
    private const decimal NetGap = 5m;

    /// <summary>Price guide by garment: FOB and SMV only, so every price reader.</summary>
    [HttpGet("guide")]
    public Task<IReadOnlyList<PriceGuideRowDto>> Guide(CancellationToken ct) => costing.PriceGuideAsync(SmallOrderQty, ct);

    /// <summary>The signed-in user's concepts against their target prices (estimated FOB only).</summary>
    [HttpGet("my-concepts")]
    public Task<IReadOnlyList<ConceptCostDto>> MyConcepts(CancellationToken ct) => costing.MyConceptsAsync(CurrentUserName, SmallOrderQty, ct);

    /// <summary>Costing overview for Admin and Costing: data health, net balance watch, quotes needing attention.</summary>
    [HttpGet("overview")]
    [Authorize(Roles = CostingRoles.CostEditors)]
    public Task<CostingOverviewDto> Overview(CancellationToken ct) => costing.OverviewAsync(SmallOrderQty, NetGap, ct);

    // ----- Plant cost per minute: Admin and Costing (factory cost) -----

    [HttpGet("rates")]
    [Authorize(Roles = CostingRoles.CostEditors)]
    public Task<PlantRatesDto> Rates(bool withDeleted, CancellationToken ct) => costing.PlantRatesAsync(withDeleted, ct);

    [HttpPost("rates")]
    [Authorize(Roles = CostingRoles.CostEditors)]
    public Task<IActionResult> AddRate(SavePlantRateRequest request, CancellationToken ct) => SaveRate(null, request, ct);

    [HttpPut("rates/{rateId:int}")]
    [Authorize(Roles = CostingRoles.CostEditors)]
    public Task<IActionResult> UpdateRate(int rateId, SavePlantRateRequest request, CancellationToken ct) => SaveRate(rateId, request, ct);

    /// <summary>Marks the rate deleted: it stays listed with deleted rates and is never used again.</summary>
    [HttpDelete("rates/{rateId:int}")]
    [Authorize(Roles = CostingRoles.CostEditors)]
    public async Task<IActionResult> DeleteRate(int rateId, CancellationToken ct)
    {
        try
        {
            await costing.DeletePlantRateAsync(rateId, CurrentUserName, ct);
            return NoContent();
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    private async Task<IActionResult> SaveRate(int? rateId, SavePlantRateRequest r, CancellationToken ct)
    {
        var errors = PlantRateRules.Validate(r);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        try
        {
            var id = await costing.SavePlantRateAsync(rateId, r.PlantCode!.Trim().ToUpperInvariant(), r.EffectiveDate!.Value, r.MinOrderQty!.Value,
                r.SalesTerm!.Trim().ToUpperInvariant(), r.Currency!.Trim().ToUpperInvariant(), Math.Round(r.Cost!.Value, 5, MidpointRounding.AwayFromZero), CurrentUserName, ct);
            return Ok(new { rateId = id });
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    /// <summary>Loads the ERP costing export (.xlsx). A costing version already here is replaced, so the file can be loaded again.</summary>
    [HttpPost("imports")]
    [Authorize(Roles = CostingRoles.CostAdmin)]
    [RequestSizeLimit(StyleImportRules.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = StyleImportRules.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Bad("Choose the costing export (.xlsx) to upload.");
        if (file.Length > StyleImportRules.MaxFileBytes) return Bad("The workbook must be 100 MB or smaller.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return Bad("Only Excel workbooks (.xlsx) can be imported. Save the file as .xlsx and try again.");

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        if (buffer.Length < 4 || !buffer.GetBuffer().AsSpan(0, 4).SequenceEqual(ZipSignature)) return Bad("This file is not an Excel workbook (.xlsx).");

        CostingWorkbook workbook;
        try
        {
            buffer.Position = 0;
            workbook = reader.Read(buffer);
        }
        catch (Exception ex) when (ex is InvalidDataException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            return Bad("This workbook could not be read. Open it in Excel, save it as .xlsx and try again.");
        }
        if (workbook.Problems.Count > 0) return Bad(string.Join(" ", workbook.Problems));

        var name = Path.GetFileName(file.FileName);
        var result = await costing.ImportAsync(name.Length > 260 ? name[^260..] : name, workbook.Rows, CurrentUserName, ct);
        return Ok(result with { Issues = workbook.Issues.Take(MaxIssuesShown).ToList() });
    }

    private ObjectResult Bad(string title) => Problem(statusCode: StatusCodes.Status400BadRequest, title: title);
}
