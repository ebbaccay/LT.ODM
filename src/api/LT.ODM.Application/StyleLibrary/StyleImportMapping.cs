namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// One staging column the import can fill: its staging name, the template heading, where it ends up in the library,
/// the headings it is found under automatically (normalised: lower case, letters and digits only), and its size.
/// </summary>
public sealed record ImportTargetColumn(
    string Name, string Heading, string Destination, string[] Aliases, int MaxLength, bool Required = false, bool IsDate = false);

/// <summary>One of the three sheets the import needs. Key is Style, Article or Bom; Names are the sheet names found automatically.</summary>
public sealed record ImportTargetSheet(string Key, string Label, string[] Names, ImportTargetColumn[] Columns);

/// <summary>
/// The Style Library workbook as the import sees it. Column order = staging table column order after BatchId, RowNo
/// (db/tables/style.tables.sql); columns added later are at the end of each list.
/// </summary>
public static class StyleImportLayout
{
    public static readonly ImportTargetSheet Style = new("Style", "Style Header", ["styleheader", "style", "styles"],
    [
        new("Cust", "Cust", "Customer code", ["cust", "customer", "customerid"], 32, Required: true),
        new("SeasonId", "Season_ID", "Season", ["seasonid", "season"], 16, Required: true),
        new("StyleId", "Style_ID", "Style number", ["styleid", "style", "styleno"], 100, Required: true),
        new("Description", "Description", "Style description", ["description", "styledesc"], 400),
        new("ModelId", "Model_id", "Model code", ["modelid", "model"], 60),
        new("ModelName", "Model_Name", "Model name", ["modelname"], 100),
        new("WeaveTypeId", "WeaveType_ID", "Weave type code", ["weavetypeid", "weavetype"], 8),
        new("WeaveTypeDesc", "WeaveType_desc", "Weave type name (new codes)", ["weavetypedesc"], 50),
        new("ProdTypeId", "Prod_type_id", "Product type code", ["prodtypeid", "producttypeid", "producttype"], 40),
        new("ProdTypeDesc", "ProdType_Desc", "Product type name (new codes)", ["prodtypedesc", "producttypedesc"], 100),
        new("Gender", "Gender", "Gender", ["gender"], 16),
        new("LeadTime", "Garment_Lead_time", "Garment lead time (days)", ["garmentleadtime", "leadtime"], 16),
        new("BuTeam", "BU_Team", "Business unit code", ["buteam", "businessunit", "bu"], 16),
        new("BuDesc", "BU_Desc", "Business unit name (new codes)", ["budesc", "businessunitdesc"], 100),
        new("CreateDt", "Create_dt", "Created in the source system", ["createdt", "createdate", "created"], 0, IsDate: true),
        new("SketchUrl", "Sketch", "Sketch (https:// address)", ["sketch", "sketchurl"], 500),
        new("ImageUrl", "Image", "Photo (https:// address)", ["image", "imageurl", "photo"], 500),
        new("CustName", "Cust_Name", "Customer name (per style; names new customers)", ["custname", "customername"], 150),
        new("CustBrand", "Customer_Brand", "Customer brand (groups customer accounts)", ["custbrand", "customerbrand", "brand"], 100),
    ]);

    public static readonly ImportTargetSheet Article = new("Article", "Article", ["article", "articles", "colorway", "colorways"],
    [
        new("Cust", "Cust", "Customer code", ["cust", "customer", "customerid"], 32, Required: true),
        new("SeasonId", "Season ID", "Season", ["seasonid", "season"], 16, Required: true),
        new("StyleId", "Style ID", "Style number", ["styleid", "style", "styleno"], 100, Required: true),
        new("SeqNo", "SeqNo", "Colorway order", ["seqno", "seq", "sortorder"], 8),
        new("ArticleId", "Article ID", "Colorway code", ["articleid", "article", "colorwaycode", "colorway"], 100, Required: true),
        new("Description", "Description", "Colorway name", ["description", "articledesc", "colorwayname"], 150),
        new("Status", "Status", "Colorway status (INRANGE / DROPPED)", ["status"], 16),
        new("ImageUrl", "Image", "Colorway photo (https:// address)", ["image", "imageurl", "photo"], 500),
    ]);

