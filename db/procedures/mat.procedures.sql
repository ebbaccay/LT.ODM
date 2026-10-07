/*
    LT ODM Style Library - Materials (/api/v1/materials): the BOM seen by material instead of by style.

    usp_Material_List  materials with how widely they are used (styles, lines, seasons, customers, suppliers)
    usp_Material_Get   one material: where it is used, consumption per product type, suppliers, colours and
                       materials that may be the same thing (same description, or same code before the version suffix)

    Usage numbers come from BOM lines. Consumption compared = brand consumption, else LT's (LCO), totalled per style
    (a fabric on a body and a pocket line is one use) - the same basis as the AI Studio BOM check.
    Requires style.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\mat.procedures.sql
*/
SET NOCOUNT ON;
GO

/*
    Filters: search (material code or description, contains), content class and material type (as on the BOM line),
    SAP supplier, and customer / season (counts then cover only those styles). Sort: 'used' (most styles first) or 'code'.
*/
CREATE OR ALTER PROCEDURE mat.usp_Material_List
    @Search           nvarchar(100) = NULL,
    @ContentClassCode nvarchar(8)   = NULL,
    @MaterialTypeCode nvarchar(16)  = NULL,
    @SupplierCode     nvarchar(32)  = NULL,
    @CustomerCode     nvarchar(32)  = NULL,
    @SeasonCode       nvarchar(16)  = NULL,
    @Sort             varchar(10)   = 'used',
    @Skip             int           = 0,
    @Take             int           = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @like nvarchar(110) = UPPER(N'%' + REPLACE(REPLACE(REPLACE(@Search, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%');
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;
    DECLARE @supplierId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = @SupplierCode);
    IF @SupplierCode IS NOT NULL AND @supplierId IS NULL SET @supplierId = -1;

    CREATE TABLE #use
    (
        MaterialId int NOT NULL PRIMARY KEY, Styles int NOT NULL, Lines int NOT NULL, Seasons int NOT NULL, Customers int NOT NULL,
        Suppliers int NOT NULL
    );
    INSERT #use
    SELECT b.MaterialId, COUNT(DISTINCT b.StyleId), COUNT(*), COUNT(DISTINCT s.SeasonCode), COUNT(DISTINCT s.CustomerId), COUNT(DISTINCT b.SupplierId)
    FROM style.BomLine b
    JOIN style.Style s ON s.StyleId = b.StyleId
    JOIN mat.Material m ON m.MaterialId = b.MaterialId
    WHERE (@customerId IS NULL OR s.CustomerId = @customerId)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode)
      AND (@supplierId IS NULL OR b.SupplierId = @supplierId)
      AND (@ContentClassCode IS NULL OR b.ContentClassCode = @ContentClassCode)
      AND (@MaterialTypeCode IS NULL OR b.MaterialTypeCode = @MaterialTypeCode)
      AND (@Search IS NULL
           OR UPPER(m.MaterialCode) COLLATE Latin1_General_100_BIN2 LIKE @like
           OR UPPER(m.Description) COLLATE Latin1_General_100_BIN2 LIKE @like
           OR UPPER(b.MaterialDescription) COLLATE Latin1_General_100_BIN2 LIKE @like)
    GROUP BY b.MaterialId
    OPTION (RECOMPILE);

    DECLARE @total int = @@ROWCOUNT;

    -- Page first, then the per-material lookups for that page only.
    DECLARE @page TABLE (RowNo int NOT NULL PRIMARY KEY, MaterialId int NOT NULL);
    INSERT @page
    SELECT ROW_NUMBER() OVER (ORDER BY x.k1 DESC, x.k2 DESC, x.MaterialCode), x.MaterialId
    FROM (SELECT u.MaterialId, m.MaterialCode, k1 = IIF(@Sort = 'code', 0, u.Styles), k2 = IIF(@Sort = 'code', 0, u.Lines)
          FROM #use u JOIN mat.Material m ON m.MaterialId = u.MaterialId
          ORDER BY k1 DESC, k2 DESC, m.MaterialCode
          OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY) x;

    SELECT m.MaterialId, m.MaterialCode,
           Description = COALESCE(m.Description, l.MaterialDescription),
           MaterialTypeCode = COALESCE(m.MaterialTypeCode, l.MaterialTypeCode), MaterialTypeName = mt.Name,
           ContentClassCode = COALESCE(m.ContentClassCode, l.ContentClassCode), ContentClassName = cc.Name,
           u.Styles, u.Lines, u.Seasons, u.Customers, u.Suppliers,
           TopSupplierName = ts.Name, LatestSeason = ls.SeasonCode,
           TotalCount = @total
    FROM @page pg
    JOIN #use u ON u.MaterialId = pg.MaterialId
    JOIN mat.Material m ON m.MaterialId = u.MaterialId
    OUTER APPLY (SELECT TOP 1 b.MaterialDescription, b.MaterialTypeCode, b.ContentClassCode
                 FROM style.BomLine b WHERE b.MaterialId = m.MaterialId ORDER BY b.BomLineId DESC) l
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = COALESCE(m.MaterialTypeCode, l.MaterialTypeCode)
    LEFT JOIN ref.ContentClass cc ON cc.ContentClassCode = COALESCE(m.ContentClassCode, l.ContentClassCode)
    OUTER APPLY (SELECT TOP 1 p.Name FROM style.BomLine b JOIN partner.Partner p ON p.PartnerId = b.SupplierId
                 WHERE b.MaterialId = m.MaterialId GROUP BY p.Name ORDER BY COUNT(*) DESC, p.Name) ts
    OUTER APPLY (SELECT TOP 1 s.SeasonCode FROM style.BomLine b JOIN style.Style s ON s.StyleId = b.StyleId
                 JOIN ref.Season se ON se.SeasonCode = s.SeasonCode JOIN ref.SeasonTerm t ON t.Term = se.Term
                 WHERE b.MaterialId = m.MaterialId ORDER BY se.SeasonYear DESC, t.SortOrder DESC) ls
    ORDER BY pg.RowNo;
END
GO

/*
    One material. Result sets: header (with library-wide counts); where-used lines (newest season first, at most
    @MaxRows); consumption per product type and unit (per-style totals: min, quartiles, median, max); suppliers;
    material colours; possibly-the-same materials.
*/
CREATE OR ALTER PROCEDURE mat.usp_Material_Get
    @MaterialId int,
    @MaxRows    int = 1000
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Header
    SELECT m.MaterialId, m.MaterialCode,
           Description = COALESCE(m.Description, l.MaterialDescription),
           MaterialTypeCode = COALESCE(m.MaterialTypeCode, l.MaterialTypeCode), MaterialTypeName = mt.Name,
           ContentClassCode = COALESCE(m.ContentClassCode, l.ContentClassCode), ContentClassName = cc.Name,
           Styles = (SELECT COUNT(DISTINCT StyleId) FROM style.BomLine WHERE MaterialId = m.MaterialId),
           Lines = (SELECT COUNT(*) FROM style.BomLine WHERE MaterialId = m.MaterialId),
           Customers = (SELECT COUNT(DISTINCT s.CustomerId) FROM style.BomLine b JOIN style.Style s ON s.StyleId = b.StyleId WHERE b.MaterialId = m.MaterialId),
           Seasons = (SELECT COUNT(DISTINCT s.SeasonCode) FROM style.BomLine b JOIN style.Style s ON s.StyleId = b.StyleId WHERE b.MaterialId = m.MaterialId),
           m.CreatedBy, m.CreatedUtc, m.UpdatedBy, m.UpdatedUtc
    FROM mat.Material m
    OUTER APPLY (SELECT TOP 1 b.MaterialDescription, b.MaterialTypeCode, b.ContentClassCode
                 FROM style.BomLine b WHERE b.MaterialId = m.MaterialId ORDER BY b.BomLineId DESC) l
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = COALESCE(m.MaterialTypeCode, l.MaterialTypeCode)
    LEFT JOIN ref.ContentClass cc ON cc.ContentClassCode = COALESCE(m.ContentClassCode, l.ContentClassCode)
    WHERE m.MaterialId = @MaterialId;

    IF @@ROWCOUNT = 0 RETURN;

    -- 2. Where used
    SELECT TOP (@MaxRows)
           b.BomLineId, s.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName,
           s.ProductTypeCode, ProductTypeName = pt.Name, s.Gender, b.LineSeq, b.PartNo, b.LcoConsumption, b.BrandConsumption, b.UomCode,
           SupplierName = sp.Name, b.NominatedSupplierName,
           Colorways = (SELECT COUNT(*) FROM style.BomLineColorway x WHERE x.BomLineId = b.BomLineId),
           Colours = (SELECT STRING_AGG(d.Colour, N', ') WITHIN GROUP (ORDER BY d.Colour)
                      FROM (SELECT DISTINCT TOP 6 Colour = x.MaterialColorDescription FROM style.BomLineColorway x
                            WHERE x.BomLineId = b.BomLineId AND x.MaterialColorDescription IS NOT NULL
                            ORDER BY x.MaterialColorDescription) d)
    FROM style.BomLine b
    JOIN style.Style s ON s.StyleId = b.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    JOIN ref.Season se ON se.SeasonCode = s.SeasonCode
    JOIN ref.SeasonTerm t ON t.Term = se.Term
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    LEFT JOIN partner.Partner sp ON sp.PartnerId = b.SupplierId
    WHERE b.MaterialId = @MaterialId
    ORDER BY se.SeasonYear DESC, t.SortOrder DESC, s.StyleNo, b.LineSeq;

    -- 3. Consumption per product type and unit (per-style totals)
    ;WITH perStyle AS (
        SELECT b.StyleId, b.UomCode, Eff = SUM(COALESCE(NULLIF(b.BrandConsumption, 0), NULLIF(b.LcoConsumption, 0)))
        FROM style.BomLine b
        WHERE b.MaterialId = @MaterialId AND b.UomCode IS NOT NULL
        GROUP BY b.StyleId, b.UomCode
        HAVING SUM(COALESCE(NULLIF(b.BrandConsumption, 0), NULLIF(b.LcoConsumption, 0))) IS NOT NULL),
    typed AS (
        SELECT p.Eff, p.UomCode, ProductTypeCode = s.ProductTypeCode, ProductTypeName = pt.Name
        FROM perStyle p JOIN style.Style s ON s.StyleId = p.StyleId
        LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode)
    SELECT DISTINCT ProductTypeCode, ProductTypeName, UomCode,
           Styles = COUNT(*) OVER (PARTITION BY ProductTypeCode, UomCode),
           MinValue = MIN(Eff) OVER (PARTITION BY ProductTypeCode, UomCode),
           P25 = CAST(PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY Eff) OVER (PARTITION BY ProductTypeCode, UomCode) AS decimal(18, 6)),
           Median = CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Eff) OVER (PARTITION BY ProductTypeCode, UomCode) AS decimal(18, 6)),
           P75 = CAST(PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY Eff) OVER (PARTITION BY ProductTypeCode, UomCode) AS decimal(18, 6)),
           MaxValue = MAX(Eff) OVER (PARTITION BY ProductTypeCode, UomCode)
    FROM typed
    ORDER BY Styles DESC, ProductTypeCode;

    -- 4. Suppliers (no SAP supplier = empty code)
    SELECT SupplierCode = ISNULL(p.PartnerCode, N''), SupplierName = p.Name, Styles = COUNT(DISTINCT b.StyleId), Lines = COUNT(*)
    FROM style.BomLine b
    LEFT JOIN partner.Partner p ON p.PartnerId = b.SupplierId
    WHERE b.MaterialId = @MaterialId
    GROUP BY p.PartnerCode, p.Name
    ORDER BY COUNT(DISTINCT b.StyleId) DESC;

    -- 5. Material colours
    SELECT TOP 40 x.MaterialColorCode, x.MaterialColorDescription, Colorways = COUNT(*), Styles = COUNT(DISTINCT b.StyleId)
    FROM style.BomLineColorway x
    JOIN style.BomLine b ON b.BomLineId = x.BomLineId
    WHERE b.MaterialId = @MaterialId
    GROUP BY x.MaterialColorCode, x.MaterialColorDescription
    ORDER BY COUNT(*) DESC, x.MaterialColorDescription;

    -- 6. Possibly the same material: same description (ignoring case and spaces), or same code before the "_" version suffix
    DECLARE @code nvarchar(64), @desc nvarchar(4000);
    SELECT @code = MaterialCode, @desc = UPPER(REPLACE(TRIM(Description), N' ', N'')) FROM mat.Material WHERE MaterialId = @MaterialId;
    DECLARE @base nvarchar(64) = IIF(CHARINDEX(N'_', @code) > 1, LEFT(@code, CHARINDEX(N'_', @code) - 1), @code);

    SELECT TOP 20 m.MaterialId, m.MaterialCode, m.Description,
           Reason = CASE WHEN UPPER(REPLACE(TRIM(m.Description), N' ', N'')) = @desc THEN 'SameDescription' ELSE 'SameBaseCode' END,
           Styles = (SELECT COUNT(DISTINCT StyleId) FROM style.BomLine WHERE MaterialId = m.MaterialId)
    FROM mat.Material m
    WHERE m.MaterialId <> @MaterialId
      AND ((@desc IS NOT NULL AND LEN(@desc) >= 8 AND UPPER(REPLACE(TRIM(m.Description), N' ', N'')) = @desc)
           OR (LEN(@base) >= 5 AND (m.MaterialCode = @base OR m.MaterialCode LIKE REPLACE(REPLACE(@base, N'[', N'[[]'), N'%', N'[%]') + N'[_]%')))
    ORDER BY CASE WHEN UPPER(REPLACE(TRIM(m.Description), N' ', N'')) = @desc THEN 0 ELSE 1 END, m.MaterialCode;
