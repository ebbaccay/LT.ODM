/*
    LT ODM Style Library - workbook import (Settings > Import, Admin only)

      1. usp_StyleImport_Create   the API creates a batch, then bulk-copies the three sheets into staging.*Row
      2. usp_StyleImport_Check    normalises and checks the rows, records issues, and decides per style:
                                  New / Changed / Unchanged / Blocked (staging.ImportStyle)
      3. usp_StyleImport_Commit   writes New styles and the Changed styles the user ticked, then links style history
         usp_StyleImport_Cancel   discards a batch

    The workbook keeps the customer's headings (Article ID = colorway code). A BOM line is one distinct
    part + material + description + type + class + nominated supplier + consumptions + UOM + SAP supplier on a style;
    the workbook repeats it once per colorway (style.BomLineColorway).
    Requires style.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.import.procedures.sql
*/
SET NOCOUNT ON;
GO

/* ---------- Normalised staged rows (shared by check and commit) ---------- */

CREATE OR ALTER FUNCTION staging.fn_StyleRows (@BatchId int)
RETURNS TABLE
AS RETURN
SELECT
    r.RowNo,
    StyleKey        = CONCAT(r.Cust, N'|', UPPER(r.SeasonId), N'|', r.StyleId),
    CustomerCode    = r.Cust,
    SeasonCode      = UPPER(r.SeasonId),
    SeasonYear      = TRY_CONVERT(smallint, LEFT(r.SeasonId, 4)),
    Term            = UPPER(SUBSTRING(r.SeasonId, 6, 8)),
    StyleNo         = r.StyleId,
    r.Description,
    ModelCode       = r.ModelId,
    r.ModelName,
    WeaveTypeCode   = UPPER(r.WeaveTypeId),
    WeaveTypeName   = r.WeaveTypeDesc,
    ProductTypeCode = r.ProdTypeId,
    ProductTypeName = r.ProdTypeDesc,
    GenderRaw       = r.Gender,
    Gender          = CASE
                          WHEN r.Gender IN (N'MALE', N'MEN', N'MENS', N'M') THEN N'MALE'
                          WHEN r.Gender IN (N'FEMALE', N'WOMEN', N'WOMENS', N'W', N'LADIES') THEN N'FEMALE'
                          WHEN r.Gender IN (N'UNISEX', N'U') THEN N'UNISEX'
                          WHEN r.Gender IN (N'KIDS', N'KID', N'BOYS', N'GIRLS', N'YOUTH', N'INFANT') THEN N'KIDS'
                      END,
    LeadTimeRaw     = r.LeadTime,
    LeadTimeDays    = TRY_CONVERT(smallint, TRY_CONVERT(decimal(9, 2), r.LeadTime)),
    BusinessUnitCode = r.BuTeam,
    BusinessUnitName = r.BuDesc,
    SourceCreatedUtc = r.CreateDt,
    r.SketchUrl,
    r.ImageUrl
FROM staging.StyleRow AS r
WHERE r.BatchId = @BatchId;
GO

CREATE OR ALTER FUNCTION staging.fn_ColorwayRows (@BatchId int)
RETURNS TABLE
AS RETURN
SELECT
    r.RowNo,
    StyleKey     = CONCAT(r.Cust, N'|', UPPER(r.SeasonId), N'|', r.StyleId),
    r.Cust, r.SeasonId, r.StyleId,
    SortOrderRaw = r.SeqNo,
    SortOrder    = TRY_CONVERT(smallint, TRY_CONVERT(decimal(9, 2), r.SeqNo)),
    ColorwayCode = r.ArticleId,
    ColorwayName = r.Description,
    StatusRaw    = r.Status,
    Status       = IIF(r.Status IS NULL, 'INRANGE', IIF(UPPER(r.Status) IN (N'INRANGE', N'DROPPED'), CAST(UPPER(r.Status) AS varchar(10)), NULL)),
    r.ImageUrl
FROM staging.ArticleRow AS r
WHERE r.BatchId = @BatchId;
GO

CREATE OR ALTER FUNCTION staging.fn_BomRows (@BatchId int)
RETURNS TABLE
AS RETURN
SELECT
    n.*,
    -- Identifies a BOM line within its style (NULLs and separators kept distinct).
    LineKey = HASHBYTES('SHA2_256', CONCAT(CAST(ISNULL(CONVERT(nvarchar(20), n.PartNo), N'~') AS nvarchar(max)),
        NCHAR(31), ISNULL(n.MaterialCode, N'~'), NCHAR(31), ISNULL(n.MaterialDescription, N'~'),
        NCHAR(31), ISNULL(n.MaterialTypeCode, N'~'), NCHAR(31), ISNULL(n.ContentClassCode, N'~'),
        NCHAR(31), ISNULL(n.NominatedSupplierCode, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(40), n.LcoConsumption), N'~'),
        NCHAR(31), ISNULL(CONVERT(nvarchar(40), n.BrandConsumption), N'~'), NCHAR(31), ISNULL(n.UomCode, N'~'),
        NCHAR(31), ISNULL(n.SupplierCode, N'~')))
FROM (
    SELECT
        r.RowNo,
        StyleKey              = CONCAT(r.Cust, N'|', UPPER(r.SeasonId), N'|', r.StyleId),
        r.Cust, r.SeasonId, r.StyleId,
        PartNoRaw             = r.PartNo,
        PartNo                = TRY_CONVERT(int, TRY_CONVERT(decimal(18, 2), r.PartNo)),
        MaterialCode          = r.AdMatId,
        MaterialDescription   = r.MaterialDesc,
        MaterialTypeCode      = UPPER(r.MatTypeId),
        MaterialTypeName      = r.MatTypeDesc,
        ContentClassCode      = UPPER(r.ContentClass),
        NominatedSupplierCode = r.AdSupplierId,
        NominatedSupplierName = r.AdSupplierName,
        LcoRaw                = NULLIF(r.LcoConsump, N'NULL'),
        LcoConsumption        = TRY_CONVERT(decimal(18, 6), NULLIF(r.LcoConsump, N'NULL')),
        BrandRaw              = NULLIF(r.AdConsump, N'NULL'),
        BrandConsumption      = TRY_CONVERT(decimal(18, 6), NULLIF(r.AdConsump, N'NULL')),
        UomCode               = LOWER(r.Uom),
        SupplierCode          = r.SapSupplierId,
        SupplierName          = r.SapSupplierName,
        ColorwayCode          = r.ArticleId,
        MaterialColorCode     = r.MatColorId,
        MaterialColorDescription = r.MatColorDesc,
        r.ImageUrl
    FROM staging.BomRow AS r
    WHERE r.BatchId = @BatchId
) AS n;
GO

/* Replaced by the set-based comparison in usp_StyleImport_Check. */
DROP FUNCTION IF EXISTS style.fn_StyleFingerprint;
GO

/* ---------- 1. Create a batch ---------- */

/* Starts a batch for an uploaded workbook. Staged batches left for more than a day are cancelled (their rows removed). */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Create
    @FileName   nvarchar(260),
    @UploadedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @old TABLE (BatchId int PRIMARY KEY);
    UPDATE staging.ImportBatch SET Status = 'Cancelled', FinishedBy = N'system', FinishedUtc = SYSUTCDATETIME()
    OUTPUT inserted.BatchId INTO @old
    WHERE Status = 'Staged' AND UploadedUtc < DATEADD(DAY, -1, SYSUTCDATETIME());
    DELETE r FROM staging.StyleRow r JOIN @old o ON o.BatchId = r.BatchId;
    DELETE r FROM staging.ArticleRow r JOIN @old o ON o.BatchId = r.BatchId;
    DELETE r FROM staging.BomRow r JOIN @old o ON o.BatchId = r.BatchId;

    INSERT staging.ImportBatch (Kind, FileName, UploadedBy) VALUES ('StyleLibrary', @FileName, @UploadedBy);
    SELECT BatchId = CAST(SCOPE_IDENTITY() AS int);
