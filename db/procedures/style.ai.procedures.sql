/*
    LT ODM Style Library - AI Studio (/api/v1/ai/...)

    usp_BomCheck_Run      rule-based BOM check (the AI only explains its findings)
    usp_AiRender_List / _Add / _Delete   AI renders of a style (style.AiRender)
    fn_ImageSlots, usp_ImageIndex_Status / _ToIndex / _Save, usp_ImageSearch   image search (style.ImageIndex)
    usp_ImageVector_Status / _ToIndex / _Save, usp_ImageVectorSearch   look-alike vectors (style.ImageVector)

    Smart search uses style.usp_Style_List; the change summary reads both styles with style.usp_Style_Get.
    Requires style.tables.sql and style.procedures.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.ai.procedures.sql
*/
SET NOCOUNT ON;
GO

/*
    BOM check for the styles in scope (one style, or a customer and/or season; nothing = the whole library).
    Peers always come from the whole library. Consumption compared = brand consumption, else LT's (LCO):
    the brand column is the more complete one. Rules (severity 3 High, 2 Medium, 1 Low):
      NoConsumption      fabric or thread line with no consumption at all                                   3
      LcoBrandGap        LCO and brand consumption both set and more than 15% apart (50%: High)            2/3
      PeerOutlier        a style uses a fabric 40%+ less or 60%+ more than the median of the styles of the
                         same product type using that fabric in the same unit (at least 3 styles; the same
                         pocketing is 0.08 yd on pants and 0.3 yd on jackets; under 0.4x / over 2.5x: High)  2/3
      MainFabricOutlier  the style's main fabric (most yards/metres) is under 0.65x or over 1.5x the median
                         for its product type (at least 5 styles; under 0.5x / over 2x: High)             2/3
      FamilyDrift        consumption of a material changed by more than 10% from the style it was reused from 2
      LcoMissing         fabric line with a brand consumption but no LCO consumption yet                     1
      NoSupplier         fabric, trim or accessory line without an SAP supplier                             1
    Material totals are per style (a fabric on a body and a pocket line is one use) and are reported on the
    material's first line. Result sets: scope counts; counts per rule and severity; the first @MaxRows findings.
*/
CREATE OR ALTER PROCEDURE style.usp_BomCheck_Run
    @StyleId      int          = NULL,
    @CustomerCode nvarchar(32) = NULL,
    @SeasonCode   nvarchar(16) = NULL,
    @MaxRows      int          = 500
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;

    CREATE TABLE #scope (StyleId int NOT NULL PRIMARY KEY);
    INSERT #scope (StyleId)
    SELECT s.StyleId FROM style.Style s
    WHERE (@StyleId IS NULL OR s.StyleId = @StyleId)
      AND (@customerId IS NULL OR s.CustomerId = @customerId)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode);

    CREATE TABLE #line
    (
        BomLineId int NOT NULL PRIMARY KEY, StyleId int NOT NULL, LineSeq int NOT NULL, MaterialId int NOT NULL, UomCode nvarchar(8) NULL,
        ContentClassCode nvarchar(8) NULL, Lco decimal(18, 6) NULL, Brand decimal(18, 6) NULL, Eff decimal(18, 6) NULL,
        SupplierId int NULL, InScope bit NOT NULL
    );
    INSERT #line
    SELECT b.BomLineId, b.StyleId, b.LineSeq, b.MaterialId, b.UomCode, b.ContentClassCode, b.LcoConsumption, b.BrandConsumption,
           COALESCE(NULLIF(b.BrandConsumption, 0), NULLIF(b.LcoConsumption, 0)), b.SupplierId,
           IIF(EXISTS (SELECT 1 FROM #scope x WHERE x.StyleId = b.StyleId), 1, 0)
    FROM style.BomLine b;

    -- One row per style + material + unit: total consumption, reported on the material's first line.
    CREATE TABLE #use
    (
        StyleId int NOT NULL, MaterialId int NOT NULL, UomCode nvarchar(8) NOT NULL, ContentClassCode nvarchar(8) NULL,
        Eff decimal(18, 6) NOT NULL, FirstLineId int NOT NULL, InScope bit NOT NULL, ProductTypeCode nvarchar(40) NULL,
        PRIMARY KEY (StyleId, MaterialId, UomCode)
    );
    INSERT #use
    SELECT g.StyleId, g.MaterialId, g.UomCode, g.ContentClassCode, g.Eff, l.BomLineId, g.InScope, s.ProductTypeCode
    FROM (SELECT StyleId, MaterialId, UomCode, ContentClassCode = MIN(ContentClassCode), Eff = SUM(Eff), FirstSeq = MIN(LineSeq), InScope = CAST(MAX(CAST(InScope AS int)) AS bit)
          FROM #line WHERE Eff IS NOT NULL AND UomCode IS NOT NULL
          GROUP BY StyleId, MaterialId, UomCode) g
    JOIN #line l ON l.StyleId = g.StyleId AND l.LineSeq = g.FirstSeq
    JOIN style.Style s ON s.StyleId = g.StyleId;

    CREATE TABLE #flag
    (
        BomLineId int NOT NULL, RuleCode varchar(20) NOT NULL, Severity tinyint NOT NULL,
        Value decimal(18, 6) NULL, RefValue decimal(18, 6) NULL, PeerCount int NULL, RefStyleId int NULL
    );

    -- NoConsumption
    INSERT #flag (BomLineId, RuleCode, Severity)
    SELECT BomLineId, 'NoConsumption', 3 FROM #line WHERE InScope = 1 AND ContentClassCode IN (N'FAB', N'TRI') AND Eff IS NULL;

    -- LcoBrandGap
    INSERT #flag (BomLineId, RuleCode, Severity, Value, RefValue)
    SELECT BomLineId, 'LcoBrandGap', IIF(ABS(Lco - Brand) / Brand > 0.5, 3, 2), Lco, Brand
    FROM #line WHERE InScope = 1 AND Lco > 0 AND Brand > 0 AND ABS(Lco - Brand) / Brand > 0.15;

    -- PeerOutlier (fabrics, within the product type)
    ;WITH peers AS (
        SELECT MaterialId, UomCode, ProductTypeCode, Styles = COUNT(*) FROM #use WHERE ContentClassCode = N'FAB' AND ProductTypeCode IS NOT NULL
        GROUP BY MaterialId, UomCode, ProductTypeCode HAVING COUNT(*) >= 3),
    med AS (
        SELECT DISTINCT u.MaterialId, u.UomCode, u.ProductTypeCode, p.Styles,
               Median = CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY u.Eff) OVER (PARTITION BY u.MaterialId, u.UomCode, u.ProductTypeCode) AS decimal(18, 6))
        FROM #use u JOIN peers p ON p.MaterialId = u.MaterialId AND p.UomCode = u.UomCode AND p.ProductTypeCode = u.ProductTypeCode
        WHERE u.ContentClassCode = N'FAB')
    INSERT #flag (BomLineId, RuleCode, Severity, Value, RefValue, PeerCount)
    SELECT u.FirstLineId, 'PeerOutlier', IIF(u.Eff / m.Median < 0.4 OR u.Eff / m.Median > 2.5, 3, 2), u.Eff, m.Median, m.Styles
    FROM #use u JOIN med m ON m.MaterialId = u.MaterialId AND m.UomCode = u.UomCode AND m.ProductTypeCode = u.ProductTypeCode
    WHERE u.InScope = 1 AND u.ContentClassCode = N'FAB' AND m.Median > 0 AND (u.Eff / m.Median < 0.6 OR u.Eff / m.Median > 1.6);

    -- MainFabricOutlier: main fabric in yards (metres converted) against the product type's median.
    ;WITH fabric AS (
        SELECT u.StyleId, u.FirstLineId, u.Eff, u.UomCode,
               Factor = CASE u.UomCode WHEN N'm' THEN CAST(1.093613 AS decimal(18, 6)) ELSE CAST(1 AS decimal(18, 6)) END,
               rn = ROW_NUMBER() OVER (PARTITION BY u.StyleId ORDER BY u.Eff * CASE u.UomCode WHEN N'm' THEN 1.093613 ELSE 1 END DESC)
        FROM #use u WHERE u.ContentClassCode = N'FAB' AND u.UomCode IN (N'yd', N'y', N'm')),
    main AS (
        SELECT f.StyleId, f.FirstLineId, f.Eff, f.Factor, Yards = f.Eff * f.Factor, s.ProductTypeCode
        FROM fabric f JOIN style.Style s ON s.StyleId = f.StyleId
        WHERE f.rn = 1 AND s.ProductTypeCode IS NOT NULL),
    typeMed AS (
        SELECT DISTINCT ProductTypeCode, Styles = COUNT(*) OVER (PARTITION BY ProductTypeCode),
               Median = CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Yards) OVER (PARTITION BY ProductTypeCode) AS decimal(18, 6))
        FROM main)
    INSERT #flag (BomLineId, RuleCode, Severity, Value, RefValue, PeerCount)
    SELECT m.FirstLineId, 'MainFabricOutlier', IIF(m.Yards / t.Median < 0.5 OR m.Yards / t.Median > 2, 3, 2), m.Eff,
           CAST(t.Median / m.Factor AS decimal(18, 6)), t.Styles
    FROM main m
    JOIN typeMed t ON t.ProductTypeCode = m.ProductTypeCode
    JOIN #scope sc ON sc.StyleId = m.StyleId
    WHERE t.Styles >= 5 AND t.Median > 0 AND (m.Yards / t.Median < 0.65 OR m.Yards / t.Median > 1.5);

    -- FamilyDrift: against the style it was reused from.
    INSERT #flag (BomLineId, RuleCode, Severity, Value, RefValue, RefStyleId)
    SELECT u.FirstLineId, 'FamilyDrift', 2, u.Eff, r.Eff, h.SourceStyleId
    FROM #use u
    JOIN style.StyleHistory h ON h.StyleId = u.StyleId
    JOIN #use r ON r.StyleId = h.SourceStyleId AND r.MaterialId = u.MaterialId AND r.UomCode = u.UomCode
    WHERE u.InScope = 1 AND u.ContentClassCode IN (N'FAB', N'TRI', N'ACC') AND r.Eff > 0 AND ABS(u.Eff - r.Eff) / r.Eff > 0.10;

    -- LcoMissing
    INSERT #flag (BomLineId, RuleCode, Severity, RefValue)
    SELECT BomLineId, 'LcoMissing', 1, Brand FROM #line WHERE InScope = 1 AND ContentClassCode = N'FAB' AND ISNULL(Lco, 0) = 0 AND Brand > 0;

    -- NoSupplier
    INSERT #flag (BomLineId, RuleCode, Severity)
    SELECT BomLineId, 'NoSupplier', 1 FROM #line WHERE InScope = 1 AND ContentClassCode IN (N'FAB', N'TRI', N'ACC') AND SupplierId IS NULL;

    -- 1. Scope
    SELECT StylesChecked = (SELECT COUNT(*) FROM #scope), LinesChecked = (SELECT COUNT(*) FROM #line WHERE InScope = 1),
           Findings = (SELECT COUNT(*) FROM #flag);

    -- 2. Per rule and severity
    SELECT f.RuleCode, Severity = CASE f.Severity WHEN 3 THEN 'High' WHEN 2 THEN 'Medium' ELSE 'Low' END,
           Lines = COUNT(*), Styles = COUNT(DISTINCT l.StyleId)
    FROM #flag f JOIN #line l ON l.BomLineId = f.BomLineId
    GROUP BY f.RuleCode, f.Severity
    ORDER BY f.Severity DESC, COUNT(*) DESC;

    -- 3. Findings, most severe first
    SELECT TOP (@MaxRows)
           f.BomLineId, b.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, b.LineSeq, b.PartNo, m.MaterialCode,
           MaterialDescription = LEFT(b.MaterialDescription, 300), b.ContentClassCode, b.UomCode, b.LcoConsumption, b.BrandConsumption,
           f.RuleCode, Severity = CASE f.Severity WHEN 3 THEN 'High' WHEN 2 THEN 'Medium' ELSE 'Low' END,
           f.Value, f.RefValue, f.PeerCount, f.RefStyleId, RefStyleNo = rs.StyleNo
    FROM #flag f
    JOIN style.BomLine b ON b.BomLineId = f.BomLineId
    JOIN style.Style s ON s.StyleId = b.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    JOIN mat.Material m ON m.MaterialId = b.MaterialId
    LEFT JOIN style.Style rs ON rs.StyleId = f.RefStyleId
    ORDER BY f.Severity DESC, s.SeasonCode DESC, s.StyleNo, b.LineSeq, f.RuleCode;
END
GO

/* ---------- AI renders ---------- */

CREATE OR ALTER PROCEDURE style.usp_AiRender_List
    @StyleId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RenderId, StyleId, ColorwayId, ColorwayCode, ImageUrl, Prompt, Provider, Model, UsedSketch, CreatedBy, CreatedUtc
    FROM style.AiRender
    WHERE StyleId = @StyleId
    ORDER BY RenderId DESC;
END
GO

CREATE OR ALTER PROCEDURE style.usp_AiRender_Add
    @StyleId    int,
    @ColorwayId int            = NULL,
    @ImageUrl   nvarchar(500),
    @Prompt     nvarchar(4000),
    @Provider   nvarchar(60),
    @Model      nvarchar(100),
    @UsedSketch bit,
    @CreatedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM style.Style WHERE StyleId = @StyleId)
        THROW 50404, N'The style was not found. It may have been deleted.', 1;
    DECLARE @colorwayCode nvarchar(100) = NULL;
    IF @ColorwayId IS NOT NULL
    BEGIN
        SELECT @colorwayCode = ColorwayCode FROM style.Colorway WHERE ColorwayId = @ColorwayId AND StyleId = @StyleId;
        IF @colorwayCode IS NULL THROW 50400, N'The colorway is not on this style. Reload and try again.', 1;
    END

    INSERT style.AiRender (StyleId, ColorwayId, ColorwayCode, ImageUrl, Prompt, Provider, Model, UsedSketch, CreatedBy)
    VALUES (@StyleId, @ColorwayId, @colorwayCode, @ImageUrl, @Prompt, @Provider, @Model, @UsedSketch, @CreatedBy);

    SELECT RenderId, StyleId, ColorwayId, ColorwayCode, ImageUrl, Prompt, Provider, Model, UsedSketch, CreatedBy, CreatedUtc
    FROM style.AiRender WHERE RenderId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE style.usp_AiRender_Delete
    @StyleId  int,
    @RenderId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE style.AiRender WHERE RenderId = @RenderId AND StyleId = @StyleId;
    IF @@ROWCOUNT = 0 THROW 50404, N'The render was not found. It may have been deleted.', 1;
END
GO

/* ---------- Concept to existing styles ---------- */

/*
    Library styles closest to a concept's criteria (read from the concept by the AI, then scored here):
      product type 4, each keyword found on the style's fabric lines 2, each wanted trim type on the BOM 1,
      gender 1, weave 1, customer 1. Only styles with a BOM and a score count; ties go to the newest season.
    @ProductTypes, @Keywords and @MaterialTypes are comma-separated (at most 10 / 6 / 8 values).
    Returns the candidates with what the AI needs to compare them: main fabrics, visible trims, matched keywords.
*/
CREATE OR ALTER PROCEDURE style.usp_ConceptMatch_Candidates
    @CustomerCode  nvarchar(32)  = NULL,
    @ProductTypes  nvarchar(400) = NULL,
    @Gender        nvarchar(16)  = NULL,
    @WeaveTypeCode nvarchar(8)   = NULL,
    @Keywords      nvarchar(400) = NULL,
    @MaterialTypes nvarchar(200) = NULL,
    @Take          int           = 25
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);

    DECLARE @pt TABLE (Code nvarchar(40) NOT NULL PRIMARY KEY);
    INSERT @pt SELECT DISTINCT TOP 10 LEFT(TRIM(value), 40) FROM STRING_SPLIT(@ProductTypes, N',') WHERE TRIM(value) <> N'';
    DECLARE @kw TABLE (Word nvarchar(60) NOT NULL PRIMARY KEY, Pattern nvarchar(70) NOT NULL);
    INSERT @kw SELECT DISTINCT TOP 6 w, N'%' + REPLACE(REPLACE(REPLACE(w, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%'
    FROM (SELECT w = UPPER(LEFT(TRIM(value), 60)) FROM STRING_SPLIT(@Keywords, N',') WHERE LEN(TRIM(value)) >= 3) k;
    DECLARE @mt TABLE (Code nvarchar(16) NOT NULL PRIMARY KEY);
    INSERT @mt SELECT DISTINCT TOP 8 UPPER(LEFT(TRIM(value), 16)) FROM STRING_SPLIT(@MaterialTypes, N',') WHERE TRIM(value) <> N'';

    CREATE TABLE #score (StyleId int NOT NULL PRIMARY KEY, Score int NOT NULL, Keywords nvarchar(400) NULL);
    INSERT #score
    SELECT s.StyleId,
           IIF(s.ProductTypeCode IN (SELECT Code FROM @pt), 4, 0)
         + 2 * (SELECT COUNT(*) FROM @kw k WHERE EXISTS (
               SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId AND b.ContentClassCode = N'FAB'
                 AND UPPER(b.MaterialDescription) COLLATE Latin1_General_100_BIN2 LIKE k.Pattern))
         + (SELECT COUNT(*) FROM @mt m WHERE EXISTS (SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId AND b.MaterialTypeCode = m.Code))
         + IIF(@Gender IS NOT NULL AND s.Gender = @Gender, 1, 0)
         + IIF(@WeaveTypeCode IS NOT NULL AND s.WeaveTypeCode = @WeaveTypeCode, 1, 0)
         + IIF(@customerId IS NOT NULL AND s.CustomerId = @customerId, 1, 0),
           (SELECT STRING_AGG(k.Word, N', ') FROM @kw k WHERE EXISTS (
               SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId AND b.ContentClassCode = N'FAB'
                 AND UPPER(b.MaterialDescription) COLLATE Latin1_General_100_BIN2 LIKE k.Pattern))
    FROM style.Style s
    WHERE EXISTS (SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId)
    OPTION (RECOMPILE);

    SELECT TOP (@Take)
           s.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, s.Description, s.ProductTypeCode,
           ProductTypeName = pt.Name, s.Gender, s.WeaveTypeCode, s.ImageUrl, s.SketchUrl, x.Score, MatchedKeywords = x.Keywords,
           MainFabrics = (SELECT STRING_AGG(LEFT(f.MaterialDescription, 90), N' | ')
                          FROM (SELECT TOP 3 b.MaterialDescription FROM style.BomLine b
                                WHERE b.StyleId = s.StyleId AND b.ContentClassCode = N'FAB'
                                ORDER BY COALESCE(NULLIF(b.BrandConsumption, 0), b.LcoConsumption) DESC, b.LineSeq) f),
           Trims = (SELECT STRING_AGG(t.Name, N', ')
                    FROM (SELECT DISTINCT TOP 8 Name = mt.Name FROM style.BomLine b JOIN ref.MaterialType mt ON mt.MaterialTypeCode = b.MaterialTypeCode
                          WHERE b.StyleId = s.StyleId AND b.ContentClassCode IN (N'ACC', N'ART')) t),
           ColorwayCount = (SELECT COUNT(*) FROM style.Colorway k WHERE k.StyleId = s.StyleId)
    FROM #score x
    JOIN style.Style s ON s.StyleId = x.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    JOIN ref.Season se ON se.SeasonCode = s.SeasonCode
    LEFT JOIN ref.SeasonTerm t ON t.Term = se.Term
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE x.Score > 0
    ORDER BY x.Score DESC, se.SeasonYear DESC, t.SortOrder DESC, s.StyleNo;
END
GO

/* ---------- Image search ---------- */

/*
    Every picture image search can read: a style's photo and sketch, colorway pictures, AI renders. Only pictures stored
    by LT ODM (/api/v1/styles/images/...): outside addresses from imports are not fetched.
*/
CREATE OR ALTER FUNCTION style.fn_ImageSlots()
RETURNS TABLE
AS
RETURN
    SELECT s.StyleId, Source = CAST('Photo' AS varchar(10)), ItemId = 0, ImageUrl = s.ImageUrl
    FROM style.Style s WHERE s.ImageUrl LIKE N'/api/v1/styles/images/%'
    UNION ALL
    SELECT s.StyleId, 'Sketch', 0, s.SketchUrl
    FROM style.Style s WHERE s.SketchUrl LIKE N'/api/v1/styles/images/%'
    UNION ALL
    SELECT c.StyleId, 'Colorway', c.ColorwayId, c.ImageUrl
    FROM style.Colorway c WHERE c.ImageUrl LIKE N'/api/v1/styles/images/%'
    UNION ALL
    SELECT r.StyleId, 'Render', r.RenderId, r.ImageUrl
    FROM style.AiRender r WHERE r.ImageUrl LIKE N'/api/v1/styles/images/%';
GO

/*
    How far the index is for the current models: per source, the pictures, those indexed (searchable), those that could
    not be read (Failed) and those still to do (new, changed, or indexed with other models).
*/
CREATE OR ALTER PROCEDURE style.usp_ImageIndex_Status
    @VisionModel    nvarchar(200),
    @EmbeddingModel nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;
    -- Rows the first version stored for missing files (see the _ToIndex procedure) are removed here too, so the page
    -- counts them as to do and the Index button is offered.
    DELETE FROM style.ImageIndex WHERE Error = N'The picture file is missing or is not a picture.';
    SELECT p.Source,
           Pictures = COUNT(*),
           Indexed  = SUM(IIF(i.ImageIndexId IS NOT NULL AND i.Embedding IS NOT NULL, 1, 0)),
           Failed   = SUM(IIF(i.ImageIndexId IS NOT NULL AND i.Embedding IS NULL, 1, 0)),
           ToIndex  = SUM(IIF(i.ImageIndexId IS NULL, 1, 0))
    FROM style.fn_ImageSlots() p
    LEFT JOIN style.ImageIndex i
           ON i.StyleId = p.StyleId AND i.Source = p.Source AND i.ItemId = p.ItemId AND i.ImageUrl = p.ImageUrl
          AND i.VisionModel = @VisionModel AND i.EmbeddingModel = @EmbeddingModel
    GROUP BY p.Source;
END
GO

/*
    The next pictures to index. First tidies up: removes rows of pictures that are gone, and copies the reading of a
    picture already indexed with the same models (a style copied to another season shares its files), so no AI call is
    spent on it. KnownText is set when only the embedding model changed: the picture's text is re-embedded, not re-read.
*/
CREATE OR ALTER PROCEDURE style.usp_ImageIndex_ToIndex
    @VisionModel    nvarchar(200),
    @EmbeddingModel nvarchar(200),
    @Take           int = 8,
    @IndexedBy      nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Pictures whose file was missing were stored as failed by the first version (2026-10-09); a missing file is not a
    -- failure (it may be copied later), so those rows are removed and the pictures are tried again.
    DELETE i FROM style.ImageIndex i WHERE i.Error = N'The picture file is missing or is not a picture.';

    DELETE i
    FROM style.ImageIndex i
    WHERE NOT EXISTS (SELECT 1 FROM style.fn_ImageSlots() p WHERE p.StyleId = i.StyleId AND p.Source = i.Source AND p.ItemId = i.ItemId);

    WITH todo AS
    (
        SELECT p.StyleId, p.Source, p.ItemId, p.ImageUrl
        FROM style.fn_ImageSlots() p
        WHERE NOT EXISTS (SELECT 1 FROM style.ImageIndex i
                          WHERE i.StyleId = p.StyleId AND i.Source = p.Source AND i.ItemId = p.ItemId AND i.ImageUrl = p.ImageUrl
                            AND i.VisionModel = @VisionModel AND i.EmbeddingModel = @EmbeddingModel)
    ),
    known AS
    (
        SELECT t.StyleId, t.Source, t.ItemId, t.ImageUrl, k.Garment, k.Colours, k.Details, k.Description, k.SearchText, k.Embedding, k.Provider
        FROM todo t
        CROSS APPLY (SELECT TOP 1 i.Garment, i.Colours, i.Details, i.Description, i.SearchText, i.Embedding, i.Provider
                     FROM style.ImageIndex i
                     WHERE i.ImageUrl = t.ImageUrl AND i.VisionModel = @VisionModel AND i.EmbeddingModel = @EmbeddingModel AND i.Embedding IS NOT NULL
                     ORDER BY i.ImageIndexId) k
    )
    MERGE style.ImageIndex AS d
    USING known AS s ON d.StyleId = s.StyleId AND d.Source = s.Source AND d.ItemId = s.ItemId
    WHEN MATCHED THEN UPDATE SET
        ImageUrl = s.ImageUrl, Garment = s.Garment, Colours = s.Colours, Details = s.Details, Description = s.Description,
        SearchText = s.SearchText, Embedding = s.Embedding, Error = NULL, VisionModel = @VisionModel, EmbeddingModel = @EmbeddingModel,
        Provider = s.Provider, IndexedBy = @IndexedBy, IndexedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (StyleId, Source, ItemId, ImageUrl, Garment, Colours, Details, Description, SearchText, Embedding,
                                  VisionModel, EmbeddingModel, Provider, IndexedBy)
        VALUES (s.StyleId, s.Source, s.ItemId, s.ImageUrl, s.Garment, s.Colours, s.Details, s.Description, s.SearchText, s.Embedding,
                @VisionModel, @EmbeddingModel, s.Provider, @IndexedBy);

    SELECT TOP (@Take)
           p.StyleId, p.Source, p.ItemId, p.ImageUrl,
           KnownText = IIF(i.ImageUrl = p.ImageUrl AND i.VisionModel = @VisionModel AND i.Error IS NULL, i.SearchText, NULL),
           i.Garment, i.Colours, i.Details, i.Description,
           Remaining = COUNT(*) OVER ()
    FROM style.fn_ImageSlots() p
    LEFT JOIN style.ImageIndex i ON i.StyleId = p.StyleId AND i.Source = p.Source AND i.ItemId = p.ItemId
    WHERE i.ImageIndexId IS NULL OR i.ImageUrl <> p.ImageUrl OR i.VisionModel <> @VisionModel OR i.EmbeddingModel <> @EmbeddingModel
    ORDER BY CASE p.Source WHEN 'Photo' THEN 1 WHEN 'Colorway' THEN 2 WHEN 'Sketch' THEN 3 ELSE 4 END, p.StyleId DESC, p.ItemId;
END
GO

/*
    Stores readings (json array: StyleId, Source, ItemId, ImageUrl, Garment, Colours, Details, Description, SearchText,
    Embedding as a json array of 1024 numbers, Error). Pictures changed or removed since they were read are skipped.
    Returns the number of rows stored.
*/
CREATE OR ALTER PROCEDURE style.usp_ImageIndex_Save
    @Items          nvarchar(max),
    @VisionModel    nvarchar(200),
    @EmbeddingModel nvarchar(200),
    @Provider       nvarchar(100),
    @IndexedBy      nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @rows TABLE (StyleId int, Source varchar(10), ItemId int, ImageUrl nvarchar(500), Garment nvarchar(100), Colours nvarchar(300),
                         Details nvarchar(600), Description nvarchar(1000), SearchText nvarchar(2000), Embedding vector(1024) NULL, Error nvarchar(300));
    INSERT @rows
    SELECT j.StyleId, j.Source, j.ItemId, j.ImageUrl, LEFT(j.Garment, 100), LEFT(j.Colours, 300), LEFT(j.Details, 600), LEFT(j.Description, 1000),
           LEFT(j.SearchText, 2000), IIF(j.Embedding IS NULL, NULL, CAST(j.Embedding AS vector(1024))), LEFT(j.Error, 300)
    FROM OPENJSON(@Items) WITH (
        StyleId int, Source varchar(10), ItemId int, ImageUrl nvarchar(500), Garment nvarchar(200), Colours nvarchar(400), Details nvarchar(800),
        Description nvarchar(1200), SearchText nvarchar(2400), Embedding nvarchar(max) AS JSON, Error nvarchar(400)) j
    WHERE EXISTS (SELECT 1 FROM style.fn_ImageSlots() p
                  WHERE p.StyleId = j.StyleId AND p.Source = j.Source AND p.ItemId = j.ItemId AND p.ImageUrl = j.ImageUrl);

    MERGE style.ImageIndex AS d
    USING @rows AS s ON d.StyleId = s.StyleId AND d.Source = s.Source AND d.ItemId = s.ItemId
    WHEN MATCHED THEN UPDATE SET
        ImageUrl = s.ImageUrl, Garment = s.Garment, Colours = s.Colours, Details = s.Details, Description = s.Description,
        SearchText = s.SearchText, Embedding = s.Embedding, Error = s.Error, VisionModel = @VisionModel, EmbeddingModel = @EmbeddingModel,
        Provider = @Provider, IndexedBy = @IndexedBy, IndexedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (StyleId, Source, ItemId, ImageUrl, Garment, Colours, Details, Description, SearchText, Embedding, Error,
                                  VisionModel, EmbeddingModel, Provider, IndexedBy)
        VALUES (s.StyleId, s.Source, s.ItemId, s.ImageUrl, s.Garment, s.Colours, s.Details, s.Description, s.SearchText, s.Embedding, s.Error,
                @VisionModel, @EmbeddingModel, @Provider, @IndexedBy);

    SELECT COUNT(*) FROM @rows;
END
GO

/*
    Styles whose pictures are closest to a vector (cosine distance), best picture per style. The vector is @Embedding
    (json array of 1024 numbers, from a photo or words) or, for "more like this", the indexed picture of @FromStyleId
    (photo first, then colorway, sketch, render; that style is left out of the results). Only current rows indexed with
    @EmbeddingModel count: vectors of different models cannot be compared. Similarity = 1 - cosine distance.
*/
CREATE OR ALTER PROCEDURE style.usp_ImageSearch
    @EmbeddingModel nvarchar(200),
    @Embedding      nvarchar(max) = NULL,
    @FromStyleId    int           = NULL,
    @CustomerCode   nvarchar(32)  = NULL,
    @Take           int           = 24
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    DECLARE @q vector(1024);
    IF @Embedding IS NOT NULL
        SET @q = CAST(@Embedding AS vector(1024));
    ELSE
        SELECT TOP 1 @q = i.Embedding
        FROM style.ImageIndex i
        JOIN style.fn_ImageSlots() p ON p.StyleId = i.StyleId AND p.Source = i.Source AND p.ItemId = i.ItemId AND p.ImageUrl = i.ImageUrl
        WHERE i.StyleId = @FromStyleId AND i.EmbeddingModel = @EmbeddingModel AND i.Embedding IS NOT NULL
        ORDER BY CASE i.Source WHEN 'Photo' THEN 1 WHEN 'Colorway' THEN 2 WHEN 'Sketch' THEN 3 ELSE 4 END, i.ItemId;

    WITH scored AS
    (
        SELECT i.StyleId, i.Source, i.ItemId, i.ImageUrl, i.Garment, i.Colours, i.Details, i.Description, i.ImageIndexId,
               Distance = VECTOR_DISTANCE('cosine', i.Embedding, @q)
        FROM style.ImageIndex i
        JOIN style.fn_ImageSlots() p ON p.StyleId = i.StyleId AND p.Source = i.Source AND p.ItemId = i.ItemId AND p.ImageUrl = i.ImageUrl
        WHERE @q IS NOT NULL AND i.EmbeddingModel = @EmbeddingModel AND i.Embedding IS NOT NULL
          AND (@FromStyleId IS NULL OR i.StyleId <> @FromStyleId)
    ),
    best AS
    (
        SELECT *, Rn = ROW_NUMBER() OVER (PARTITION BY StyleId ORDER BY Distance, ImageIndexId) FROM scored
    )
    SELECT TOP (@Take)
           s.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, ProductTypeName = pt.Name, s.IsActive,
           x.Source, ColorwayCode = k.ColorwayCode, x.ImageUrl, x.Garment, x.Colours, x.Details, x.Description,
           Similarity = CAST(1 - x.Distance AS decimal(9, 4))
    FROM best x
    JOIN style.Style s ON s.StyleId = x.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    LEFT JOIN style.Colorway k ON x.Source = 'Colorway' AND k.ColorwayId = x.ItemId
    WHERE x.Rn = 1 AND (@customerId IS NULL OR s.CustomerId = @customerId)
    ORDER BY x.Distance, s.StyleNo;
END
GO

/* ---------- Image search: look-alike vectors (image model built into the API) ---------- */

/* Per source: pictures, those with a current vector, those that could not be read, those still to do. */
CREATE OR ALTER PROCEDURE style.usp_ImageVector_Status
    @Model nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;
    -- Rows the first version stored for missing files (see the _ToIndex procedure) are removed here too, so the page
    -- counts them as to do and the Index button is offered.
    DELETE FROM style.ImageVector WHERE Error = N'The picture file is missing or is not a picture.';
    SELECT p.Source,
           Pictures = COUNT(*),
           Indexed  = SUM(IIF(v.ImageVectorId IS NOT NULL AND v.Vector IS NOT NULL, 1, 0)),
           Failed   = SUM(IIF(v.ImageVectorId IS NOT NULL AND v.Vector IS NULL, 1, 0)),
           ToIndex  = SUM(IIF(v.ImageVectorId IS NULL, 1, 0))
    FROM style.fn_ImageSlots() p
    LEFT JOIN style.ImageVector v
           ON v.StyleId = p.StyleId AND v.Source = p.Source AND v.ItemId = p.ItemId AND v.ImageUrl = p.ImageUrl AND v.Model = @Model
    GROUP BY p.Source;
END
GO

/*
    The next pictures without a current vector. First removes rows of pictures that are gone and copies the vector of a
    picture file already done with the same model (styles copied to another season share files).
*/
CREATE OR ALTER PROCEDURE style.usp_ImageVector_ToIndex
    @Model     nvarchar(200),
    @Take      int = 200,
    @IndexedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Pictures whose file was missing were stored as failed by the first version (2026-10-09); a missing file is not a
    -- failure (it may be copied later), so those rows are removed and the pictures are tried again.
    DELETE v FROM style.ImageVector v WHERE v.Error = N'The picture file is missing or is not a picture.';

    DELETE v
    FROM style.ImageVector v
    WHERE NOT EXISTS (SELECT 1 FROM style.fn_ImageSlots() p WHERE p.StyleId = v.StyleId AND p.Source = v.Source AND p.ItemId = v.ItemId);

    WITH todo AS
    (
        SELECT p.StyleId, p.Source, p.ItemId, p.ImageUrl
        FROM style.fn_ImageSlots() p
        WHERE NOT EXISTS (SELECT 1 FROM style.ImageVector v
                          WHERE v.StyleId = p.StyleId AND v.Source = p.Source AND v.ItemId = p.ItemId AND v.ImageUrl = p.ImageUrl AND v.Model = @Model)
    ),
    known AS
    (
        SELECT t.StyleId, t.Source, t.ItemId, t.ImageUrl, k.Vector
        FROM todo t
        CROSS APPLY (SELECT TOP 1 v.Vector FROM style.ImageVector v
                     WHERE v.ImageUrl = t.ImageUrl AND v.Model = @Model AND v.Vector IS NOT NULL
                     ORDER BY v.ImageVectorId) k
    )
    MERGE style.ImageVector AS d
    USING known AS s ON d.StyleId = s.StyleId AND d.Source = s.Source AND d.ItemId = s.ItemId
    WHEN MATCHED THEN UPDATE SET ImageUrl = s.ImageUrl, Model = @Model, Vector = s.Vector, Error = NULL, IndexedBy = @IndexedBy, IndexedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (StyleId, Source, ItemId, ImageUrl, Model, Vector, IndexedBy)
        VALUES (s.StyleId, s.Source, s.ItemId, s.ImageUrl, @Model, s.Vector, @IndexedBy);

    SELECT TOP (@Take) p.StyleId, p.Source, p.ItemId, p.ImageUrl, Remaining = COUNT(*) OVER ()
    FROM style.fn_ImageSlots() p
    LEFT JOIN style.ImageVector v ON v.StyleId = p.StyleId AND v.Source = p.Source AND v.ItemId = p.ItemId
    WHERE v.ImageVectorId IS NULL OR v.ImageUrl <> p.ImageUrl OR v.Model <> @Model
    ORDER BY CASE p.Source WHEN 'Photo' THEN 1 WHEN 'Colorway' THEN 2 WHEN 'Sketch' THEN 3 ELSE 4 END, p.StyleId DESC, p.ItemId;
END
GO

/*
    Stores vectors (json array: StyleId, Source, ItemId, ImageUrl, Vector as a json array of 768 numbers, Error).
    Pictures changed or removed since they were read are skipped. Returns the number of rows stored.
*/
CREATE OR ALTER PROCEDURE style.usp_ImageVector_Save
    @Items     nvarchar(max),
    @Model     nvarchar(200),
    @IndexedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @rows TABLE (StyleId int, Source varchar(10), ItemId int, ImageUrl nvarchar(500), Vector vector(768) NULL, Error nvarchar(300));
    INSERT @rows
    SELECT j.StyleId, j.Source, j.ItemId, j.ImageUrl, IIF(j.Vector IS NULL, NULL, CAST(j.Vector AS vector(768))), LEFT(j.Error, 300)
    FROM OPENJSON(@Items) WITH (StyleId int, Source varchar(10), ItemId int, ImageUrl nvarchar(500), Vector nvarchar(max) AS JSON, Error nvarchar(400)) j
    WHERE EXISTS (SELECT 1 FROM style.fn_ImageSlots() p
                  WHERE p.StyleId = j.StyleId AND p.Source = j.Source AND p.ItemId = j.ItemId AND p.ImageUrl = j.ImageUrl);

    MERGE style.ImageVector AS d
    USING @rows AS s ON d.StyleId = s.StyleId AND d.Source = s.Source AND d.ItemId = s.ItemId
    -- A new vector makes the picture's tags outdated (TagVersion NULL: tagged again on the next indexing round).
    WHEN MATCHED THEN UPDATE SET ImageUrl = s.ImageUrl, Model = @Model, Vector = s.Vector, Error = s.Error, IndexedBy = @IndexedBy, IndexedUtc = SYSUTCDATETIME(),
        TagVersion = NULL, TaggedUtc = NULL
    WHEN NOT MATCHED THEN INSERT (StyleId, Source, ItemId, ImageUrl, Model, Vector, Error, IndexedBy)
        VALUES (s.StyleId, s.Source, s.ItemId, s.ImageUrl, @Model, s.Vector, s.Error, @IndexedBy);

    SELECT COUNT(*) FROM @rows;
END
GO

/*
    Styles whose pictures look closest to a look-alike vector (cosine), best picture per style. The vector is @Vector
    (json array of 768 numbers, from an uploaded photo) or the picture of @FromStyleId (photo first, then colorway,
    sketch, render; that style is left out). Only current rows of @Model count. The garment description from
    style.ImageIndex is added when the picture has one (any model), for display.
*/
CREATE OR ALTER PROCEDURE style.usp_ImageVectorSearch
    @Model        nvarchar(200),
    @Vector       nvarchar(max) = NULL,
    @FromStyleId  int           = NULL,
    @CustomerCode nvarchar(32)  = NULL,
    @Take         int           = 24
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    DECLARE @q vector(768);
    IF @Vector IS NOT NULL
        SET @q = CAST(@Vector AS vector(768));
    ELSE
        SELECT TOP 1 @q = v.Vector
        FROM style.ImageVector v
        JOIN style.fn_ImageSlots() p ON p.StyleId = v.StyleId AND p.Source = v.Source AND p.ItemId = v.ItemId AND p.ImageUrl = v.ImageUrl
        WHERE v.StyleId = @FromStyleId AND v.Model = @Model AND v.Vector IS NOT NULL
        ORDER BY CASE v.Source WHEN 'Photo' THEN 1 WHEN 'Colorway' THEN 2 WHEN 'Sketch' THEN 3 ELSE 4 END, v.ItemId;

    WITH scored AS
    (
        SELECT v.StyleId, v.Source, v.ItemId, v.ImageUrl, v.ImageVectorId, Distance = VECTOR_DISTANCE('cosine', v.Vector, @q)
        FROM style.ImageVector v
        JOIN style.fn_ImageSlots() p ON p.StyleId = v.StyleId AND p.Source = v.Source AND p.ItemId = v.ItemId AND p.ImageUrl = v.ImageUrl
        WHERE @q IS NOT NULL AND v.Model = @Model AND v.Vector IS NOT NULL
          AND (@FromStyleId IS NULL OR v.StyleId <> @FromStyleId)
    ),
    best AS
    (
        SELECT *, Rn = ROW_NUMBER() OVER (PARTITION BY StyleId ORDER BY Distance, ImageVectorId) FROM scored
    )
    SELECT TOP (@Take)
           s.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, ProductTypeName = pt.Name, s.IsActive,
           x.Source, ColorwayCode = k.ColorwayCode, x.ImageUrl, d.Garment, d.Colours, d.Details, d.Description,
           Similarity = CAST(1 - x.Distance AS decimal(9, 4))
    FROM best x
    JOIN style.Style s ON s.StyleId = x.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    LEFT JOIN style.Colorway k ON x.Source = 'Colorway' AND k.ColorwayId = x.ItemId
    LEFT JOIN style.ImageIndex d ON d.StyleId = x.StyleId AND d.Source = x.Source AND d.ItemId = x.ItemId AND d.ImageUrl = x.ImageUrl AND d.Error IS NULL
    WHERE x.Rn = 1 AND (@customerId IS NULL OR s.CustomerId = @customerId)
    ORDER BY x.Distance, s.StyleNo;
END
GO

/*
    How words (or a picture) compare with every current picture of @Model: how many pictures, and the average and spread of
    their similarity. Smart search uses it to tell a request about looks (one picture stands out from the rest) from one
    that is not (every picture scores about the same), before trusting the look-alike search.
*/
CREATE OR ALTER PROCEDURE style.usp_ImageVectorStats
    @Model  nvarchar(200),
    @Vector nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @q vector(768) = CAST(@Vector AS vector(768));
    SELECT Pictures = COUNT(*), Average = ISNULL(AVG(x.Similarity), 0), StdDev = ISNULL(STDEVP(x.Similarity), 0)
    FROM (SELECT Similarity = CAST(1 - VECTOR_DISTANCE('cosine', v.Vector, @q) AS float)
          FROM style.ImageVector v
          JOIN style.fn_ImageSlots() p ON p.StyleId = v.StyleId AND p.Source = v.Source AND p.ItemId = v.ItemId AND p.ImageUrl = v.ImageUrl
          WHERE v.Model = @Model AND v.Vector IS NOT NULL) x;
END
GO