END
GO

/* ======================= Materials insights (views on the Materials page) ======================= */

/* Recycled content as written on BOM descriptions (spelled several ways: RECYCLED, REC., PET-REC, Primegreen). */
CREATE OR ALTER FUNCTION mat.fn_IsRecycled (@Description nvarchar(4000))
RETURNS bit
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @d nvarchar(4000) = UPPER(@Description);
    RETURN IIF(@d LIKE N'%RECYCL%' OR @d LIKE N'%REC.%' OR @d LIKE N'%REC %' OR @d LIKE N'%PET-REC%' OR @d LIKE N'%PRIMEGREEN%', 1, 0);
END
GO

/*
    Standard trims for one product type (optionally one customer / season): trims, accessories, artwork, labels and
    packaging by how many of the product type's styles (with a BOM) use them.
    Result sets: 1. styles of the product type with a BOM; 2. per material type: how many different materials do the
    same job (consolidation candidates) and how many are used once; 3. materials with share of styles and median consumption.
*/
CREATE OR ALTER PROCEDURE mat.usp_Material_StandardTrims
    @ProductTypeCode nvarchar(40),
    @CustomerCode    nvarchar(32) = NULL,
    @SeasonCode      nvarchar(16) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;

    CREATE TABLE #styles (StyleId int NOT NULL PRIMARY KEY);
    INSERT #styles
    SELECT s.StyleId FROM style.Style s
    WHERE s.ProductTypeCode = @ProductTypeCode
      AND (@customerId IS NULL OR s.CustomerId = @customerId)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode)
      AND EXISTS (SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId);
    DECLARE @n int = @@ROWCOUNT;

    SELECT StylesWithBom = @n;

    CREATE TABLE #use
    (
        MaterialId int NOT NULL, StyleId int NOT NULL, MaterialTypeCode nvarchar(16) NULL, ContentClassCode nvarchar(8) NULL,
        UomCode nvarchar(8) NULL, Eff decimal(18, 6) NULL, PRIMARY KEY (MaterialId, StyleId)
    );
    INSERT #use
    SELECT b.MaterialId, b.StyleId, MIN(b.MaterialTypeCode), MIN(b.ContentClassCode), MIN(b.UomCode),
           SUM(COALESCE(NULLIF(b.BrandConsumption, 0), NULLIF(b.LcoConsumption, 0)))
    FROM style.BomLine b JOIN #styles s ON s.StyleId = b.StyleId
    WHERE b.ContentClassCode IN (N'ACC', N'TRI', N'ART', N'LNP')
    GROUP BY b.MaterialId, b.StyleId;

    ;WITH perMaterial AS (
        SELECT MaterialId, MaterialTypeCode = MIN(MaterialTypeCode), Uses = COUNT(*) FROM #use GROUP BY MaterialId)
    SELECT pm.MaterialTypeCode, MaterialTypeName = mt.Name,
           ContentClassCode = (SELECT TOP 1 u.ContentClassCode FROM #use u WHERE ISNULL(u.MaterialTypeCode, N'') = ISNULL(pm.MaterialTypeCode, N'')),
           Materials = COUNT(*), OneOffs = SUM(IIF(pm.Uses = 1, 1, 0)),
           Styles = (SELECT COUNT(DISTINCT u.StyleId) FROM #use u WHERE ISNULL(u.MaterialTypeCode, N'') = ISNULL(pm.MaterialTypeCode, N''))
    FROM perMaterial pm
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = pm.MaterialTypeCode
    GROUP BY pm.MaterialTypeCode, mt.Name
    ORDER BY Styles DESC, COUNT(*) DESC;

    SELECT TOP 150 m.MaterialId, m.MaterialCode, Description = LEFT(COALESCE(m.Description, d.MaterialDescription), 300),
           g.MaterialTypeCode, MaterialTypeName = mt.Name, g.ContentClassCode, g.Styles,
           Share = CAST(CAST(g.Styles AS decimal(9, 4)) / NULLIF(@n, 0) AS decimal(9, 4)), g.UomCode, g.Median
    FROM (SELECT DISTINCT u.MaterialId,
                 MaterialTypeCode = FIRST_VALUE(u.MaterialTypeCode) OVER (PARTITION BY u.MaterialId ORDER BY u.StyleId),
                 ContentClassCode = FIRST_VALUE(u.ContentClassCode) OVER (PARTITION BY u.MaterialId ORDER BY u.StyleId),
                 UomCode = FIRST_VALUE(u.UomCode) OVER (PARTITION BY u.MaterialId ORDER BY u.StyleId),
                 Styles = COUNT(*) OVER (PARTITION BY u.MaterialId),
                 Median = CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY u.Eff) OVER (PARTITION BY u.MaterialId) AS decimal(18, 6))
          FROM #use u) g
    JOIN mat.Material m ON m.MaterialId = g.MaterialId
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = g.MaterialTypeCode
    OUTER APPLY (SELECT TOP 1 b.MaterialDescription FROM style.BomLine b WHERE b.MaterialId = m.MaterialId) d
    ORDER BY g.Styles DESC, m.MaterialCode;
END
GO

/*
    Supplier concentration (optionally one customer / season / section). Result sets: 1. totals (styles, materials,
    suppliers, single- and multi-source materials, lines without a supplier); 2. per supplier: styles, share of styles,
    lines, materials, and materials only this supplier provides.
*/
CREATE OR ALTER PROCEDURE mat.usp_Material_Suppliers
    @CustomerCode     nvarchar(32) = NULL,
    @SeasonCode       nvarchar(16) = NULL,
    @ContentClassCode nvarchar(8)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;

    CREATE TABLE #l (StyleId int NOT NULL, MaterialId int NOT NULL, SupplierId int NULL);
    INSERT #l
    SELECT b.StyleId, b.MaterialId, b.SupplierId
    FROM style.BomLine b JOIN style.Style s ON s.StyleId = b.StyleId
    WHERE (@customerId IS NULL OR s.CustomerId = @customerId)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode)
      AND (@ContentClassCode IS NULL OR b.ContentClassCode = @ContentClassCode);

    DECLARE @styles int = (SELECT COUNT(DISTINCT StyleId) FROM #l);

    CREATE TABLE #sources (MaterialId int NOT NULL PRIMARY KEY, Sources int NOT NULL, OnlySupplierId int NULL);
    INSERT #sources
    SELECT MaterialId, COUNT(DISTINCT SupplierId), IIF(COUNT(DISTINCT SupplierId) = 1, MIN(SupplierId), NULL)
    FROM #l WHERE SupplierId IS NOT NULL GROUP BY MaterialId;

    SELECT Styles = @styles, Materials = (SELECT COUNT(DISTINCT MaterialId) FROM #l),
           Suppliers = (SELECT COUNT(DISTINCT SupplierId) FROM #l WHERE SupplierId IS NOT NULL),
           SingleSource = (SELECT COUNT(*) FROM #sources WHERE Sources = 1),
           MultiSource = (SELECT COUNT(*) FROM #sources WHERE Sources > 1),
           LinesWithoutSupplier = (SELECT COUNT(*) FROM #l WHERE SupplierId IS NULL),
           Lines = (SELECT COUNT(*) FROM #l);

    SELECT TOP 100 SupplierCode = p.PartnerCode, SupplierName = p.Name,
           Styles = COUNT(DISTINCT l.StyleId),
           Share = CAST(CAST(COUNT(DISTINCT l.StyleId) AS decimal(9, 4)) / NULLIF(@styles, 0) AS decimal(9, 4)),
           Lines = COUNT(*), Materials = COUNT(DISTINCT l.MaterialId),
           OnlySource = (SELECT COUNT(*) FROM #sources x WHERE x.OnlySupplierId = l.SupplierId)
    FROM #l l JOIN partner.Partner p ON p.PartnerId = l.SupplierId
    GROUP BY l.SupplierId, p.PartnerCode, p.Name
    ORDER BY COUNT(DISTINCT l.StyleId) DESC, p.Name;
END
GO

/*
    Duplicate candidates across the material master: groups of materials with the same description (ignoring case and
    spaces; at least 8 characters), optionally in one section. Result sets: 1. one row per material, GroupNo ties a group
    together, groups used on the most styles first; 2. totals (groups, materials in them).
*/
CREATE OR ALTER PROCEDURE mat.usp_Material_Duplicates
    @ContentClassCode nvarchar(8) = NULL,
    @MaxGroups        int         = 200
AS
BEGIN
    SET NOCOUNT ON;
    CREATE TABLE #m (MaterialId int NOT NULL PRIMARY KEY, NormDesc nvarchar(450) NOT NULL, ContentClassCode nvarchar(8) NULL, Styles int NOT NULL);
    INSERT #m
    SELECT m.MaterialId, LEFT(UPPER(REPLACE(TRIM(m.Description), N' ', N'')), 450),
           COALESCE(m.ContentClassCode, (SELECT TOP 1 b.ContentClassCode FROM style.BomLine b WHERE b.MaterialId = m.MaterialId)),
           (SELECT COUNT(DISTINCT b.StyleId) FROM style.BomLine b WHERE b.MaterialId = m.MaterialId)
    FROM mat.Material m
    WHERE LEN(TRIM(m.Description)) >= 8;
    IF @ContentClassCode IS NOT NULL DELETE #m WHERE ISNULL(ContentClassCode, N'') <> @ContentClassCode;

    CREATE TABLE #g (NormDesc nvarchar(450) NOT NULL PRIMARY KEY, Members int NOT NULL, Styles int NOT NULL, GroupNo int NOT NULL);
    INSERT #g
    SELECT NormDesc, Members, Styles, ROW_NUMBER() OVER (ORDER BY Styles DESC, Members DESC, NormDesc)
    FROM (SELECT NormDesc, Members = COUNT(*), Styles = SUM(Styles) FROM #m GROUP BY NormDesc HAVING COUNT(*) > 1) g;

    SELECT g.GroupNo, GroupStyles = g.Styles, g.Members, m.MaterialId, m.MaterialCode, Description = LEFT(m.Description, 300),
           x.ContentClassCode, x.Styles
    FROM #g g
    JOIN #m x ON x.NormDesc = g.NormDesc
    JOIN mat.Material m ON m.MaterialId = x.MaterialId
    WHERE g.GroupNo <= @MaxGroups
    ORDER BY g.GroupNo, x.Styles DESC, m.MaterialCode;

    SELECT Groups = COUNT(*), Materials = ISNULL(SUM(Members), 0) FROM #g;
END
GO

/*
    Recycled share of fabrics (mat.fn_IsRecycled on the BOM line description), optionally for one customer.
    Result sets: 1. per season (oldest first); 2. per product type (most styles first). For each: styles with fabric,
    styles whose fabrics are all recycled, styles with some recycled fabric, fabric lines, recycled fabric lines.
*/
CREATE OR ALTER PROCEDURE mat.usp_Material_Recycled
    @CustomerCode nvarchar(32) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;

    CREATE TABLE #s (StyleId int NOT NULL PRIMARY KEY, SeasonCode nvarchar(16) NOT NULL, ProductTypeCode nvarchar(40) NULL,
                     FabricLines int NOT NULL, RecycledLines int NOT NULL);
    INSERT #s
    SELECT s.StyleId, s.SeasonCode, s.ProductTypeCode, COUNT(*), SUM(CAST(mat.fn_IsRecycled(b.MaterialDescription) AS int))
    FROM style.Style s JOIN style.BomLine b ON b.StyleId = s.StyleId
    WHERE b.ContentClassCode = N'FAB' AND (@customerId IS NULL OR s.CustomerId = @customerId)
    GROUP BY s.StyleId, s.SeasonCode, s.ProductTypeCode;

    SELECT x.SeasonCode, Styles = COUNT(*), AllRecycled = SUM(IIF(x.RecycledLines = x.FabricLines, 1, 0)),
           SomeRecycled = SUM(IIF(x.RecycledLines > 0, 1, 0)), FabricLines = SUM(x.FabricLines), RecycledLines = SUM(x.RecycledLines)
    FROM #s x JOIN ref.Season se ON se.SeasonCode = x.SeasonCode JOIN ref.SeasonTerm t ON t.Term = se.Term
    GROUP BY x.SeasonCode, se.SeasonYear, t.SortOrder
    ORDER BY se.SeasonYear, t.SortOrder;

    SELECT x.ProductTypeCode, ProductTypeName = pt.Name, Styles = COUNT(*), AllRecycled = SUM(IIF(x.RecycledLines = x.FabricLines, 1, 0)),
           SomeRecycled = SUM(IIF(x.RecycledLines > 0, 1, 0)), FabricLines = SUM(x.FabricLines), RecycledLines = SUM(x.RecycledLines)
    FROM #s x LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = x.ProductTypeCode
    GROUP BY x.ProductTypeCode, pt.Name
    ORDER BY COUNT(*) DESC, pt.Name;
END
GO

/*
    Material colour usage (optionally one customer / season / section): the most used material colours, with how many
    colorways, styles, materials and seasons use them, and how many colour codes share the name.
*/
CREATE OR ALTER PROCEDURE mat.usp_Material_Colours
    @CustomerCode     nvarchar(32) = NULL,
    @SeasonCode       nvarchar(16) = NULL,
    @ContentClassCode nvarchar(8)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;

    SELECT TOP 80 Colour = UPPER(TRIM(x.MaterialColorDescription)),
           Codes = COUNT(DISTINCT x.MaterialColorCode), Colorways = COUNT(DISTINCT x.ColorwayId),
           Styles = COUNT(DISTINCT b.StyleId), Materials = COUNT(DISTINCT b.MaterialId), Seasons = COUNT(DISTINCT s.SeasonCode)
    FROM style.BomLineColorway x
    JOIN style.BomLine b ON b.BomLineId = x.BomLineId
    JOIN style.Style s ON s.StyleId = b.StyleId
    WHERE x.MaterialColorDescription IS NOT NULL AND UPPER(x.MaterialColorDescription) NOT LIKE N'%NO COLO%'
      AND (@customerId IS NULL OR s.CustomerId = @customerId)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode)
      AND (@ContentClassCode IS NULL OR b.ContentClassCode = @ContentClassCode)
    GROUP BY UPPER(TRIM(x.MaterialColorDescription))
    ORDER BY COUNT(DISTINCT x.ColorwayId) DESC, Colour;
END
GO