    public static readonly ImportTargetSheet Bom = new("Bom", "BOM Detail", ["bomdetail", "bom", "bomdetails"],
    [
        new("Cust", "Cust", "Customer code", ["cust", "customer", "customerid"], 32, Required: true),
        new("SeasonId", "Season_ID", "Season", ["seasonid", "season"], 16, Required: true),
        new("StyleId", "Style_ID", "Style number", ["styleid", "style", "styleno"], 100, Required: true),
        new("PartNo", "Part_No", "Part number", ["partno", "part"], 16),
        new("AdMatId", "Ad_Mat_ID", "Material code (material master)", ["admatid", "materialid", "materialcode", "matid"], 64, Required: true),
        new("MaterialDesc", "Material_Desc", "Material description", ["materialdesc", "materialdescription"], 4000),
        new("MatTypeId", "MatType_ID", "Material type code", ["mattypeid", "materialtypeid", "mattype"], 16),
        new("MatTypeDesc", "MatType_Desc", "Material type name (new codes)", ["mattypedesc", "materialtypedesc"], 100),
        new("ContentClass", "Content_Class", "BOM section (FAB, TRI, ACC, LNP, ART)", ["contentclass"], 100),
        new("AdSupplierId", "Ad_Supplier_ID", "Nominated supplier code", ["adsupplierid", "nominatedsupplierid"], 32),
        new("AdSupplierName", "Ad_Supplier_Name", "Nominated supplier name", ["adsuppliername", "nominatedsuppliername"], 100),
        new("LcoConsump", "LCO_Consump", "LCO consumption", ["lcoconsump", "lcoconsumption"], 32),
        new("AdConsump", "AD_Consump", "Brand consumption", ["adconsump", "adconsumption", "brandconsumption"], 32),
        new("Uom", "UOM", "Unit", ["uom", "adconsumpuom", "consumpuom"], 8),
        new("SapSupplierId", "SAP_Supplier_ID", "SAP supplier code", ["sapsupplierid", "supplierid"], 32),
        new("SapSupplierName", "SAP_Supplier_Name", "SAP supplier name (new suppliers)", ["sapsuppliername", "suppliername"], 150),
        new("ArticleSeq", "Article Seq", "Article sequence (not stored)", ["articleseq"], 16),
        new("ArticleId", "Article ID", "Colorway code (blank = all colorways)", ["articleid", "article", "colorwaycode", "colorway"], 100),
        new("ArticleDesc", "Article_Desc", "Colorway name (not stored)", ["articledesc", "colorwayname"], 150),
        new("MatColorId", "MatColor_ID", "Material colour code", ["matcolorid", "materialcolorid", "materialcolorcode"], 80),
        new("MatColorDesc", "MatColor_Desc", "Material colour", ["matcolordesc", "materialcolordesc", "materialcolordescription"], 150),
        new("ImageUrl", "Image", "BOM line picture (https:// address)", ["image", "imageurl", "photo"], 500),
        new("ConsumpAllowance", "Consump_Allowance", "Consumption allowance",
            ["consumpallowance", "consumptionallowance", "asadconsumpallowance", "allowance"], 32),
    ]);

    public static readonly ImportTargetSheet[] Sheets = [Style, Article, Bom];

