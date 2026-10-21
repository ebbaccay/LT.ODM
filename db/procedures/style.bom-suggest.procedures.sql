/*
    LT ODM Style Library - starting a new style: BOM suggestions (/api/v1/styles/{id}/bom-suggestion) and the context for
    Suggest from picture (/api/v1/styles/describe-picture)

    Reads only, except usp_BomLineColour_Fill (fills empty material colours picked on the colour suggestion). The API scores the candidates further (fabric words of the description, look of the sketch or photo),
    picks the reference styles, and builds the suggested BOM from the materials they share. Nothing is saved until a
    person ticks lines and adds them through the normal BOM line save.

      usp_BomSuggest_Candidates  styles with a BOM that could serve as references, with a score for what the headers
                                 share (family, product type / garment, weave, gender, customer / brand)
      usp_BomSuggest_Lines       the BOM lines of the chosen reference styles
      usp_BomSuggest_Fabrics     every fabric in the library with its description, how often it is used (and by this
                                 customer / brand), its usual supplier and consumption, for matching the description

    Requires style.tables.sql and style.gaps.procedures.sql (style.fn_GarmentCategory). Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.bom-suggest.procedures.sql
*/
SET NOCOUNT ON;
GO

/*
    Candidate reference styles for @StyleId: other styles with a BOM, best header match first (up to @Take).
    HeaderScore (0-75):
      30 family: an earlier or later version of the style (same customer and base number, or linked by history)
      20 same product type, else 14 same garment (fn_GarmentCategory of the product type; the description for this
         style when it has none). Other styles without a product type count only as family (reading every one's
         description took 0.4 s, and they make weak references anyway).
      10 same weave, 5 same gender
      10 same customer, else 6 same brand
    Styles that share nothing but the BOM are not candidates. Newer seasons first among equals.
    First result: the style itself (what the API needs to score the rest).
*/
CREATE OR ALTER PROCEDURE style.usp_BomSuggest_Candidates
    @StyleId int,
    @Take    int = 300