END
GO

/* ---------- Reading a batch ---------- */

CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Get
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BatchId, FileName, Status, Summary = CAST(Summary AS nvarchar(max)), UploadedBy, UploadedUtc, FinishedBy, FinishedUtc
    FROM staging.ImportBatch WHERE BatchId = @BatchId;
END
GO

CREATE OR ALTER PROCEDURE staging.usp_StyleImport_List
    @Top int = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top) BatchId, FileName, Status, Summary = CAST(Summary AS nvarchar(max)), UploadedBy, UploadedUtc, FinishedBy, FinishedUtc
    FROM staging.ImportBatch WHERE Kind = 'StyleLibrary' ORDER BY BatchId DESC;
END
GO

/* @Action NULL = all. Styles with errors first, then by style number. */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_GetStyles
    @BatchId int,
    @Action  varchar(10)   = NULL,
    @Search  nvarchar(100) = NULL,
    @Skip    int           = 0,
    @Take    int           = 100
AS
BEGIN
    SET NOCOUNT ON;
    SELECT StyleKey, CustomerCode, SeasonCode, StyleNo, ModelName, Action, ExistingStyleId, Changes,
           ColorwayCount, BomLineCount, ErrorCount, WarningCount, Committed,
           TotalCount = COUNT(*) OVER ()
    FROM staging.ImportStyle
    WHERE BatchId = @BatchId
      AND (@Action IS NULL OR Action = @Action)
      AND (@Search IS NULL OR StyleNo LIKE N'%' + @Search + N'%' OR ModelName LIKE N'%' + @Search + N'%')
    ORDER BY IIF(Action = 'Blocked', 0, 1), StyleNo, SeasonCode
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE staging.usp_StyleImport_GetIssues
    @BatchId  int,
    @Severity varchar(8)    = NULL,
    @StyleKey nvarchar(100) = NULL,
    @Skip     int           = 0,
    @Take     int           = 100
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Sheet, RowNo, Severity, StyleKey, Message, TotalCount = COUNT(*) OVER ()
    FROM staging.ImportIssue
    WHERE BatchId = @BatchId
      AND (@Severity IS NULL OR Severity = @Severity)
      AND (@StyleKey IS NULL OR StyleKey = @StyleKey)
    ORDER BY IIF(Severity = 'Error', 0, 1), CASE Sheet WHEN 'Style' THEN 0 WHEN 'Article' THEN 1 ELSE 2 END, RowNo
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

/* ---------- 2. Check ---------- */

CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Check
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM staging.ImportBatch WHERE BatchId = @BatchId AND Status = 'Staged')
        THROW 50404, 'This import does not exist or is no longer waiting to be committed.', 1;

    DELETE staging.ImportIssue WHERE BatchId = @BatchId;
    DELETE staging.ImportStyle WHERE BatchId = @BatchId;

    SELECT * INTO #s FROM staging.fn_StyleRows(@BatchId);
    SELECT * INTO #c FROM staging.fn_ColorwayRows(@BatchId);
    SELECT * INTO #b FROM staging.fn_BomRows(@BatchId);
    CREATE INDEX IX_s ON #s (StyleKey);
    CREATE INDEX IX_c ON #c (StyleKey, ColorwayCode);
    CREATE INDEX IX_b ON #b (StyleKey, ColorwayCode);
    CREATE INDEX IX_b_line ON #b (StyleKey, LineKey) INCLUDE (ColorwayCode, MaterialColorCode, MaterialColorDescription);

    DECLARE @i TABLE (Sheet varchar(16), RowNo int, Severity varchar(8), StyleKey nvarchar(100), Message nvarchar(400));

    /* Style Header */
    INSERT @i SELECT 'Style', RowNo, 'Error', NULL, N'Cust, Season_ID and Style_ID are required.'
    FROM #s WHERE CustomerCode IS NULL OR SeasonCode IS NULL OR StyleNo IS NULL;

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, CONCAT(N'Season "', SeasonCode, N'" must look like 2027-SS (year, dash, ',
        (SELECT STRING_AGG(Term, N'/') WITHIN GROUP (ORDER BY SortOrder) FROM ref.SeasonTerm), N').')
    FROM #s WHERE SeasonCode IS NOT NULL
      AND (NOT REGEXP_LIKE(SeasonCode, N'^[0-9]{4}-[A-Z]{2,4}$') OR NOT EXISTS (SELECT 1 FROM ref.SeasonTerm t WHERE t.Term = #s.Term));

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, CONCAT(N'Style ', StyleNo, N' (', SeasonCode, N') appears more than once.')
    FROM (SELECT *, n = COUNT(*) OVER (PARTITION BY StyleKey) FROM #s WHERE StyleNo IS NOT NULL) d WHERE n > 1;

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, CONCAT(N'Garment_Lead_time "', LeadTimeRaw, N'" must be a number of days (0-730).')
    FROM #s WHERE LeadTimeRaw IS NOT NULL AND (LeadTimeDays IS NULL OR LeadTimeDays NOT BETWEEN 0 AND 730);

    INSERT @i SELECT 'Style', RowNo, 'Warning', StyleKey, CONCAT(N'Gender "', GenderRaw, N'" is not recognised and is left blank.')
    FROM #s WHERE GenderRaw IS NOT NULL AND Gender IS NULL;

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, N'Sketch and Image must be https:// addresses.'
    FROM #s WHERE (SketchUrl IS NOT NULL AND SketchUrl NOT LIKE N'https://%') OR (ImageUrl IS NOT NULL AND ImageUrl NOT LIKE N'https://%');

    /* Article (colorways) */
    INSERT @i SELECT 'Article', RowNo, 'Error', NULL, N'Cust, Season ID, Style ID and Article ID are required.'
    FROM #c WHERE Cust IS NULL OR SeasonId IS NULL OR StyleId IS NULL OR ColorwayCode IS NULL;

    INSERT @i SELECT 'Article', c.RowNo, 'Error', c.StyleKey, CONCAT(N'Style ', c.StyleId, N' (', c.SeasonId, N') is not on the Style Header sheet.')
    FROM #c c WHERE c.StyleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #s s WHERE s.StyleKey = c.StyleKey);

    INSERT @i SELECT 'Article', RowNo, 'Error', StyleKey, CONCAT(N'Article ', ColorwayCode, N' appears more than once for this style.')
    FROM (SELECT *, n = COUNT(*) OVER (PARTITION BY StyleKey, ColorwayCode) FROM #c WHERE ColorwayCode IS NOT NULL) d WHERE n > 1;

    INSERT @i SELECT 'Article', RowNo, 'Error', StyleKey, CONCAT(N'SeqNo "', SortOrderRaw, N'" must be a whole number.')
    FROM #c WHERE SortOrderRaw IS NOT NULL AND SortOrder IS NULL;

    INSERT @i SELECT 'Article', RowNo, 'Error', StyleKey, CONCAT(N'Status "', StatusRaw, N'" must be INRANGE or DROPPED.')
    FROM #c WHERE Status IS NULL;

    INSERT @i SELECT 'Article', RowNo, 'Error', StyleKey, N'Image must be an https:// address.'
    FROM #c WHERE ImageUrl IS NOT NULL AND ImageUrl NOT LIKE N'https://%';

    /* BOM Detail */
    INSERT @i SELECT 'BOM', RowNo, 'Error', NULL, N'Cust, Season_ID, Style_ID, Ad_Mat_ID and Article ID are required.'
    FROM #b WHERE Cust IS NULL OR SeasonId IS NULL OR StyleId IS NULL OR MaterialCode IS NULL OR ColorwayCode IS NULL;

    INSERT @i SELECT 'BOM', b.RowNo, 'Error', b.StyleKey, CONCAT(N'Style ', b.StyleId, N' (', b.SeasonId, N') is not on the Style Header sheet.')
    FROM #b b WHERE b.StyleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #s s WHERE s.StyleKey = b.StyleKey);

    INSERT @i SELECT 'BOM', b.RowNo, 'Error', b.StyleKey, CONCAT(N'Article ', b.ColorwayCode, N' is not on the Article sheet for this style.')
    FROM #b b WHERE b.ColorwayCode IS NOT NULL AND EXISTS (SELECT 1 FROM #s s WHERE s.StyleKey = b.StyleKey)
      AND NOT EXISTS (SELECT 1 FROM #c c WHERE c.StyleKey = b.StyleKey AND c.ColorwayCode = b.ColorwayCode);

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, CONCAT(N'Part_No "', PartNoRaw, N'" must be a whole number.')
    FROM #b WHERE PartNoRaw IS NOT NULL AND PartNo IS NULL;

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, CONCAT(N'Consumption must be a number of 0 or more (LCO_Consump "', LcoRaw, N'", AD_Consump "', BrandRaw, N'").')
    FROM #b WHERE (LcoRaw IS NOT NULL AND (LcoConsumption IS NULL OR LcoConsumption < 0))
               OR (BrandRaw IS NOT NULL AND (BrandConsumption IS NULL OR BrandConsumption < 0));

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, CONCAT(N'Content_Class "', ContentClassCode, N'" must be one of ',
        (SELECT STRING_AGG(ContentClassCode, N', ') WITHIN GROUP (ORDER BY SortOrder) FROM ref.ContentClass), N'.')
    FROM #b b WHERE ContentClassCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ContentClass cc WHERE cc.ContentClassCode = b.ContentClassCode);

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, N'Image must be an https:// address.'
    FROM #b WHERE ImageUrl IS NOT NULL AND ImageUrl NOT LIKE N'https://%';

    INSERT @i SELECT 'BOM', RowNo, 'Warning', StyleKey, CONCAT(N'Material ', MaterialCode, N' has no UOM.')
    FROM #b WHERE UomCode IS NULL AND MaterialCode IS NOT NULL;

    INSERT @i SELECT 'BOM', RowNo, 'Warning', StyleKey, N'Same BOM line and article as an earlier row: ignored.'
    FROM (SELECT *, n = ROW_NUMBER() OVER (PARTITION BY StyleKey, LineKey, ColorwayCode ORDER BY RowNo) FROM #b) d WHERE n > 1;

    /* Styles without colorways or BOM: imported, but worth a look. */
    INSERT @i SELECT 'Style', s.RowNo, 'Warning', s.StyleKey, N'No articles (colorways) for this style.'
    FROM #s s WHERE s.StyleNo IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #c c WHERE c.StyleKey = s.StyleKey);
    INSERT @i SELECT 'Style', s.RowNo, 'Warning', s.StyleKey, N'No BOM lines for this style.'
    FROM #s s WHERE s.StyleNo IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #b b WHERE b.StyleKey = s.StyleKey);

    INSERT staging.ImportIssue (BatchId, Sheet, RowNo, Severity, StyleKey, Message)
    SELECT @BatchId, Sheet, RowNo, Severity, StyleKey, LEFT(Message, 400) FROM @i;

    /* What committing would do to each style. */
    INSERT staging.ImportStyle (BatchId, StyleKey, CustomerCode, SeasonCode, StyleNo, ModelName, Action, ExistingStyleId,
                                ColorwayCount, BomLineCount, ErrorCount, WarningCount)
    SELECT @BatchId, s.StyleKey, s.CustomerCode, s.SeasonCode, s.StyleNo, s.ModelName, 'New', st.StyleId,
           (SELECT COUNT(*) FROM #c c WHERE c.StyleKey = s.StyleKey),
           (SELECT COUNT(DISTINCT b.LineKey) FROM #b b WHERE b.StyleKey = s.StyleKey),
           (SELECT COUNT(*) FROM @i i WHERE i.StyleKey = s.StyleKey AND i.Severity = 'Error'),
           (SELECT COUNT(*) FROM @i i WHERE i.StyleKey = s.StyleKey AND i.Severity = 'Warning')
    FROM (SELECT *, n = ROW_NUMBER() OVER (PARTITION BY StyleKey ORDER BY RowNo) FROM #s
          WHERE CustomerCode IS NOT NULL AND SeasonCode IS NOT NULL AND StyleNo IS NOT NULL) s
    LEFT JOIN partner.Partner p ON p.PartnerType = 'Customer' AND p.PartnerCode = s.CustomerCode
    LEFT JOIN style.Style st ON st.CustomerId = p.PartnerId AND st.SeasonCode = s.SeasonCode AND st.StyleNo = s.StyleNo
    WHERE s.n = 1;

    UPDATE staging.ImportStyle SET Action = 'Blocked' WHERE BatchId = @BatchId AND ErrorCount > 0;

    /*
        Existing styles: compare the staged style with the library, part by part (header, colorways, BOM), as hashes of
        the same canonical text built on both sides. Material codes are compared without case (the material master keeps
        the first spelling it saw: '...-LOOSE' = '...-Loose'); lists are ordered in binary collation so equal content
        always gives the same hash. Images count only when the file has one.
    */
    SELECT StyleKey, ExistingStyleId INTO #ex
    FROM staging.ImportStyle WHERE BatchId = @BatchId AND Action = 'New' AND ExistingStyleId IS NOT NULL;

    IF EXISTS (SELECT 1 FROM #ex)
    BEGIN
        CREATE UNIQUE CLUSTERED INDEX IX_ex ON #ex (StyleKey);

        /* Staged side. */
        SELECT s.StyleKey,
               HeaderHash = HASHBYTES('SHA2_256', CONCAT(CAST(ISNULL(s.Description, N'~') AS nvarchar(max)), NCHAR(31), ISNULL(s.ModelCode, N'~'),
                   NCHAR(31), ISNULL(s.ModelName, N'~'), NCHAR(31), ISNULL(s.WeaveTypeCode, N'~'), NCHAR(31), ISNULL(s.ProductTypeCode, N'~'),
                   NCHAR(31), ISNULL(s.Gender, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(10), s.LeadTimeDays), N'~'),
                   NCHAR(31), ISNULL(s.BusinessUnitCode, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(30), CAST(s.SourceCreatedUtc AS datetime2(3)), 121), N'~'))),
               s.SketchUrl, s.ImageUrl
        INTO #sh FROM #s s JOIN #ex e ON e.StyleKey = s.StyleKey;

        SELECT c.StyleKey, ColorwayHash = HASHBYTES('SHA2_256', STRING_AGG(CONCAT(CAST(c.ColorwayCode AS nvarchar(max)), NCHAR(31), ISNULL(c.ColorwayName, N'~'),
                   NCHAR(31), c.Status, NCHAR(31), ISNULL(c.SortOrder, c.RowNo)), NCHAR(30)) WITHIN GROUP (ORDER BY c.ColorwayCode COLLATE Latin1_General_BIN2))
        INTO #sc FROM #c c JOIN #ex e ON e.StyleKey = c.StyleKey GROUP BY c.StyleKey;

        SELECT StyleKey, LineKey,
               Colors = STRING_AGG(CONCAT(CAST(ColorwayCode AS nvarchar(max)), N':', ISNULL(MaterialColorCode, N'~'), N':', ISNULL(MaterialColorDescription, N'~')), N',')
                        WITHIN GROUP (ORDER BY ColorwayCode COLLATE Latin1_General_BIN2)
        INTO #slc
        FROM (SELECT b.StyleKey, b.LineKey, b.ColorwayCode, MaterialColorCode = MIN(b.MaterialColorCode), MaterialColorDescription = MIN(b.MaterialColorDescription)
              FROM #b b JOIN #ex e ON e.StyleKey = b.StyleKey GROUP BY b.StyleKey, b.LineKey, b.ColorwayCode) x
        GROUP BY StyleKey, LineKey;
        CREATE UNIQUE CLUSTERED INDEX IX_slc ON #slc (StyleKey, LineKey);

        SELECT l.StyleKey, BomHash = HASHBYTES('SHA2_256', STRING_AGG(l.Line, NCHAR(30)) WITHIN GROUP (ORDER BY l.Line COLLATE Latin1_General_BIN2))
        INTO #sb
        FROM (SELECT b.StyleKey,
                     Line = CONCAT(CAST(ISNULL(CONVERT(nvarchar(20), MAX(b.PartNo)), N'~') AS nvarchar(max)), NCHAR(31), UPPER(MAX(b.MaterialCode)),
                         NCHAR(31), ISNULL(MAX(b.MaterialDescription), N'~'), NCHAR(31), ISNULL(MAX(b.MaterialTypeCode), N'~'),
                         NCHAR(31), ISNULL(MAX(b.ContentClassCode), N'~'), NCHAR(31), ISNULL(MAX(b.NominatedSupplierCode), N'~'),
                         NCHAR(31), ISNULL(MAX(b.NominatedSupplierName), N'~'), NCHAR(31), ISNULL(MAX(b.SupplierCode), N'~'),
                         NCHAR(31), ISNULL(CONVERT(nvarchar(40), MAX(b.LcoConsumption)), N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(40), MAX(b.BrandConsumption)), N'~'),
                         NCHAR(31), ISNULL(MAX(b.UomCode), N'~'), NCHAR(31), MAX(k.Colors))
              FROM #b b
              JOIN #slc k ON k.StyleKey = b.StyleKey AND k.LineKey = b.LineKey
              GROUP BY b.StyleKey, b.LineKey) l
        GROUP BY l.StyleKey;

        /* Library side. */
        SELECT e.StyleKey,
               HeaderHash = HASHBYTES('SHA2_256', CONCAT(CAST(ISNULL(st.Description, N'~') AS nvarchar(max)), NCHAR(31), ISNULL(st.ModelCode, N'~'),
                   NCHAR(31), ISNULL(st.ModelName, N'~'), NCHAR(31), ISNULL(st.WeaveTypeCode, N'~'), NCHAR(31), ISNULL(st.ProductTypeCode, N'~'),
                   NCHAR(31), ISNULL(st.Gender, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(10), st.GarmentLeadTimeDays), N'~'),
                   NCHAR(31), ISNULL(st.BusinessUnitCode, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(30), st.SourceCreatedUtc, 121), N'~'))),
               st.SketchUrl, st.ImageUrl
        INTO #lh FROM #ex e JOIN style.Style st ON st.StyleId = e.ExistingStyleId;

        SELECT e.StyleKey, ColorwayHash = HASHBYTES('SHA2_256', STRING_AGG(CONCAT(CAST(c.ColorwayCode AS nvarchar(max)), NCHAR(31), ISNULL(c.ColorwayName, N'~'),
                   NCHAR(31), c.Status, NCHAR(31), c.SortOrder), NCHAR(30)) WITHIN GROUP (ORDER BY c.ColorwayCode COLLATE Latin1_General_BIN2))
        INTO #lc FROM #ex e JOIN style.Colorway c ON c.StyleId = e.ExistingStyleId GROUP BY e.StyleKey;

        SELECT blc.BomLineId,
               Colors = STRING_AGG(CONCAT(CAST(c.ColorwayCode AS nvarchar(max)), N':', ISNULL(blc.MaterialColorCode, N'~'), N':', ISNULL(blc.MaterialColorDescription, N'~')), N',')
                        WITHIN GROUP (ORDER BY c.ColorwayCode COLLATE Latin1_General_BIN2)
        INTO #llc
        FROM #ex e
        JOIN style.BomLine bl ON bl.StyleId = e.ExistingStyleId
        JOIN style.BomLineColorway blc ON blc.BomLineId = bl.BomLineId
        JOIN style.Colorway c ON c.ColorwayId = blc.ColorwayId
        GROUP BY blc.BomLineId;
        CREATE UNIQUE CLUSTERED INDEX IX_llc ON #llc (BomLineId);

        SELECT l.StyleKey, BomHash = HASHBYTES('SHA2_256', STRING_AGG(l.Line, NCHAR(30)) WITHIN GROUP (ORDER BY l.Line COLLATE Latin1_General_BIN2))
        INTO #lb
        FROM (SELECT e.StyleKey,
                     Line = CONCAT(CAST(ISNULL(CONVERT(nvarchar(20), bl.PartNo), N'~') AS nvarchar(max)), NCHAR(31), UPPER(m.MaterialCode),
                         NCHAR(31), ISNULL(bl.MaterialDescription, N'~'), NCHAR(31), ISNULL(bl.MaterialTypeCode, N'~'),
                         NCHAR(31), ISNULL(bl.ContentClassCode, N'~'), NCHAR(31), ISNULL(bl.NominatedSupplierCode, N'~'),
                         NCHAR(31), ISNULL(bl.NominatedSupplierName, N'~'), NCHAR(31), ISNULL(p.PartnerCode, N'~'),
                         NCHAR(31), ISNULL(CONVERT(nvarchar(40), bl.LcoConsumption), N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(40), bl.BrandConsumption), N'~'),
                         NCHAR(31), ISNULL(bl.UomCode, N'~'), NCHAR(31), k.Colors)
              FROM #ex e
              JOIN style.BomLine bl ON bl.StyleId = e.ExistingStyleId
              JOIN mat.Material m ON m.MaterialId = bl.MaterialId
              LEFT JOIN partner.Partner p ON p.PartnerId = bl.SupplierId
              LEFT JOIN #llc k ON k.BomLineId = bl.BomLineId) l
        GROUP BY l.StyleKey;

        UPDATE i SET
            Changes = NULLIF(CONCAT_WS(N', ',
                IIF(sh.HeaderHash = lh.HeaderHash
                    AND (sh.SketchUrl IS NULL OR sh.SketchUrl = lh.SketchUrl) AND (sh.ImageUrl IS NULL OR sh.ImageUrl = lh.ImageUrl), NULL, N'header'),
                IIF(ISNULL(sc.ColorwayHash, 0x) = ISNULL(lc.ColorwayHash, 0x)
                    AND NOT EXISTS (SELECT 1 FROM #c c JOIN style.Colorway k ON k.StyleId = i.ExistingStyleId AND k.ColorwayCode = c.ColorwayCode
                                    WHERE c.StyleKey = i.StyleKey AND c.ImageUrl IS NOT NULL AND c.ImageUrl <> ISNULL(k.ImageUrl, N'')), NULL, N'colorways'),
                IIF(ISNULL(sb.BomHash, 0x) = ISNULL(lb.BomHash, 0x), NULL, N'BOM')), N'')
        FROM staging.ImportStyle i
        JOIN #sh sh ON sh.StyleKey = i.StyleKey
        JOIN #lh lh ON lh.StyleKey = i.StyleKey
        LEFT JOIN #sc sc ON sc.StyleKey = i.StyleKey
        LEFT JOIN #lc lc ON lc.StyleKey = i.StyleKey
        LEFT JOIN #sb sb ON sb.StyleKey = i.StyleKey
        LEFT JOIN #lb lb ON lb.StyleKey = i.StyleKey
        WHERE i.BatchId = @BatchId;

        UPDATE staging.ImportStyle SET Action = IIF(Changes IS NULL, 'Unchanged', 'Changed')
        WHERE BatchId = @BatchId AND Action = 'New' AND ExistingStyleId IS NOT NULL;
    END

    /* Summary for the preview. */
    UPDATE staging.ImportBatch SET Summary = (
        SELECT
            [styles.total]     = (SELECT COUNT(*) FROM staging.ImportStyle WHERE BatchId = @BatchId),
            [styles.new]       = (SELECT COUNT(*) FROM staging.ImportStyle WHERE BatchId = @BatchId AND Action = 'New'),
            [styles.changed]   = (SELECT COUNT(*) FROM staging.ImportStyle WHERE BatchId = @BatchId AND Action = 'Changed'),
            [styles.unchanged] = (SELECT COUNT(*) FROM staging.ImportStyle WHERE BatchId = @BatchId AND Action = 'Unchanged'),
            [styles.blocked]   = (SELECT COUNT(*) FROM staging.ImportStyle WHERE BatchId = @BatchId AND Action = 'Blocked'),
            [rows.styles]      = (SELECT COUNT(*) FROM #s),
            [rows.colorways]   = (SELECT COUNT(*) FROM #c),
            [rows.bom]         = (SELECT COUNT(*) FROM #b),
            [bomLines]         = (SELECT COUNT(*) FROM (SELECT DISTINCT StyleKey, LineKey FROM #b) d),
            [errors]           = (SELECT COUNT(*) FROM @i WHERE Severity = 'Error'),
            [warnings]         = (SELECT COUNT(*) FROM @i WHERE Severity = 'Warning')
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    WHERE BatchId = @BatchId;

    EXEC staging.usp_StyleImport_Get @BatchId;
END
GO

/* ---------- 3. Commit ---------- */

/*
    Writes every New style and the Changed styles listed in @OverwriteStyleKeys (json array of StyleKey).
    A replaced style keeps its id; its colorways are updated in place (images uploaded in the app are kept when the
    file has none), its BOM is rebuilt (line images are carried over by part + material). Blocked and Unchanged
    styles are never written. Style history links are then rebuilt for the families the batch touched.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Commit
    @BatchId            int,
    @OverwriteStyleKeys nvarchar(max) = N'[]',
    @CommittedBy        nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    EXEC sp_getapplock @Resource = N'style.import', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;

    IF NOT EXISTS (SELECT 1 FROM staging.ImportBatch WITH (UPDLOCK) WHERE BatchId = @BatchId AND Status = 'Staged')
        THROW 50409, 'This import was already committed or cancelled.', 1;
    IF ISJSON(@OverwriteStyleKeys) = 0
        THROW 50400, 'The styles to overwrite must be a JSON array.', 1;

    DECLARE @now datetime2(3) = SYSUTCDATETIME();

    SELECT i.StyleKey, i.Action, i.ExistingStyleId, StyleId = i.ExistingStyleId
    INTO #w
    FROM staging.ImportStyle i
    WHERE i.BatchId = @BatchId
      AND (i.Action = 'New'
           OR (i.Action = 'Changed' AND i.StyleKey IN (SELECT [value] FROM OPENJSON(@OverwriteStyleKeys))));
    CREATE UNIQUE CLUSTERED INDEX IX_w ON #w (StyleKey);

    SELECT s.* INTO #s FROM staging.fn_StyleRows(@BatchId) s JOIN #w w ON w.StyleKey = s.StyleKey;
    SELECT c.* INTO #c FROM staging.fn_ColorwayRows(@BatchId) c JOIN #w w ON w.StyleKey = c.StyleKey;
    SELECT b.* INTO #b FROM staging.fn_BomRows(@BatchId) b JOIN #w w ON w.StyleKey = b.StyleKey;
    CREATE INDEX IX_b ON #b (StyleKey, LineKey);

    /* Reference data first. */
    -- Season terms are fixed (the check refuses unknown ones); seasons are added as they appear.
    INSERT ref.Season (SeasonCode, SeasonYear, Term)
    SELECT DISTINCT SeasonCode, SeasonYear, Term FROM #s s WHERE NOT EXISTS (SELECT 1 FROM ref.Season x WHERE x.SeasonCode = s.SeasonCode);
    INSERT ref.WeaveType (WeaveTypeCode, Name)
    SELECT WeaveTypeCode, ISNULL(MAX(WeaveTypeName), WeaveTypeCode) FROM #s s
    WHERE WeaveTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.WeaveType x WHERE x.WeaveTypeCode = s.WeaveTypeCode) GROUP BY WeaveTypeCode;
    INSERT ref.ProductType (ProductTypeCode, Name)
    SELECT ProductTypeCode, ISNULL(MAX(ProductTypeName), ProductTypeCode) FROM #s s
    WHERE ProductTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ProductType x WHERE x.ProductTypeCode = s.ProductTypeCode) GROUP BY ProductTypeCode;
    INSERT ref.BusinessUnit (BusinessUnitCode, Name)
    SELECT BusinessUnitCode, ISNULL(MAX(BusinessUnitName), BusinessUnitCode) FROM #s s
    WHERE BusinessUnitCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.BusinessUnit x WHERE x.BusinessUnitCode = s.BusinessUnitCode) GROUP BY BusinessUnitCode;
    INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy)
    SELECT DISTINCT 'Customer', CustomerCode, CustomerCode, @CommittedBy FROM #s s
    WHERE NOT EXISTS (SELECT 1 FROM partner.Partner x WHERE x.PartnerType = 'Customer' AND x.PartnerCode = s.CustomerCode);
    INSERT ref.MaterialType (MaterialTypeCode, Name)
    SELECT MaterialTypeCode, ISNULL(MAX(MaterialTypeName), MaterialTypeCode) FROM #b b
    WHERE MaterialTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.MaterialType x WHERE x.MaterialTypeCode = b.MaterialTypeCode) GROUP BY MaterialTypeCode;
    INSERT ref.Uom (UomCode, Name)
    SELECT DISTINCT UomCode, UomCode FROM #b b
    WHERE UomCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.Uom x WHERE x.UomCode = b.UomCode);
    INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy)
    SELECT 'Supplier', SupplierCode, ISNULL(MAX(SupplierName), SupplierCode), @CommittedBy FROM #b b
    WHERE SupplierCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM partner.Partner x WHERE x.PartnerType = 'Supplier' AND x.PartnerCode = b.SupplierCode)
    GROUP BY SupplierCode;

    /* Material master: the most common description / type / class for each code in this file. */
    SELECT MaterialCode, MaterialDescription, MaterialTypeCode, ContentClassCode
    INTO #m
    FROM (SELECT MaterialCode, MaterialDescription, MaterialTypeCode, ContentClassCode,
                 n = ROW_NUMBER() OVER (PARTITION BY MaterialCode ORDER BY COUNT(*) DESC, MIN(RowNo))
          FROM #b GROUP BY MaterialCode, MaterialDescription, MaterialTypeCode, ContentClassCode) d
    WHERE n = 1;
    INSERT mat.Material (MaterialCode, Description, MaterialTypeCode, ContentClassCode, CreatedBy)
    SELECT m.MaterialCode, m.MaterialDescription, m.MaterialTypeCode, m.ContentClassCode, @CommittedBy
    FROM #m m WHERE NOT EXISTS (SELECT 1 FROM mat.Material x WHERE x.MaterialCode = m.MaterialCode);
    UPDATE x SET Description = m.MaterialDescription, MaterialTypeCode = m.MaterialTypeCode, ContentClassCode = m.ContentClassCode,
                 UpdatedBy = @CommittedBy, UpdatedUtc = @now
    FROM mat.Material x JOIN #m m ON m.MaterialCode = x.MaterialCode
    WHERE EXISTS (SELECT x.Description, x.MaterialTypeCode, x.ContentClassCode EXCEPT SELECT m.MaterialDescription, m.MaterialTypeCode, m.ContentClassCode);

    /* Style headers: update replaced styles, insert new ones. */
    UPDATE st SET
        Description = s.Description, ModelCode = s.ModelCode, ModelName = s.ModelName, WeaveTypeCode = s.WeaveTypeCode,
        ProductTypeCode = s.ProductTypeCode, Gender = s.Gender, GarmentLeadTimeDays = s.LeadTimeDays, BusinessUnitCode = s.BusinessUnitCode,
        SourceCreatedUtc = s.SourceCreatedUtc, SketchUrl = ISNULL(s.SketchUrl, st.SketchUrl), ImageUrl = ISNULL(s.ImageUrl, st.ImageUrl),
        ImportBatchId = @BatchId, UpdatedBy = @CommittedBy, UpdatedUtc = @now
    FROM style.Style st
    JOIN #w w ON w.ExistingStyleId = st.StyleId
    JOIN (SELECT *, n = ROW_NUMBER() OVER (PARTITION BY StyleKey ORDER BY RowNo) FROM #s) s ON s.StyleKey = w.StyleKey AND s.n = 1;

    INSERT style.Style (CustomerId, SeasonCode, StyleNo, Description, ModelCode, ModelName, WeaveTypeCode, ProductTypeCode, Gender,
                        GarmentLeadTimeDays, BusinessUnitCode, SketchUrl, ImageUrl, SourceCreatedUtc, ImportBatchId, CreatedBy)
    SELECT p.PartnerId, s.SeasonCode, s.StyleNo, s.Description, s.ModelCode, s.ModelName, s.WeaveTypeCode, s.ProductTypeCode, s.Gender,
           s.LeadTimeDays, s.BusinessUnitCode, s.SketchUrl, s.ImageUrl, s.SourceCreatedUtc, @BatchId, @CommittedBy
    FROM (SELECT *, n = ROW_NUMBER() OVER (PARTITION BY StyleKey ORDER BY RowNo) FROM #s) s
    JOIN #w w ON w.StyleKey = s.StyleKey AND w.ExistingStyleId IS NULL
    JOIN partner.Partner p ON p.PartnerType = 'Customer' AND p.PartnerCode = s.CustomerCode
    WHERE s.n = 1;

    UPDATE w SET StyleId = st.StyleId
    FROM #w w
    JOIN (SELECT StyleKey, CustomerCode, SeasonCode, StyleNo, n = ROW_NUMBER() OVER (PARTITION BY StyleKey ORDER BY RowNo) FROM #s) s
      ON s.StyleKey = w.StyleKey AND s.n = 1
    JOIN partner.Partner p ON p.PartnerType = 'Customer' AND p.PartnerCode = s.CustomerCode
    JOIN style.Style st ON st.CustomerId = p.PartnerId AND st.SeasonCode = s.SeasonCode AND st.StyleNo = s.StyleNo
    WHERE w.StyleId IS NULL;

    /* Replaced styles: keep line images by part + material, then clear the old BOM. */
    SELECT bl.StyleId, bl.PartNo, bl.MaterialId, ImageUrl = MIN(bl.ImageUrl)
    INTO #lineImages
    FROM style.BomLine bl JOIN #w w ON w.ExistingStyleId = bl.StyleId
    WHERE bl.ImageUrl IS NOT NULL
    GROUP BY bl.StyleId, bl.PartNo, bl.MaterialId;

    DELETE blc FROM style.BomLineColorway blc JOIN style.BomLine bl ON bl.BomLineId = blc.BomLineId JOIN #w w ON w.ExistingStyleId = bl.StyleId;
    DELETE bl FROM style.BomLine bl JOIN #w w ON w.ExistingStyleId = bl.StyleId;

    /* Colorways: update in place, add new, remove those no longer in the file. */
    WITH src AS (
        SELECT w.StyleId, c.ColorwayCode, c.ColorwayName, c.Status, c.ImageUrl,
               SortOrder = ISNULL(c.SortOrder, ROW_NUMBER() OVER (PARTITION BY c.StyleKey ORDER BY c.RowNo))
        FROM #c c JOIN #w w ON w.StyleKey = c.StyleKey
    ),
    tgt AS (SELECT k.* FROM style.Colorway k WHERE k.StyleId IN (SELECT StyleId FROM #w))
    MERGE tgt AS t
    USING src AS s ON t.StyleId = s.StyleId AND t.ColorwayCode = s.ColorwayCode
    WHEN MATCHED AND EXISTS (SELECT t.ColorwayName, t.Status, t.SortOrder, t.ImageUrl
                             EXCEPT SELECT s.ColorwayName, s.Status, s.SortOrder, ISNULL(s.ImageUrl, t.ImageUrl)) THEN
        UPDATE SET ColorwayName = s.ColorwayName, Status = s.Status, SortOrder = s.SortOrder, ImageUrl = ISNULL(s.ImageUrl, t.ImageUrl),
                   UpdatedBy = @CommittedBy, UpdatedUtc = @now
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (StyleId, SortOrder, ColorwayCode, ColorwayName, Status, ImageUrl, CreatedBy)
        VALUES (s.StyleId, s.SortOrder, s.ColorwayCode, s.ColorwayName, s.Status, s.ImageUrl, @CommittedBy)
    WHEN NOT MATCHED BY SOURCE THEN DELETE;

    /* BOM lines: one per distinct line, numbered in file order. */
    SELECT w.StyleId, b.LineKey,
           LineSeq = ROW_NUMBER() OVER (PARTITION BY w.StyleId ORDER BY MIN(b.RowNo)),
           PartNo = MAX(b.PartNo), MaterialCode = MAX(b.MaterialCode), MaterialDescription = MAX(b.MaterialDescription),
           MaterialTypeCode = MAX(b.MaterialTypeCode), ContentClassCode = MAX(b.ContentClassCode),
           NominatedSupplierCode = MAX(b.NominatedSupplierCode), NominatedSupplierName = MAX(b.NominatedSupplierName),
           SupplierCode = MAX(b.SupplierCode), LcoConsumption = MAX(b.LcoConsumption), BrandConsumption = MAX(b.BrandConsumption),
           UomCode = MAX(b.UomCode), ImageUrl = MAX(b.ImageUrl)
    INTO #lines
    FROM #b b JOIN #w w ON w.StyleKey = b.StyleKey
    GROUP BY w.StyleId, b.LineKey;

    INSERT style.BomLine (StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode,
                          NominatedSupplierCode, NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ImageUrl, CreatedBy)
    SELECT l.StyleId, l.LineSeq, l.PartNo, m.MaterialId, l.MaterialDescription, l.MaterialTypeCode, l.ContentClassCode,
           l.NominatedSupplierCode, l.NominatedSupplierName, p.PartnerId, l.LcoConsumption, l.BrandConsumption, l.UomCode,
           ISNULL(l.ImageUrl, li.ImageUrl), @CommittedBy
    FROM #lines l
    JOIN mat.Material m ON m.MaterialCode = l.MaterialCode
    LEFT JOIN partner.Partner p ON p.PartnerType = 'Supplier' AND p.PartnerCode = l.SupplierCode
    LEFT JOIN #lineImages li ON li.StyleId = l.StyleId AND li.MaterialId = m.MaterialId AND EXISTS (SELECT li.PartNo INTERSECT SELECT l.PartNo);

    INSERT style.BomLineColorway (BomLineId, ColorwayId, MaterialColorCode, MaterialColorDescription)
    SELECT bl.BomLineId, k.ColorwayId, MIN(b.MaterialColorCode), MIN(b.MaterialColorDescription)
    FROM #b b
    JOIN #w w ON w.StyleKey = b.StyleKey
    JOIN #lines l ON l.StyleId = w.StyleId AND l.LineKey = b.LineKey
    JOIN style.BomLine bl ON bl.StyleId = l.StyleId AND bl.LineSeq = l.LineSeq
    JOIN style.Colorway k ON k.StyleId = w.StyleId AND k.ColorwayCode = b.ColorwayCode
    GROUP BY bl.BomLineId, k.ColorwayId;

    /* Style history for the families this batch touched (same customer + base style number + model). */
    SELECT st.StyleId, st.CustomerId, st.BaseStyleNo, st.ModelCode, st.StyleNo,
           OrderKey = se.SeasonYear * 100 + t.SortOrder, st.SeasonCode
    INTO #fam
    FROM style.Style st
    JOIN ref.Season se ON se.SeasonCode = st.SeasonCode
    JOIN ref.SeasonTerm t ON t.Term = se.Term
    WHERE st.ModelCode IS NOT NULL
      AND EXISTS (SELECT 1 FROM style.Style x JOIN #w w ON w.StyleId = x.StyleId
                  WHERE x.CustomerId = st.CustomerId AND x.BaseStyleNo = st.BaseStyleNo AND x.ModelCode = st.ModelCode);

    DELETE h FROM style.StyleHistory h JOIN #fam f ON f.StyleId = h.StyleId WHERE h.LinkSource = 'Import';

    INSERT style.StyleHistory (StyleId, SourceStyleId, Relation, Suffix, LinkSource, CreatedBy)
    SELECT f.StyleId, src.StyleId, src.Relation,
           IIF(CHARINDEX(N'_', f.StyleNo) > 1, SUBSTRING(f.StyleNo, CHARINDEX(N'_', f.StyleNo) + 1, 40), NULL),
           'Import', @CommittedBy
    FROM #fam f
    CROSS APPLY (
        SELECT TOP (1) c.StyleId, c.Relation FROM (
            -- the closest earlier season in the family
            SELECT * FROM (
                SELECT TOP (1) p.StyleId, Relation = 'CarryOver', Pri = 0
                FROM #fam p
                WHERE p.CustomerId = f.CustomerId AND p.BaseStyleNo = f.BaseStyleNo AND p.ModelCode = f.ModelCode AND p.OrderKey < f.OrderKey
                ORDER BY p.OrderKey DESC, LEN(p.StyleNo), p.StyleNo) e
            UNION ALL
            -- otherwise a variant of the family's first style in the same season (shortest style number first)
            SELECT * FROM (
                SELECT TOP (1) p.StyleId, Relation = 'Variant', Pri = 1
                FROM #fam p
                WHERE p.CustomerId = f.CustomerId AND p.BaseStyleNo = f.BaseStyleNo AND p.ModelCode = f.ModelCode AND p.OrderKey = f.OrderKey
                  AND p.StyleId <> f.StyleId AND (LEN(p.StyleNo) < LEN(f.StyleNo) OR (LEN(p.StyleNo) = LEN(f.StyleNo) AND p.StyleNo < f.StyleNo))
                ORDER BY LEN(p.StyleNo), p.StyleNo) v
        ) c ORDER BY c.Pri
    ) src
    WHERE NOT EXISTS (SELECT 1 FROM style.StyleHistory h WHERE h.StyleId = f.StyleId);

    /* Versions named with letters after another style's number ('S2808MR0000A'), whatever their model. */
    EXEC style.usp_StyleHistory_LinkByNumber @CommittedBy;

    /* Close the batch. */
    UPDATE i SET Committed = 1 FROM staging.ImportStyle i JOIN #w w ON w.StyleKey = i.StyleKey WHERE i.BatchId = @BatchId;

    DECLARE @written int = (SELECT COUNT(*) FROM #w), @new int = (SELECT COUNT(*) FROM #w WHERE ExistingStyleId IS NULL);
    UPDATE staging.ImportBatch SET
        Status = 'Committed', FinishedBy = @CommittedBy, FinishedUtc = @now,
        Summary = JSON_MODIFY(CAST(Summary AS nvarchar(max)), '$.committed', JSON_QUERY(
                    (SELECT styles = @written, new = @new, replaced = @written - @new, bomLines = (SELECT COUNT(*) FROM #lines)
                     FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)))
    WHERE BatchId = @BatchId;

    DELETE staging.StyleRow WHERE BatchId = @BatchId;
    DELETE staging.ArticleRow WHERE BatchId = @BatchId;
    DELETE staging.BomRow WHERE BatchId = @BatchId;

    COMMIT TRANSACTION;

    EXEC staging.usp_StyleImport_Get @BatchId;
END
GO

/* Discards a batch that has not been committed. */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Cancel
    @BatchId     int,
    @CancelledBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    UPDATE staging.ImportBatch SET Status = 'Cancelled', FinishedBy = @CancelledBy, FinishedUtc = SYSUTCDATETIME()
    WHERE BatchId = @BatchId AND Status = 'Staged';
    IF @@ROWCOUNT = 0
        THROW 50409, 'This import was already committed or cancelled.', 1;
    DELETE staging.StyleRow WHERE BatchId = @BatchId;
    DELETE staging.ArticleRow WHERE BatchId = @BatchId;
    DELETE staging.BomRow WHERE BatchId = @BatchId;
    COMMIT TRANSACTION;
END
GO

/* ---------- New codes (Settings > Import: "New codes" tab) ---------- */

/*
    Comparison key for codes and names: upper case without spaces and punctuation, and without a plural S
    ('PANTS (1/1)' = 'PANTS1/1', 'TRACK TOP' = 'TRACKTOP', 'JACKETS' = 'JACKET', 'YDS' = 'yd', 'PCS' = 'pc').
*/
CREATE OR ALTER FUNCTION staging.fn_CodeKey (@Value nvarchar(400))
RETURNS nvarchar(400)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @k nvarchar(400) = UPPER(TRANSLATE(@Value, N' ()-_/.,&''"', N'           '));
    SET @k = REPLACE(@k, N' ', N'');
    RETURN IIF(LEN(@k) > 2 AND RIGHT(@k, 1) = N'S', LEFT(@k, LEN(@k) - 1), @k);
END
GO

/*
    Codes the import would add to the reference lists, and values that block a row or are blanked, with the existing
    code they most likely mean (same comparison key as a code or a name). One row per list + value:
      customer, productType, weaveType, businessUnit, materialType, uom, supplier  -> added at commit (Blocking = 0)
      contentClass, status                                                        -> the row is blocked (Blocking = 1)
      gender                                                                      -> left blank (Blocking = 0)
    Value is as the check reads it (e.g. units in lower case). Label is the file's own name for the value, if any.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_NewCodes
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #v (List varchar(16) NOT NULL, Value nvarchar(150) NOT NULL, Label nvarchar(150) NULL, Rows int NOT NULL, Styles int NOT NULL, Blocking bit NOT NULL);

    INSERT #v
    SELECT 'customer', r.Cust, NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.Cust IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM partner.Partner p WHERE p.PartnerType = 'Customer' AND p.PartnerCode = r.Cust)
    GROUP BY r.Cust;

    INSERT #v
    SELECT 'productType', r.ProdTypeId, MAX(r.ProdTypeDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.ProdTypeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ProductType x WHERE x.ProductTypeCode = r.ProdTypeId)
    GROUP BY r.ProdTypeId;

    INSERT #v
    SELECT 'weaveType', UPPER(r.WeaveTypeId), MAX(r.WeaveTypeDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.WeaveTypeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.WeaveType x WHERE x.WeaveTypeCode = UPPER(r.WeaveTypeId))
    GROUP BY UPPER(r.WeaveTypeId);

    INSERT #v
    SELECT 'businessUnit', r.BuTeam, MAX(r.BuDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.BuTeam IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.BusinessUnit x WHERE x.BusinessUnitCode = r.BuTeam)
    GROUP BY r.BuTeam;

    INSERT #v
    SELECT 'gender', r.Gender, NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.StyleRow r CROSS APPLY (SELECT g = UPPER(r.Gender)) x
    WHERE r.BatchId = @BatchId AND r.Gender IS NOT NULL
      AND x.g NOT IN (N'MALE', N'MEN', N'MENS', N'M', N'FEMALE', N'WOMEN', N'WOMENS', N'W', N'LADIES', N'UNISEX', N'U',
                      N'KIDS', N'KID', N'BOYS', N'GIRLS', N'YOUTH', N'INFANT')
    GROUP BY r.Gender;

    INSERT #v
    SELECT 'status', r.Status, NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 1
    FROM staging.ArticleRow r
    WHERE r.BatchId = @BatchId AND r.Status IS NOT NULL AND UPPER(r.Status) NOT IN (N'INRANGE', N'DROPPED')
    GROUP BY r.Status;

    INSERT #v
    SELECT 'materialType', UPPER(r.MatTypeId), MAX(r.MatTypeDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.MatTypeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.MaterialType x WHERE x.MaterialTypeCode = UPPER(r.MatTypeId))
    GROUP BY UPPER(r.MatTypeId);

    INSERT #v
    SELECT 'uom', LOWER(r.Uom), NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.Uom IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.Uom x WHERE x.UomCode = LOWER(r.Uom))
    GROUP BY LOWER(r.Uom);

    INSERT #v
    SELECT 'contentClass', UPPER(r.ContentClass), NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 1
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.ContentClass IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ContentClass x WHERE x.ContentClassCode = UPPER(r.ContentClass))
    GROUP BY UPPER(r.ContentClass);

    INSERT #v
    SELECT 'supplier', r.SapSupplierId, MAX(r.SapSupplierName), COUNT(*), COUNT(DISTINCT r.StyleId), 0
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.SapSupplierId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM partner.Partner p WHERE p.PartnerType = 'Supplier' AND p.PartnerCode = r.SapSupplierId)
    GROUP BY r.SapSupplierId;

    -- What each list already holds (code + name), to suggest from.
    CREATE TABLE #ref (List varchar(16) NOT NULL, Code nvarchar(64) NOT NULL, Name nvarchar(150) NULL);
    INSERT #ref SELECT 'customer', PartnerCode, Name FROM partner.Partner WHERE PartnerType = 'Customer';
    INSERT #ref SELECT 'supplier', PartnerCode, Name FROM partner.Partner WHERE PartnerType = 'Supplier';
    INSERT #ref SELECT 'productType', ProductTypeCode, Name FROM ref.ProductType;
    INSERT #ref SELECT 'weaveType', WeaveTypeCode, Name FROM ref.WeaveType;
    INSERT #ref SELECT 'businessUnit', BusinessUnitCode, Name FROM ref.BusinessUnit;
    INSERT #ref SELECT 'materialType', MaterialTypeCode, Name FROM ref.MaterialType;
    INSERT #ref SELECT 'uom', UomCode, Name FROM ref.Uom;
    INSERT #ref SELECT 'contentClass', ContentClassCode, Name FROM ref.ContentClass;
    INSERT #ref VALUES ('gender', N'MALE', N'Male'), ('gender', N'FEMALE', N'Female'), ('gender', N'UNISEX', N'Unisex'), ('gender', N'KIDS', N'Kids'),
                       ('status', N'INRANGE', N'In range'), ('status', N'DROPPED', N'Dropped');

    SELECT v.List, v.Value, v.Label, v.Rows, v.Styles, v.Blocking, SuggestedCode = s.Code, SuggestedName = s.Name
    FROM #v v
    OUTER APPLY (
        SELECT TOP 1 r.Code, r.Name
        FROM #ref r
        WHERE r.List = v.List
          AND (staging.fn_CodeKey(r.Code) IN (staging.fn_CodeKey(v.Value), staging.fn_CodeKey(v.Label))
               OR staging.fn_CodeKey(r.Name) IN (staging.fn_CodeKey(v.Value), staging.fn_CodeKey(v.Label)))
        ORDER BY IIF(staging.fn_CodeKey(r.Code) = staging.fn_CodeKey(v.Value), 0, 1), r.Code) s
    ORDER BY v.Blocking DESC, CASE v.List WHEN 'customer' THEN 0 WHEN 'productType' THEN 1 WHEN 'weaveType' THEN 2 WHEN 'businessUnit' THEN 3
             WHEN 'gender' THEN 4 WHEN 'status' THEN 5 WHEN 'contentClass' THEN 6 WHEN 'materialType' THEN 7 WHEN 'uom' THEN 8 ELSE 9 END, v.Rows DESC;

    -- The lists' current codes (for the "use existing" pick lists and for the AI).
    SELECT List, Code, Name FROM #ref ORDER BY List, Code;
END
GO

/*
    Uses an existing code for a value in a staged workbook: rewrites the staged rows (all three sheets for a customer)
    and checks the batch again, so the preview shows the result. Refuses codes that are not on the list.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_MapCode
    @BatchId int,
    @List    varchar(16),
    @Value   nvarchar(150),
    @Code    nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM staging.ImportBatch WHERE BatchId = @BatchId AND Status = 'Staged')
        THROW 50404, 'This import does not exist or is no longer waiting to be committed.', 1;

    DECLARE @known bit = CASE @List
        WHEN 'customer'     THEN IIF(EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @Code), 1, 0)
        WHEN 'supplier'     THEN IIF(EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = @Code), 1, 0)
        WHEN 'productType'  THEN IIF(EXISTS (SELECT 1 FROM ref.ProductType WHERE ProductTypeCode = @Code), 1, 0)
        WHEN 'weaveType'    THEN IIF(EXISTS (SELECT 1 FROM ref.WeaveType WHERE WeaveTypeCode = @Code), 1, 0)
        WHEN 'businessUnit' THEN IIF(EXISTS (SELECT 1 FROM ref.BusinessUnit WHERE BusinessUnitCode = @Code), 1, 0)
        WHEN 'materialType' THEN IIF(EXISTS (SELECT 1 FROM ref.MaterialType WHERE MaterialTypeCode = @Code), 1, 0)
        WHEN 'uom'          THEN IIF(EXISTS (SELECT 1 FROM ref.Uom WHERE UomCode = @Code), 1, 0)
        WHEN 'contentClass' THEN IIF(EXISTS (SELECT 1 FROM ref.ContentClass WHERE ContentClassCode = @Code), 1, 0)
        WHEN 'gender'       THEN IIF(@Code IN (N'MALE', N'FEMALE', N'UNISEX', N'KIDS'), 1, 0)
        WHEN 'status'       THEN IIF(@Code IN (N'INRANGE', N'DROPPED'), 1, 0)
        ELSE 0 END;
    IF @known = 0 THROW 50400, 'Choose a code that is already on the list.', 1;

    BEGIN TRANSACTION;
    IF @List = 'customer'
    BEGIN
        UPDATE staging.StyleRow   SET Cust = @Code WHERE BatchId = @BatchId AND Cust = @Value;
        UPDATE staging.ArticleRow SET Cust = @Code WHERE BatchId = @BatchId AND Cust = @Value;
        UPDATE staging.BomRow     SET Cust = @Code WHERE BatchId = @BatchId AND Cust = @Value;
    END
    ELSE IF @List = 'productType'  UPDATE staging.StyleRow SET ProdTypeId = @Code WHERE BatchId = @BatchId AND ProdTypeId = @Value;
    ELSE IF @List = 'weaveType'    UPDATE staging.StyleRow SET WeaveTypeId = @Code WHERE BatchId = @BatchId AND UPPER(WeaveTypeId) = @Value;
    ELSE IF @List = 'businessUnit' UPDATE staging.StyleRow SET BuTeam = @Code WHERE BatchId = @BatchId AND BuTeam = @Value;
    ELSE IF @List = 'gender'       UPDATE staging.StyleRow SET Gender = @Code WHERE BatchId = @BatchId AND Gender = @Value;
    ELSE IF @List = 'status'       UPDATE staging.ArticleRow SET Status = @Code WHERE BatchId = @BatchId AND Status = @Value;
    ELSE IF @List = 'materialType' UPDATE staging.BomRow SET MatTypeId = @Code WHERE BatchId = @BatchId AND UPPER(MatTypeId) = @Value;
    ELSE IF @List = 'uom'          UPDATE staging.BomRow SET Uom = @Code WHERE BatchId = @BatchId AND LOWER(Uom) = @Value;
    ELSE IF @List = 'contentClass' UPDATE staging.BomRow SET ContentClass = @Code WHERE BatchId = @BatchId AND UPPER(ContentClass) = @Value;
    ELSE IF @List = 'supplier'
        UPDATE b SET SapSupplierId = @Code, SapSupplierName = p.Name
        FROM staging.BomRow b CROSS JOIN (SELECT Name FROM partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = @Code) p
        WHERE b.BatchId = @BatchId AND b.SapSupplierId = @Value;
    COMMIT TRANSACTION;

    EXEC staging.usp_StyleImport_Check @BatchId;
END
GO

PRINT 'Style Library import procedures ready.';
GO