    /// <summary>"Season_ID" = "Season ID" = "seasonid": lower case, letters and digits only.</summary>
    public static string Normalize(string? text)
        => new((text ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

/// <summary>Which workbook sheet feeds one import sheet, and which heading feeds each staging column (null = not imported).</summary>
public sealed record ImportSheetMapping(string? Source, IReadOnlyDictionary<string, string?> Columns);

/// <summary>Sheet key (Style, Article, Bom) -> its mapping.</summary>
public sealed record ImportColumnMapping(IReadOnlyDictionary<string, ImportSheetMapping> Sheets);

/// <summary>A workbook column: its heading and a few sample values.</summary>
public sealed record WorkbookColumnDto(string Heading, IReadOnlyList<string> Samples);

public sealed record WorkbookSheetDto(string Name, IReadOnlyList<WorkbookColumnDto> Columns);

public sealed record ImportTargetColumnDto(string Name, string Heading, string Destination, bool Required);

public sealed record ImportTargetSheetDto(string Key, string Label, IReadOnlyList<ImportTargetColumnDto> Columns);

public sealed record ImportMappingDto(int MappingId, string Name, ImportColumnMapping Mapping, string UpdatedBy, DateTime UpdatedUtc);

/// <summary>
/// What an uploaded workbook holds, what the import needs, and the suggested mapping: the saved mapping that fits the
/// workbook best (MappingId), otherwise the automatic one (by sheet name / position and heading).
/// </summary>
/// <remarks>BatchId / FileName: the import the workbook is kept with (Status Mapping until it is staged).</remarks>
public sealed record WorkbookInspectionDto(
    IReadOnlyList<WorkbookSheetDto> Sheets,
    IReadOnlyList<ImportTargetSheetDto> Targets,
    ImportColumnMapping Mapping,
    int? MappingId,
    IReadOnlyList<ImportMappingDto> Saved,
    int? BatchId = null,
    string? FileName = null);

public sealed record SaveImportMappingRequest(string? Name, ImportColumnMapping? Mapping);

/// <summary>Builds and checks column mappings against a workbook's sheets and headings.</summary>
public static class StyleImportMapper
{
    public const int MaxNameLength = 100;

    public static IReadOnlyList<ImportTargetSheetDto> Targets { get; } = StyleImportLayout.Sheets
        .Select(s => new ImportTargetSheetDto(s.Key, s.Label, s.Columns.Select(c => new ImportTargetColumnDto(c.Name, c.Heading, c.Destination, c.Required)).ToList()))
        .ToList();

    /// <summary>
    /// The mapping found without help: each import sheet by name, otherwise the unused workbook sheet in its position
    /// (first three); each column by its heading or a known alias (first match).
    /// </summary>
    public static ImportColumnMapping Auto(IReadOnlyList<WorkbookSheetDto> sheets)
    {
        var used = new HashSet<int>();
        var chosen = new int[StyleImportLayout.Sheets.Length];
        for (var i = 0; i < chosen.Length; i++)
        {
            chosen[i] = -1;
            for (var w = 0; w < sheets.Count; w++)
                if (!used.Contains(w) && StyleImportLayout.Sheets[i].Names.Contains(StyleImportLayout.Normalize(sheets[w].Name))) { chosen[i] = w; used.Add(w); break; }
        }
        var knownNames = StyleImportLayout.Sheets.SelectMany(s => s.Names).ToHashSet();
        for (var i = 0; i < chosen.Length; i++)
            if (chosen[i] < 0 && i < sheets.Count && !used.Contains(i) && !knownNames.Contains(StyleImportLayout.Normalize(sheets[i].Name)))
            {
                chosen[i] = i;
                used.Add(i);
            }

        var result = new Dictionary<string, ImportSheetMapping>();
        for (var i = 0; i < chosen.Length; i++)
        {
            var target = StyleImportLayout.Sheets[i];
            if (chosen[i] < 0)
            {
                result[target.Key] = new ImportSheetMapping(null, new Dictionary<string, string?>());
                continue;
            }
            var sheet = sheets[chosen[i]];
            var columns = new Dictionary<string, string?>();
            var taken = new HashSet<string>();
            foreach (var column in target.Columns)
            {
                var heading = sheet.Columns.FirstOrDefault(h => !taken.Contains(h.Heading) && column.Aliases.Contains(StyleImportLayout.Normalize(h.Heading)))?.Heading;
                columns[column.Name] = heading;
                if (heading is not null) taken.Add(heading);
            }
            result[target.Key] = new ImportSheetMapping(sheet.Name, columns);
        }
        return new ImportColumnMapping(result);
    }

    /// <summary>Why the mapping cannot be used with these sheets (empty = it can).</summary>
    public static IReadOnlyList<string> Problems(ImportColumnMapping mapping, IReadOnlyList<WorkbookSheetDto> sheets)
    {
        var problems = new List<string>();
        foreach (var target in StyleImportLayout.Sheets)
        {
            var sheetMap = mapping.Sheets.GetValueOrDefault(target.Key);
            if (sheetMap?.Source is not { Length: > 0 } source)
            {
                problems.Add($"The workbook has no \"{target.Label}\" sheet.");
                continue;
            }
            var sheet = FindSheet(sheets, source);
            if (sheet is null)
            {
                problems.Add($"The workbook has no sheet named \"{source}\" (for {target.Label}).");
                continue;
            }
            foreach (var (name, heading) in sheetMap.Columns)
            {
                if (target.Columns.All(c => c.Name != name))
                    problems.Add($"\"{target.Label}\" has no column {name} to import into.");
                else if (heading is { Length: > 0 } && FindHeading(sheet.Columns.Select(c => c.Heading).ToList(), heading) < 0)
                    problems.Add($"The \"{sheet.Name}\" sheet has no \"{heading}\" column (for {target.Label} {name}).");
            }
            foreach (var missing in target.Columns.Where(c => c.Required && sheetMap.Columns.GetValueOrDefault(c.Name) is not { Length: > 0 }))
                problems.Add($"The \"{target.Label}\" sheet has no {missing.Name} column.");
        }
        return problems;
    }

    /// <summary>The saved mapping that fits these sheets with the most columns (latest first on a tie), or null.</summary>
    public static ImportMappingDto? Pick(IReadOnlyList<ImportMappingDto> saved, IReadOnlyList<WorkbookSheetDto> sheets)
        => saved.Where(m => Problems(m.Mapping, sheets).Count == 0)
            .OrderByDescending(m => m.Mapping.Sheets.Values.Sum(s => s.Columns.Count(c => c.Value is { Length: > 0 })))
            .ThenByDescending(m => m.UpdatedUtc)
            .FirstOrDefault();

    /// <summary>Only the known sheets and columns, headings trimmed, blanks as null (what is saved and used).</summary>
    public static ImportColumnMapping Clean(ImportColumnMapping mapping)
        => new(StyleImportLayout.Sheets.ToDictionary(t => t.Key, t =>
        {
            var m = mapping.Sheets?.GetValueOrDefault(t.Key);
            var source = string.IsNullOrWhiteSpace(m?.Source) ? null : m.Source.Trim();
            return new ImportSheetMapping(source, t.Columns.ToDictionary(c => c.Name, c =>
                m?.Columns?.GetValueOrDefault(c.Name) is { } h && !string.IsNullOrWhiteSpace(h) ? h.Trim() : null));
        }));

    public static WorkbookSheetDto? FindSheet(IReadOnlyList<WorkbookSheetDto> sheets, string name)
        => sheets.FirstOrDefault(s => string.Equals(s.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Index of a heading (trimmed, ignoring case), or -1.</summary>
    public static int FindHeading(IReadOnlyList<string?> headings, string heading)
    {
        for (var i = 0; i < headings.Count; i++)
            if (string.Equals(headings[i]?.Trim(), heading.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