AS
BEGIN
    SET NOCOUNT ON;

    SELECT pt.ProductTypeCode, Category = style.fn_GarmentCategory(pt.Name)
    INTO #ptc
    FROM ref.ProductType pt;
    ALTER TABLE #ptc ADD PRIMARY KEY (ProductTypeCode);

    DECLARE @customerId int, @brand nvarchar(32), @base nvarchar(100), @productType nvarchar(40), @weave nvarchar(8), @gender nvarchar(16),
            @category nvarchar(20);
    SELECT @customerId = s.CustomerId, @brand = c.BrandCode, @base = s.BaseStyleNo, @productType = s.ProductTypeCode, @weave = s.WeaveTypeCode,
           @gender = s.Gender,
           @category = COALESCE(p.Category, style.fn_GarmentCategory(CONCAT(s.Description, N' ', s.ModelName)))
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN #ptc p ON p.ProductTypeCode = s.ProductTypeCode
    WHERE s.StyleId = @StyleId;
    IF @customerId IS NULL THROW 50404, 'This style no longer exists.', 1;

    SELECT StyleId = @StyleId, CustomerCode = c.PartnerCode, BrandCode = @brand, Category = @category, s.Description, s.ModelName,
           s.ProductTypeCode, ProductTypeName = pt.Name, s.WeaveTypeCode, s.Gender, s.SketchUrl, s.ImageUrl,
           BomLines = (SELECT COUNT(*) FROM style.BomLine b WHERE b.StyleId = @StyleId)
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE s.StyleId = @StyleId;

    -- the family: same customer and base number, plus styles linked by history either way
    CREATE TABLE #family (StyleId int PRIMARY KEY);
    INSERT #family SELECT StyleId FROM style.Style WHERE CustomerId = @customerId AND BaseStyleNo = @base AND StyleId <> @StyleId;
    INSERT #family
    SELECT DISTINCT x.Id
    FROM style.StyleHistory h CROSS APPLY (VALUES (h.StyleId), (h.SourceStyleId)) x (Id)
    WHERE (h.StyleId = @StyleId OR h.SourceStyleId = @StyleId) AND x.Id <> @StyleId AND x.Id NOT IN (SELECT StyleId FROM #family);

    WITH c AS
    (
        SELECT s.StyleId, s.StyleNo, s.SeasonCode, se.SeasonYear, TermOrder = t.SortOrder, s.Description, s.ModelName, s.ProductTypeCode,
               ProductTypeName = pt.Name, s.WeaveTypeCode, s.Gender, s.IsActive, CustomerCode = cu.PartnerCode,
               SameCustomer = CAST(IIF(s.CustomerId = @customerId, 1, 0) AS bit),
               SameBrand = CAST(IIF(s.CustomerId = @customerId OR (@brand IS NOT NULL AND cu.BrandCode = @brand), 1, 0) AS bit),
               Family = CAST(IIF(f.StyleId IS NOT NULL, 1, 0) AS bit),
               SameProductType = CAST(IIF(s.ProductTypeCode = @productType, 1, 0) AS bit),
               SameGarment = CAST(IIF(@category IS NOT NULL AND p.Category = @category, 1, 0) AS bit),
               SameWeave = CAST(IIF(s.WeaveTypeCode = @weave, 1, 0) AS bit),
               SameGender = CAST(IIF(s.Gender = @gender, 1, 0) AS bit)
        FROM style.Style s
        JOIN ref.Season se ON se.SeasonCode = s.SeasonCode
        JOIN ref.SeasonTerm t ON t.Term = se.Term
        JOIN partner.Partner cu ON cu.PartnerId = s.CustomerId
        LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
        LEFT JOIN #ptc p ON p.ProductTypeCode = s.ProductTypeCode
        LEFT JOIN #family f ON f.StyleId = s.StyleId
        WHERE s.StyleId <> @StyleId
          AND EXISTS (SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId)
    ),
    scored AS
    (
        SELECT c.*,
               HeaderScore = IIF(Family = 1, 30, 0)
                           + IIF(SameProductType = 1, 20, IIF(SameGarment = 1, 14, 0))
                           + IIF(SameWeave = 1, 10, 0) + IIF(SameGender = 1, 5, 0)
                           + IIF(SameCustomer = 1, 10, IIF(SameBrand = 1, 6, 0))
        FROM c
        WHERE Family = 1 OR SameProductType = 1 OR SameGarment = 1
    )
    SELECT TOP (@Take) StyleId, StyleNo, SeasonCode, CustomerCode, Description, ModelName, ProductTypeName, WeaveTypeCode, Gender, IsActive,
           SameCustomer, SameBrand, Family, SameProductType, SameGarment, SameWeave, SameGender, HeaderScore
    FROM scored
    ORDER BY HeaderScore DESC, IIF(IsActive = 1, 0, 1), SeasonYear DESC, TermOrder DESC, StyleId DESC;
END
GO

/* BOM lines of the reference styles (@StyleIds: ids separated by commas), with what the suggestion needs. */
CREATE OR ALTER PROCEDURE style.usp_BomSuggest_Lines
    @StyleIds nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.StyleId, b.BomLineId, b.LineSeq, b.PartNo, b.MaterialId, m.MaterialCode,
           MaterialDescription = COALESCE(b.MaterialDescription, m.Description), b.MaterialTypeCode, MaterialTypeName = mt.Name,
           b.ContentClassCode, ContentClassName = cc.Name, ContentClassSort = CAST(cc.SortOrder AS int),
           b.NominatedSupplierCode, b.NominatedSupplierName, SupplierCode = sp.PartnerCode, SupplierName = sp.Name,
           b.LcoConsumption, b.BrandConsumption, b.UomCode
    FROM style.BomLine b
    JOIN mat.Material m ON m.MaterialId = b.MaterialId
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = b.MaterialTypeCode
    LEFT JOIN ref.ContentClass cc ON cc.ContentClassCode = b.ContentClassCode
    LEFT JOIN partner.Partner sp ON sp.PartnerId = b.SupplierId
    WHERE b.StyleId IN (SELECT TRY_CAST(value AS int) FROM STRING_SPLIT(@StyleIds, N','))
    ORDER BY b.StyleId, ISNULL(cc.SortOrder, 99), b.LineSeq;
END
GO

/*
    Every fabric (a material on a Fabric line), for matching the new style's description: its description (the master's,
    else the most used line description), the accepted description reading when there is one, how many styles use it
    (and how many of this customer / brand), the usual material type, unit, nominated and SAP supplier, and the median
    consumption.
*/
CREATE OR ALTER PROCEDURE style.usp_BomSuggest_Fabrics
    @CustomerCode nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = PartnerId, @brand = BrandCode FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode;

    CREATE TABLE #f
    (
        MaterialId int NOT NULL, StyleId int NOT NULL, BomLineId int NOT NULL, MaterialDescription nvarchar(4000) NULL, MaterialTypeCode nvarchar(16) NULL,
        UomCode nvarchar(8) NULL, NominatedSupplierCode nvarchar(32) NULL, NominatedSupplierName nvarchar(100) NULL, SupplierCode nvarchar(32) NULL,
        SupplierName nvarchar(200) NULL, Eff decimal(18, 6) NULL, SameCustomer bit NOT NULL, SameBrand bit NOT NULL,
        PRIMARY KEY (MaterialId, BomLineId)
    );
    INSERT #f
    SELECT b.MaterialId, b.StyleId, b.BomLineId, b.MaterialDescription, b.MaterialTypeCode, b.UomCode, b.NominatedSupplierCode, b.NominatedSupplierName,
           sp.PartnerCode, sp.Name, COALESCE(NULLIF(b.LcoConsumption, 0), NULLIF(b.BrandConsumption, 0)),
           IIF(s.CustomerId = @customerId, 1, 0), IIF(s.CustomerId = @customerId OR (@brand IS NOT NULL AND cu.BrandCode = @brand), 1, 0)
    FROM style.BomLine b
    JOIN style.Style s ON s.StyleId = b.StyleId
    JOIN partner.Partner cu ON cu.PartnerId = s.CustomerId
    LEFT JOIN partner.Partner sp ON sp.PartnerId = b.SupplierId
    WHERE b.ContentClassCode = N'FAB';

    -- the most frequent value of each column per material, and the median consumption, each in one pass
    WITH counts AS
    (
        SELECT MaterialId, Styles = COUNT(DISTINCT StyleId), CustomerStyles = COUNT(DISTINCT IIF(SameCustomer = 1, StyleId, NULL)),
               BrandStyles = COUNT(DISTINCT IIF(SameBrand = 1, StyleId, NULL))
        FROM #f GROUP BY MaterialId
    ),
    descr AS (SELECT MaterialId, MaterialDescription, Rn = ROW_NUMBER() OVER (PARTITION BY MaterialId ORDER BY COUNT(*) DESC)
              FROM #f WHERE MaterialDescription IS NOT NULL GROUP BY MaterialId, MaterialDescription),
    typ AS (SELECT MaterialId, MaterialTypeCode, Rn = ROW_NUMBER() OVER (PARTITION BY MaterialId ORDER BY COUNT(*) DESC)
            FROM #f GROUP BY MaterialId, MaterialTypeCode),
    uom AS (SELECT MaterialId, UomCode, Rn = ROW_NUMBER() OVER (PARTITION BY MaterialId ORDER BY COUNT(*) DESC)
            FROM #f GROUP BY MaterialId, UomCode),
    nom AS (SELECT MaterialId, NominatedSupplierCode, NominatedSupplierName, Rn = ROW_NUMBER() OVER (PARTITION BY MaterialId ORDER BY COUNT(*) DESC)
            FROM #f WHERE NominatedSupplierCode IS NOT NULL OR NominatedSupplierName IS NOT NULL GROUP BY MaterialId, NominatedSupplierCode, NominatedSupplierName),
    sap AS (SELECT MaterialId, SupplierCode, SupplierName, Rn = ROW_NUMBER() OVER (PARTITION BY MaterialId ORDER BY COUNT(*) DESC)
            FROM #f WHERE SupplierCode IS NOT NULL GROUP BY MaterialId, SupplierCode, SupplierName),
    med AS (SELECT DISTINCT MaterialId, Median = CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Eff) OVER (PARTITION BY MaterialId) AS decimal(18, 6))
            FROM #f WHERE Eff IS NOT NULL)
    SELECT m.MaterialId, m.MaterialCode, Description = COALESCE(m.Description, d.MaterialDescription),
           ms.Composition, ms.RecycledPct, ms.WeightGsm, ms.Construction,
           c.Styles, c.CustomerStyles, c.BrandStyles, t.MaterialTypeCode, u.UomCode,
           n.NominatedSupplierCode, n.NominatedSupplierName, sap.SupplierCode, sap.SupplierName, MedianConsumption = med.Median
    FROM counts c
    JOIN mat.Material m ON m.MaterialId = c.MaterialId
    LEFT JOIN mat.MaterialSpec ms ON ms.MaterialId = m.MaterialId AND ms.Status = 'Accepted'
    LEFT JOIN descr d ON d.MaterialId = c.MaterialId AND d.Rn = 1
    LEFT JOIN typ t ON t.MaterialId = c.MaterialId AND t.Rn = 1
    LEFT JOIN uom u ON u.MaterialId = c.MaterialId AND u.Rn = 1
    LEFT JOIN nom n ON n.MaterialId = c.MaterialId AND n.Rn = 1
    LEFT JOIN sap ON sap.MaterialId = c.MaterialId AND sap.Rn = 1
    LEFT JOIN med ON med.MaterialId = c.MaterialId;
END
GO

/*
    Suggest from picture (/api/v1/styles/describe-picture): what the API needs besides the picture.
    1. Every active product type with its garment (fn_GarmentCategory) and how many styles of this customer, of its
       brand and in all use it, so a garment read from the picture becomes the product type the customer already uses.
    2. The look-alike styles found for the picture (@StyleIds, ids separated by commas): weave, gender, description and
       product type, for the hints ("similar-looking styles are knit, recycled polyester").
*/
CREATE OR ALTER PROCEDURE style.usp_PictureDescribe_Context
    @CustomerCode nvarchar(32) = NULL,
    @StyleIds     nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = PartnerId, @brand = BrandCode FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode;

    SELECT pt.ProductTypeCode, pt.Name, Category = style.fn_GarmentCategory(pt.Name),
           CustomerStyles = (SELECT COUNT(*) FROM style.Style s WHERE s.ProductTypeCode = pt.ProductTypeCode AND s.CustomerId = @customerId),
           BrandStyles = (SELECT COUNT(*) FROM style.Style s JOIN partner.Partner c ON c.PartnerId = s.CustomerId
                          WHERE s.ProductTypeCode = pt.ProductTypeCode AND (s.CustomerId = @customerId OR (@brand IS NOT NULL AND c.BrandCode = @brand))),
           Styles = (SELECT COUNT(*) FROM style.Style s WHERE s.ProductTypeCode = pt.ProductTypeCode)
    FROM ref.ProductType pt
    WHERE pt.IsActive = 1;

    SELECT s.StyleId, s.StyleNo, CustomerCode = c.PartnerCode, s.SeasonCode, s.WeaveTypeCode, s.Gender, s.Description, ProductTypeName = pt.Name
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE s.StyleId IN (SELECT TRY_CAST(value AS int) FROM STRING_SPLIT(@StyleIds, N','));
END
GO

/*
    Material colours: what other styles used for the materials of @StyleId, per colorway name, for suggesting the colour of
    each BOM line in each colorway. Materials match on their base code (the code before a source suffix: 80020552_VN and
    80020552_BOWKER are one material, as in the BOM suggestion); colorway names match ignoring case, spaces and
    punctuation ("Collegiate Navy" = "COLLEGIATE-NAVY"). Only filled colours of other styles count.
    1. The style's cells: every BOM line x colorway it uses, with what is filled now.
    2. Per material base and colorway name: each colour used, on how many lines, how many of them this customer's.
    3. Per material base: each colour used on any colorway (for materials that always come in one colour).
*/
CREATE OR ALTER PROCEDURE style.usp_ColourSuggest_Sources
    @StyleId int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT CustomerId FROM style.Style WHERE StyleId = @StyleId);
    IF @customerId IS NULL THROW 50404, 'This style no longer exists.', 1;

    -- base code: up to the first _ space + ( or -, when that leaves at least 4 characters (BomSuggestionRules.BaseCode)
    CREATE TABLE #base (MaterialId int NOT NULL PRIMARY KEY, Base nvarchar(100) NOT NULL);
    INSERT #base
    SELECT m.MaterialId, UPPER(IIF(PATINDEX(N'%[_ +(-]%', m.MaterialCode) > 4, LEFT(m.MaterialCode, PATINDEX(N'%[_ +(-]%', m.MaterialCode) - 1), TRIM(m.MaterialCode)))
    FROM mat.Material m;

    SELECT b.BomLineId, k.ColorwayId, Base = mb.Base, k.ColorwayCode, k.ColorwayName,
           NameKey = UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(k.ColorwayName, N' ', N''), N'-', N''), N'/', N''), N'.', N''), N'_', N'')),
           x.MaterialColorCode, x.MaterialColorDescription
    INTO #cells
    FROM style.BomLine b
    JOIN style.BomLineColorway x ON x.BomLineId = b.BomLineId
    JOIN style.Colorway k ON k.ColorwayId = x.ColorwayId
    JOIN #base mb ON mb.MaterialId = b.MaterialId
    WHERE b.StyleId = @StyleId;

    SELECT BomLineId, ColorwayId, Base, ColorwayCode, ColorwayName, NameKey, MaterialColorCode, MaterialColorDescription FROM #cells;

    -- other styles' filled colours for these materials
    SELECT mb.Base, NameKey = UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(k.ColorwayName, N' ', N''), N'-', N''), N'/', N''), N'.', N''), N'_', N'')),
           x.MaterialColorCode, x.MaterialColorDescription, SameCustomer = IIF(s.CustomerId = @customerId, 1, 0)
    INTO #h
    FROM style.BomLineColorway x
    JOIN style.BomLine b ON b.BomLineId = x.BomLineId
    JOIN style.Style s ON s.StyleId = b.StyleId
    JOIN style.Colorway k ON k.ColorwayId = x.ColorwayId
    JOIN #base mb ON mb.MaterialId = b.MaterialId
    WHERE b.StyleId <> @StyleId
      AND mb.Base IN (SELECT DISTINCT Base FROM #cells)
      AND (x.MaterialColorCode IS NOT NULL OR x.MaterialColorDescription IS NOT NULL);

    SELECT Base, NameKey, MaterialColorCode, MaterialColorDescription, Lines = COUNT(*), CustomerLines = SUM(SameCustomer)
    FROM #h WHERE NameKey IN (SELECT DISTINCT NameKey FROM #cells)
    GROUP BY Base, NameKey, MaterialColorCode, MaterialColorDescription;

    SELECT Base, MaterialColorCode, MaterialColorDescription, Lines = COUNT(*), CustomerLines = SUM(SameCustomer)
    FROM #h
    GROUP BY Base, MaterialColorCode, MaterialColorDescription;
END
GO

/*
    Fills material colours picked on the colour suggestion (@Cells: json [{bomLineId, colorwayId, materialColorCode,
    materialColorDescription}]). Only cells of @StyleId's lines that are still empty are filled; the lines get a new
    RowVer, so a BOM line dialog opened before saves with a "changed by someone else" message instead of wiping them.
    Returns how many cells were filled.
*/
CREATE OR ALTER PROCEDURE style.usp_BomLineColour_Fill
    @StyleId   int,
    @Cells     nvarchar(max),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @filled TABLE (BomLineId int NOT NULL);

    BEGIN TRANSACTION;
    UPDATE x SET MaterialColorCode = NULLIF(TRIM(j.Code), N''), MaterialColorDescription = NULLIF(TRIM(j.Descr), N'')
    OUTPUT inserted.BomLineId INTO @filled
    FROM style.BomLineColorway x
    JOIN style.BomLine b ON b.BomLineId = x.BomLineId
    JOIN OPENJSON(@Cells) WITH (BomLineId int '$.bomLineId', ColorwayId int '$.colorwayId', Code nvarchar(80) '$.materialColorCode',
                                Descr nvarchar(150) '$.materialColorDescription') j ON j.BomLineId = x.BomLineId AND j.ColorwayId = x.ColorwayId
    WHERE b.StyleId = @StyleId AND x.MaterialColorCode IS NULL AND x.MaterialColorDescription IS NULL
      AND (NULLIF(TRIM(j.Code), N'') IS NOT NULL OR NULLIF(TRIM(j.Descr), N'') IS NOT NULL);

    UPDATE style.BomLine SET UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    WHERE BomLineId IN (SELECT BomLineId FROM @filled);
    IF EXISTS (SELECT 1 FROM @filled) EXEC style.usp_Style_Touch @StyleId, @ChangedBy;
    COMMIT TRANSACTION;
    SELECT Filled = COUNT(*) FROM @filled;
END
GO

PRINT 'BOM suggestion procedures ready.';
GO
