using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Materials: the BOM seen by material - where each material is used, consumption per product type, suppliers,
/// colours and possible duplicates. Read only; same readers as Styles (Admin, Merchandiser, Costing, Viewer).
/// </summary>
[ApiController]
[Route("api/v1/materials")]
[Authorize(Roles = StyleRoles.Readers)]
public sealed class MaterialsController(IMaterialRepository materials) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<MaterialListItemDto>> List(string? search, string? contentClass, string? materialType, string? supplier,
        string? customer, string? season, string? sort, int skip = 0, int take = 50, CancellationToken ct = default)
        => materials.ListAsync(new MaterialListQuery(Clean(search, 100), Clean(contentClass, 8), Clean(materialType, 16), Clean(supplier, 32),
            Clean(customer, 32), Clean(season, 16), MaterialRules.Sorts.Contains(sort) ? sort! : "used", Math.Max(0, skip), Math.Clamp(take, 1, 200)), ct);

    [HttpGet("{materialId:int}")]
    public async Task<IActionResult> Get(int materialId, CancellationToken ct)
        => await materials.GetAsync(materialId, ct) is { } material ? Ok(material) : NotFound();

    // ----- Insights (views on the Materials page; customer / season / section narrow them where they apply) -----

    /// <summary>Trims, accessories, labels and packaging of one product type by share of its styles.</summary>
    [HttpGet("insights/standard-trims")]
    public async Task<IActionResult> StandardTrims(string? productType, string? customer, string? season, CancellationToken ct)
        => Clean(productType, 40) is { } pt
            ? Ok(await materials.StandardTrimsAsync(new MaterialInsightQuery(Clean(customer, 32), Clean(season, 16), null, pt), ct))
            : ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["productType"] = ["Choose a product type."] }));

    [HttpGet("insights/suppliers")]
    public Task<SupplierConcentrationDto> Suppliers(string? customer, string? season, string? contentClass, CancellationToken ct)
        => materials.SuppliersAsync(Query(customer, season, contentClass), ct);

    [HttpGet("insights/duplicates")]
    public Task<DuplicatesDto> Duplicates(string? contentClass, CancellationToken ct)
        => materials.DuplicatesAsync(Query(null, null, contentClass), ct);

    [HttpGet("insights/recycled")]
    public Task<RecycledShareDto> Recycled(string? customer, CancellationToken ct)
        => materials.RecycledAsync(Query(customer, null, null), ct);

    [HttpGet("insights/colours")]
    public Task<IReadOnlyList<ColourUsageDto>> Colours(string? customer, string? season, string? contentClass, CancellationToken ct)
        => materials.ColoursAsync(Query(customer, season, contentClass), ct);

    private static MaterialInsightQuery Query(string? customer, string? season, string? contentClass)
        => new(Clean(customer, 32), Clean(season, 16), Clean(contentClass, 8), null);

    private static string? Clean(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();
}
