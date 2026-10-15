/*
    LT ODM Style Library - workbook import (Settings > Import, Admin only)

      1. usp_StyleImport_Create   the API creates a batch, then bulk-copies the three sheets into staging.*Row
      2. usp_StyleImport_Check    normalises and checks the rows, records issues, and decides per style:
                                  New / Changed / Unchanged / Blocked (staging.ImportStyle)
      3. usp_StyleImport_Commit   writes New styles and the Changed styles the user ticked, then links style history;
                                  records what it did (staging.ImportUndo*)
         usp_StyleImport_Cancel   discards a batch
         usp_StyleImport_Rollback undoes a commit (within 30 days, unless the styles were changed since)

    The workbook keeps the customer's headings (Article ID = colorway code). A BOM line is one distinct
    part + material + description + type + class + nominated supplier + consumptions + UOM + SAP supplier on a style;
    the workbook repeats it once per colorway (style.BomLineColorway). A BOM row without an Article ID applies to all of
    the style's colorways (a warning says so).
    Seasons are kept as the workbook writes them ('26FW', 'FA26', '2027-SS'); year and term are read from the name when
    possible (ref.fn_SeasonParts) and a season can be mapped to an existing one on the New codes tab.
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
    sp.SeasonYear,
    sp.Term,
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
    r.ImageUrl,
    CustomerName    = r.CustName,
    BrandCode       = UPPER(TRIM(r.CustBrand))
FROM staging.StyleRow AS r
OUTER APPLY ref.fn_SeasonParts(r.SeasonId) AS sp
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
        NCHAR(31), ISNULL(n.SupplierCode, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(40), n.ConsumptionAllowance), N'~')))
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
        AllowanceRaw          = r.ConsumpAllowance,
        ConsumptionAllowance  = TRY_CONVERT(decimal(18, 6), r.ConsumpAllowance),
        SupplierCode          = r.SapSupplierId,
        SupplierName          = r.SapSupplierName,
        -- No Article ID: the row is repeated for every article of its style (all colorways).
        ColorwayCode          = ISNULL(r.ArticleId, a.ArticleId),
        AllColorways          = CAST(IIF(r.ArticleId IS NULL, 1, 0) AS bit),
        MaterialColorCode     = r.MatColorId,
        MaterialColorDescription = r.MatColorDesc,
        r.ImageUrl
    FROM staging.BomRow AS r
    OUTER APPLY (SELECT x.ArticleId FROM staging.ArticleRow AS x
                 WHERE r.ArticleId IS NULL AND x.BatchId = r.BatchId AND x.StyleId = r.StyleId AND x.Cust = r.Cust
                   AND UPPER(x.SeasonId) = UPPER(r.SeasonId) AND x.ArticleId IS NOT NULL) AS a
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
    @UploadedBy nvarchar(64),
    @StoredFile nvarchar(64)  = NULL,   -- the kept workbook (Files:Root/style-imports)
    @Mapping    nvarchar(max) = NULL,   -- json ImportColumnMapping
    @Status     varchar(16)   = 'Staged' -- Mapping: file read, columns not mapped yet
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

    -- Rollback data is kept for 30 days after a commit.
    DELETE u FROM staging.ImportUndoStyle u JOIN staging.ImportBatch b ON b.BatchId = u.BatchId WHERE b.FinishedUtc < DATEADD(DAY, -30, SYSUTCDATETIME());
    DELETE u FROM staging.ImportUndoMaterial u JOIN staging.ImportBatch b ON b.BatchId = u.BatchId WHERE b.FinishedUtc < DATEADD(DAY, -30, SYSUTCDATETIME());
    DELETE u FROM staging.ImportUndoCode u JOIN staging.ImportBatch b ON b.BatchId = u.BatchId WHERE b.FinishedUtc < DATEADD(DAY, -30, SYSUTCDATETIME());

    IF @Status NOT IN ('Mapping', 'Staged') THROW 50400, 'A new import is Mapping or Staged.', 1;
    INSERT staging.ImportBatch (Kind, FileName, UploadedBy, StoredFile, Mapping, Status)
    VALUES ('StyleLibrary', @FileName, @UploadedBy, @StoredFile, @Mapping, @Status);
    SELECT BatchId = CAST(SCOPE_IDENTITY() AS int);
END
GO

/* ---------- Reading a batch ---------- */

/* 1 when the batch can be rolled back: committed in the last 30 days, with what the commit did recorded. */
CREATE OR ALTER FUNCTION staging.fn_CanRollBack (@BatchId int, @Status varchar(16), @FinishedUtc datetime2(3))
RETURNS bit
AS
BEGIN
    RETURN IIF(@Status = 'Committed' AND @FinishedUtc >= DATEADD(DAY, -30, SYSUTCDATETIME())
               AND EXISTS (SELECT 1 FROM staging.ImportUndoStyle WHERE BatchId = @BatchId), 1, 0);
END
GO

CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Get
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BatchId, FileName, Status, Summary = CAST(Summary AS nvarchar(max)), UploadedBy, UploadedUtc, FinishedBy, FinishedUtc,
           RolledBackBy, RolledBackUtc, CanRollBack = staging.fn_CanRollBack(BatchId, Status, FinishedUtc),
           HasFile = CAST(IIF(StoredFile IS NULL, 0, 1) AS bit)
    FROM staging.ImportBatch WHERE BatchId = @BatchId;
END
GO

CREATE OR ALTER PROCEDURE staging.usp_StyleImport_List
    @Top int = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top) BatchId, FileName, Status, Summary = CAST(Summary AS nvarchar(max)), UploadedBy, UploadedUtc, FinishedBy, FinishedUtc,
           RolledBackBy, RolledBackUtc, CanRollBack = staging.fn_CanRollBack(BatchId, Status, FinishedUtc),
           HasFile = CAST(IIF(StoredFile IS NULL, 0, 1) AS bit)
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
    @StyleKey nvarchar(150) = NULL,
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

/* ---------- Code rewrites (New codes tab, remembered answers) ---------- */

/* 1 when @Code is on the list (the lists the New codes tab maps to). */
CREATE OR ALTER FUNCTION staging.fn_IsKnownCode (@List varchar(16), @Code nvarchar(64))
RETURNS bit
AS
BEGIN
    RETURN CASE @List
        WHEN 'season'       THEN IIF(EXISTS (SELECT 1 FROM ref.Season WHERE SeasonCode = @Code), 1, 0)
        WHEN 'customer'     THEN IIF(EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @Code), 1, 0)
        WHEN 'brand'        THEN IIF(EXISTS (SELECT 1 FROM ref.Brand WHERE BrandCode = @Code), 1, 0)
        WHEN 'customerBrand' THEN IIF(EXISTS (SELECT 1 FROM ref.Brand WHERE BrandCode = @Code), 1, 0)
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
END
GO

/*
    Rewrites the staged rows that hold @Value (as the New codes tab shows it) to @Code: all three sheets for a season or
    customer. No checks; @Rows = rows changed. Callers check the code and run the check afterwards.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_RewriteCode
    @BatchId int,
    @List    varchar(16),
    @Value   nvarchar(150),
    @Code    nvarchar(64),
    @Rows    int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Rows = 0;
    IF @List = 'season'
    BEGIN
        UPDATE staging.StyleRow   SET SeasonId = @Code WHERE BatchId = @BatchId AND UPPER(SeasonId) = @Value; SET @Rows += @@ROWCOUNT;
        UPDATE staging.ArticleRow SET SeasonId = @Code WHERE BatchId = @BatchId AND UPPER(SeasonId) = @Value; SET @Rows += @@ROWCOUNT;
        UPDATE staging.BomRow     SET SeasonId = @Code WHERE BatchId = @BatchId AND UPPER(SeasonId) = @Value; SET @Rows += @@ROWCOUNT;
    END
    ELSE IF @List = 'customer'
    BEGIN
        UPDATE staging.StyleRow   SET Cust = @Code WHERE BatchId = @BatchId AND Cust = @Value; SET @Rows += @@ROWCOUNT;
        UPDATE staging.ArticleRow SET Cust = @Code WHERE BatchId = @BatchId AND Cust = @Value; SET @Rows += @@ROWCOUNT;
        UPDATE staging.BomRow     SET Cust = @Code WHERE BatchId = @BatchId AND Cust = @Value; SET @Rows += @@ROWCOUNT;
    END
    -- brand: a Customer_Brand value -> an existing brand; customerBrand: an account (Cust) -> its brand.
    ELSE IF @List = 'brand'         BEGIN UPDATE staging.StyleRow SET CustBrand = @Code WHERE BatchId = @BatchId AND UPPER(TRIM(CustBrand)) = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'customerBrand' BEGIN UPDATE staging.StyleRow SET CustBrand = @Code WHERE BatchId = @BatchId AND Cust = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'productType'  BEGIN UPDATE staging.StyleRow SET ProdTypeId = @Code WHERE BatchId = @BatchId AND ProdTypeId = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'weaveType'    BEGIN UPDATE staging.StyleRow SET WeaveTypeId = @Code WHERE BatchId = @BatchId AND UPPER(WeaveTypeId) = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'businessUnit' BEGIN UPDATE staging.StyleRow SET BuTeam = @Code WHERE BatchId = @BatchId AND BuTeam = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'gender'       BEGIN UPDATE staging.StyleRow SET Gender = @Code WHERE BatchId = @BatchId AND Gender = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'status'       BEGIN UPDATE staging.ArticleRow SET Status = @Code WHERE BatchId = @BatchId AND Status = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'materialType' BEGIN UPDATE staging.BomRow SET MatTypeId = @Code WHERE BatchId = @BatchId AND UPPER(MatTypeId) = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'uom'          BEGIN UPDATE staging.BomRow SET Uom = @Code WHERE BatchId = @BatchId AND LOWER(Uom) = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'contentClass' BEGIN UPDATE staging.BomRow SET ContentClass = @Code WHERE BatchId = @BatchId AND UPPER(ContentClass) = @Value; SET @Rows = @@ROWCOUNT; END
    ELSE IF @List = 'supplier'
    BEGIN
        UPDATE b SET SapSupplierId = @Code, SapSupplierName = p.Name
        FROM staging.BomRow b CROSS JOIN (SELECT Name FROM partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = @Code) p
        WHERE b.BatchId = @BatchId AND b.SapSupplierId = @Value;
        SET @Rows = @@ROWCOUNT;
    END
END
GO

/* Applies the remembered answers whose code still exists to a staged batch, and records what was applied. */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_ApplyAliases
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM staging.ImportCodeAlias) RETURN;

    DECLARE @List varchar(16), @Value nvarchar(150), @Code nvarchar(64), @rows int;
    DECLARE a CURSOR LOCAL FAST_FORWARD FOR
        SELECT List, Value, Code FROM staging.ImportCodeAlias WHERE staging.fn_IsKnownCode(List, Code) = 1
        ORDER BY IIF(List = 'customerBrand', 0, 1);   -- by account code, before an account is mapped to another customer
    OPEN a;
    FETCH NEXT FROM a INTO @List, @Value, @Code;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC staging.usp_StyleImport_RewriteCode @BatchId, @List, @Value, @Code, @rows OUTPUT;
        IF @rows > 0
            MERGE staging.ImportAppliedAlias AS t
            USING (SELECT @BatchId, @List, @Value, @Code, @rows) AS x (BatchId, List, Value, Code, Rows)
            ON t.BatchId = x.BatchId AND t.List = x.List AND t.Value = x.Value
            WHEN MATCHED THEN UPDATE SET Code = x.Code, Rows = t.Rows + x.Rows
            WHEN NOT MATCHED THEN INSERT (BatchId, List, Value, Code, Rows) VALUES (x.BatchId, x.List, x.Value, x.Code, x.Rows);
        FETCH NEXT FROM a INTO @List, @Value, @Code;
    END
    CLOSE a;
    DEALLOCATE a;
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

    -- Remembered answers (supplier 0001100131 = 1100131, customer 0000001304 = SKE) before anything is read.
    EXEC staging.usp_StyleImport_ApplyAliases @BatchId;

    DELETE staging.ImportIssue WHERE BatchId = @BatchId;
    DELETE staging.ImportStyle WHERE BatchId = @BatchId;

    SELECT * INTO #s FROM staging.fn_StyleRows(@BatchId);
    SELECT * INTO #c FROM staging.fn_ColorwayRows(@BatchId);
    SELECT * INTO #b FROM staging.fn_BomRows(@BatchId);
    CREATE INDEX IX_s ON #s (StyleKey);
    CREATE INDEX IX_c ON #c (StyleKey, ColorwayCode);
    CREATE INDEX IX_b ON #b (StyleKey, ColorwayCode);
    CREATE INDEX IX_b_line ON #b (StyleKey, LineKey) INCLUDE (ColorwayCode, MaterialColorCode, MaterialColorDescription);

    DECLARE @i TABLE (Sheet varchar(16), RowNo int, Severity varchar(8), StyleKey nvarchar(150), Message nvarchar(400));

    /* Style Header */
    INSERT @i SELECT 'Style', RowNo, 'Error', NULL, N'Cust, Season_ID and Style_ID are required.'
    FROM #s WHERE CustomerCode IS NULL OR SeasonCode IS NULL OR StyleNo IS NULL;

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, CONCAT(N'Season "', SeasonCode, N'" must not contain |.')
    FROM #s WHERE CHARINDEX(N'|', SeasonCode) > 0;

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, CONCAT(N'Style ', StyleNo, N' (', SeasonCode, N') appears more than once.')
    FROM (SELECT *, n = COUNT(*) OVER (PARTITION BY StyleKey) FROM #s WHERE StyleNo IS NOT NULL) d WHERE n > 1;

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, CONCAT(N'Garment_Lead_time "', LeadTimeRaw, N'" must be a number of days (0-730).')
    FROM #s WHERE LeadTimeRaw IS NOT NULL AND (LeadTimeDays IS NULL OR LeadTimeDays NOT BETWEEN 0 AND 730);

    INSERT @i SELECT 'Style', RowNo, 'Warning', StyleKey, CONCAT(N'Gender "', GenderRaw, N'" is not recognised and is left blank.')
    FROM #s WHERE GenderRaw IS NOT NULL AND Gender IS NULL;

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, CONCAT(N'Customer_Brand "', BrandCode, N'" must be at most 32 characters, without |.')
    FROM #s WHERE BrandCode IS NOT NULL AND (LEN(BrandCode) > 32 OR CHARINDEX(N'|', BrandCode) > 0);

    INSERT @i SELECT 'Style', s.RowNo, 'Error', s.StyleKey, CONCAT(N'Customer ', s.CustomerCode, N' has more than one Customer_Brand in this file.')
    FROM #s s WHERE s.BrandCode IS NOT NULL
      AND EXISTS (SELECT 1 FROM #s x WHERE x.CustomerCode = s.CustomerCode AND x.BrandCode IS NOT NULL AND x.BrandCode <> s.BrandCode);

    INSERT @i SELECT 'Style', RowNo, 'Error', StyleKey, N'Sketch and Image must be https:// addresses.'
    FROM #s WHERE (SketchUrl IS NOT NULL AND SketchUrl NOT LIKE N'https://%') OR (ImageUrl IS NOT NULL AND ImageUrl NOT LIKE N'https://%');

    /* Article (colorways) */
    -- With its style known, a row missing a value blocks that style (it could not be written).
    INSERT @i SELECT 'Article', RowNo, 'Error', IIF(Cust IS NULL OR SeasonId IS NULL OR StyleId IS NULL, NULL, StyleKey),
        N'Cust, Season ID, Style ID and Article ID are required.'
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
    INSERT @i SELECT 'BOM', RowNo, 'Error', NULL, N'Cust, Season_ID and Style_ID are required.'
    FROM (SELECT DISTINCT RowNo FROM #b WHERE Cust IS NULL OR SeasonId IS NULL OR StyleId IS NULL) d;

    -- No material code: the row cannot become a BOM line. It is flagged and skipped; the style imports without it.
    SELECT DISTINCT RowNo, StyleKey INTO #nomat FROM #b WHERE MaterialCode IS NULL AND Cust IS NOT NULL AND SeasonId IS NOT NULL AND StyleId IS NOT NULL;
    INSERT @i SELECT 'BOM', RowNo, 'Warning', StyleKey, N'No Ad_Mat_ID (material code): this BOM row is skipped.' FROM #nomat;
    DELETE #b WHERE MaterialCode IS NULL;

    INSERT @i SELECT 'BOM', MIN(RowNo), 'Warning', StyleKey,
        CONCAT(COUNT(DISTINCT RowNo), N' BOM row(s) have no Article ID: applied to all of the style''s colorways.')
    FROM #b WHERE AllColorways = 1 AND StyleId IS NOT NULL GROUP BY StyleKey;

    INSERT @i SELECT 'BOM', b.RowNo, 'Error', b.StyleKey, CONCAT(N'Style ', b.StyleId, N' (', b.SeasonId, N') is not on the Style Header sheet.')
    FROM (SELECT DISTINCT RowNo, StyleKey, StyleId, SeasonId FROM #b) b
    WHERE b.StyleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #s s WHERE s.StyleKey = b.StyleKey);

    INSERT @i SELECT 'BOM', b.RowNo, 'Error', b.StyleKey, CONCAT(N'Article ', b.ColorwayCode, N' is not on the Article sheet for this style.')
    FROM #b b WHERE b.ColorwayCode IS NOT NULL AND b.AllColorways = 0 AND EXISTS (SELECT 1 FROM #s s WHERE s.StyleKey = b.StyleKey)
      AND NOT EXISTS (SELECT 1 FROM #c c WHERE c.StyleKey = b.StyleKey AND c.ColorwayCode = b.ColorwayCode);

    -- Row checks once per Excel row (a row without an Article ID is repeated per colorway in #b).
    SELECT * INTO #br FROM (SELECT *, k = ROW_NUMBER() OVER (PARTITION BY RowNo ORDER BY ColorwayCode) FROM #b) d WHERE k = 1;

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, CONCAT(N'Part_No "', PartNoRaw, N'" must be a whole number.')
    FROM #br WHERE PartNoRaw IS NOT NULL AND PartNo IS NULL;

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, CONCAT(N'Consumption must be a number of 0 or more (LCO_Consump "', LcoRaw, N'", AD_Consump "', BrandRaw, N'").')
    FROM #br WHERE (LcoRaw IS NOT NULL AND (LcoConsumption IS NULL OR LcoConsumption < 0))
               OR (BrandRaw IS NOT NULL AND (BrandConsumption IS NULL OR BrandConsumption < 0));

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, CONCAT(N'Consump_Allowance "', AllowanceRaw, N'" must be a number of 0 or more.')
    FROM #br WHERE AllowanceRaw IS NOT NULL AND (ConsumptionAllowance IS NULL OR ConsumptionAllowance < 0);

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, CONCAT(N'Content_Class "', ContentClassCode, N'" must be one of ',
        (SELECT STRING_AGG(ContentClassCode, N', ') WITHIN GROUP (ORDER BY SortOrder) FROM ref.ContentClass), N'.')
    FROM #br b WHERE ContentClassCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ContentClass cc WHERE cc.ContentClassCode = b.ContentClassCode);

    INSERT @i SELECT 'BOM', RowNo, 'Error', StyleKey, N'Image must be an https:// address.'
    FROM #br WHERE ImageUrl IS NOT NULL AND ImageUrl NOT LIKE N'https://%';

    INSERT @i SELECT 'BOM', RowNo, 'Warning', StyleKey, CONCAT(N'Material ', MaterialCode, N' has no UOM.')
    FROM #br WHERE UomCode IS NULL AND MaterialCode IS NOT NULL;

    INSERT @i SELECT 'BOM', RowNo, 'Warning', StyleKey, N'Same BOM line and article as an earlier row: ignored.'
    FROM (SELECT RowNo, StyleKey, n = MIN(n) FROM (SELECT RowNo, StyleKey, n = ROW_NUMBER() OVER (PARTITION BY StyleKey, LineKey, ColorwayCode ORDER BY RowNo) FROM #b) x
          GROUP BY RowNo, StyleKey) d WHERE n > 1;

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
                   NCHAR(31), ISNULL(s.BusinessUnitCode, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(30), CAST(s.SourceCreatedUtc AS datetime2(3)), 121), N'~'),
                   NCHAR(31), ISNULL(s.CustomerName, N'~'))),
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
                         NCHAR(31), ISNULL(MAX(b.UomCode), N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(40), MAX(b.ConsumptionAllowance)), N'~'),
                         NCHAR(31), MAX(k.Colors))
              FROM #b b
              JOIN #slc k ON k.StyleKey = b.StyleKey AND k.LineKey = b.LineKey
              GROUP BY b.StyleKey, b.LineKey) l
        GROUP BY l.StyleKey;

        /* Library side. */
        SELECT e.StyleKey,
               HeaderHash = HASHBYTES('SHA2_256', CONCAT(CAST(ISNULL(st.Description, N'~') AS nvarchar(max)), NCHAR(31), ISNULL(st.ModelCode, N'~'),
                   NCHAR(31), ISNULL(st.ModelName, N'~'), NCHAR(31), ISNULL(st.WeaveTypeCode, N'~'), NCHAR(31), ISNULL(st.ProductTypeCode, N'~'),
                   NCHAR(31), ISNULL(st.Gender, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(10), st.GarmentLeadTimeDays), N'~'),
                   NCHAR(31), ISNULL(st.BusinessUnitCode, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(30), st.SourceCreatedUtc, 121), N'~'),
                   NCHAR(31), ISNULL(st.CustomerName, N'~'))),
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
                         NCHAR(31), ISNULL(bl.UomCode, N'~'), NCHAR(31), ISNULL(CONVERT(nvarchar(40), bl.ConsumptionAllowance), N'~'),
                         NCHAR(31), k.Colors)
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
            [rows.bom]         = (SELECT COUNT(DISTINCT RowNo) FROM #b) + (SELECT COUNT(*) FROM #nomat),
            [bomLines]         = (SELECT COUNT(*) FROM (SELECT DISTINCT StyleKey, LineKey FROM #b) d),
            [errors]           = (SELECT COUNT(*) FROM @i WHERE Severity = 'Error'),
            [warnings]         = (SELECT COUNT(*) FROM @i WHERE Severity = 'Warning'),
            [remembered]       = (SELECT COUNT(*) FROM staging.ImportAppliedAlias WHERE BatchId = @BatchId),
            [skippedBom.rows]   = (SELECT COUNT(*) FROM #nomat),
            [skippedBom.styles] = (SELECT COUNT(DISTINCT StyleKey) FROM #nomat)
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    WHERE BatchId = @BatchId;

    EXEC staging.usp_StyleImport_Get @BatchId;
END
GO

/*
    Rebuilds the import's style history links for families (@Families: json [{"c": CustomerId, "b": BaseStyleNo,
    "m": ModelCode}]) and links versions named by number. Used by commit and rollback.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_RebuildHistory
    @Families nvarchar(max),
    @By       nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT CustomerId, BaseStyleNo, ModelCode
    INTO #k
    FROM OPENJSON(ISNULL(@Families, N'[]')) WITH (CustomerId int '$.c', BaseStyleNo nvarchar(100) '$.b', ModelCode nvarchar(60) '$.m')
    WHERE ModelCode IS NOT NULL;

    /* Style history for the families this batch touched (same customer + base style number + model). Seasons whose
       term is not known (not normalised yet) have no order, so they are not linked here. */
    SELECT st.StyleId, st.CustomerId, st.BaseStyleNo, st.ModelCode, st.StyleNo,
           OrderKey = se.SeasonYear * 100 + t.SortOrder, st.SeasonCode
    INTO #fam
    FROM style.Style st
    JOIN ref.Season se ON se.SeasonCode = st.SeasonCode
    LEFT JOIN ref.SeasonTerm t ON t.Term = se.Term
    WHERE st.ModelCode IS NOT NULL
      AND EXISTS (SELECT 1 FROM #k k WHERE k.CustomerId = st.CustomerId AND k.BaseStyleNo = st.BaseStyleNo AND k.ModelCode = st.ModelCode);

    DELETE h FROM style.StyleHistory h JOIN #fam f ON f.StyleId = h.StyleId WHERE h.LinkSource = 'Import';

    INSERT style.StyleHistory (StyleId, SourceStyleId, Relation, Suffix, LinkSource, CreatedBy)
    SELECT f.StyleId, src.StyleId, src.Relation,
           IIF(CHARINDEX(N'_', f.StyleNo) > 1, SUBSTRING(f.StyleNo, CHARINDEX(N'_', f.StyleNo) + 1, 100), NULL),
           'Import', @By
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
    EXEC style.usp_StyleHistory_LinkByNumber @By;

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

    /* For rollback: each style it replaces as it is now (header, colorways, BOM lines with their colorways). */
    -- (ImportUndoCode may already hold customers / brands added from the Questions panel for this import: kept.)
    DELETE staging.ImportUndoStyle WHERE BatchId = @BatchId;
    DELETE staging.ImportUndoMaterial WHERE BatchId = @BatchId;
    INSERT staging.ImportUndoStyle (BatchId, StyleId, Kind, Snapshot)
    SELECT @BatchId, w.ExistingStyleId, 'R',
           (SELECT st.StyleNo, st.SeasonCode, st.Description, st.ModelCode, st.ModelName, st.WeaveTypeCode, st.ProductTypeCode, st.Gender,
                   st.GarmentLeadTimeDays, st.BusinessUnitCode, st.SketchUrl, st.ImageUrl, st.CustomerName, st.SourceCreatedUtc,
                   st.ImportBatchId, st.UpdatedBy, st.UpdatedUtc,
                   colorways = (SELECT k.ColorwayId, k.SortOrder, k.ColorwayCode, k.ColorwayName, k.Status, k.ImageUrl,
                                       k.CreatedBy, k.CreatedUtc, k.UpdatedBy, k.UpdatedUtc
                                FROM style.Colorway k WHERE k.StyleId = st.StyleId FOR JSON PATH),
                   bomLines = (SELECT bl.LineSeq, bl.PartNo, bl.MaterialId, bl.MaterialDescription, bl.MaterialTypeCode, bl.ContentClassCode,
                                      bl.NominatedSupplierCode, bl.NominatedSupplierName, bl.SupplierId, bl.LcoConsumption, bl.BrandConsumption,
                                      bl.UomCode, bl.ConsumptionAllowance, bl.ImageUrl, bl.CreatedBy, bl.CreatedUtc, bl.UpdatedBy, bl.UpdatedUtc,
                                      colorways = (SELECT blc.ColorwayId, blc.MaterialColorCode, blc.MaterialColorDescription
                                                   FROM style.BomLineColorway blc WHERE blc.BomLineId = bl.BomLineId FOR JSON PATH)
                               FROM style.BomLine bl WHERE bl.StyleId = st.StyleId FOR JSON PATH)
            FROM style.Style st WHERE st.StyleId = w.ExistingStyleId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    FROM #w w WHERE w.ExistingStyleId IS NOT NULL;

    SELECT s.* INTO #s FROM staging.fn_StyleRows(@BatchId) s JOIN #w w ON w.StyleKey = s.StyleKey;
    SELECT c.* INTO #c FROM staging.fn_ColorwayRows(@BatchId) c JOIN #w w ON w.StyleKey = c.StyleKey;
    -- Rows without a material code were flagged by the check and are skipped.
    SELECT b.* INTO #b FROM staging.fn_BomRows(@BatchId) b JOIN #w w ON w.StyleKey = b.StyleKey WHERE b.MaterialCode IS NOT NULL;
    CREATE INDEX IX_b ON #b (StyleKey, LineKey);

    /* Reference data first. */
    -- Seasons are added as they appear, under the workbook's name (year / term when the name shows them).
    INSERT ref.Season (SeasonCode, SeasonYear, Term)
    OUTPUT @BatchId, 'season', inserted.SeasonCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT DISTINCT SeasonCode, SeasonYear, Term FROM #s s WHERE NOT EXISTS (SELECT 1 FROM ref.Season x WHERE x.SeasonCode = s.SeasonCode);
    INSERT ref.WeaveType (WeaveTypeCode, Name)
    OUTPUT @BatchId, 'weaveType', inserted.WeaveTypeCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT WeaveTypeCode, ISNULL(MAX(WeaveTypeName), WeaveTypeCode) FROM #s s
    WHERE WeaveTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.WeaveType x WHERE x.WeaveTypeCode = s.WeaveTypeCode) GROUP BY WeaveTypeCode;
    INSERT ref.ProductType (ProductTypeCode, Name)
    OUTPUT @BatchId, 'productType', inserted.ProductTypeCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT ProductTypeCode, ISNULL(MAX(ProductTypeName), ProductTypeCode) FROM #s s
    WHERE ProductTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ProductType x WHERE x.ProductTypeCode = s.ProductTypeCode) GROUP BY ProductTypeCode;
    INSERT ref.BusinessUnit (BusinessUnitCode, Name)
    OUTPUT @BatchId, 'businessUnit', inserted.BusinessUnitCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT BusinessUnitCode, ISNULL(MAX(BusinessUnitName), BusinessUnitCode) FROM #s s
    WHERE BusinessUnitCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.BusinessUnit x WHERE x.BusinessUnitCode = s.BusinessUnitCode) GROUP BY BusinessUnitCode;
    INSERT ref.Brand (BrandCode, Name)
    OUTPUT @BatchId, 'brand', inserted.BrandCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT DISTINCT BrandCode, BrandCode FROM #s s
    WHERE BrandCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.Brand x WHERE x.BrandCode = s.BrandCode);
    INSERT partner.Partner (PartnerType, PartnerCode, Name, BrandCode, CreatedBy)
    OUTPUT @BatchId, 'customer', inserted.PartnerCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT 'Customer', CustomerCode, ISNULL(MAX(CustomerName), CustomerCode), MAX(BrandCode), @CommittedBy FROM #s s
    WHERE NOT EXISTS (SELECT 1 FROM partner.Partner x WHERE x.PartnerType = 'Customer' AND x.PartnerCode = s.CustomerCode)
    GROUP BY CustomerCode;
    -- Existing customers without a brand get the file's (a brand already set is kept).
    UPDATE p SET BrandCode = b.BrandCode, UpdatedBy = @CommittedBy, UpdatedUtc = @now
    OUTPUT @BatchId, 'customerBrand', inserted.PartnerCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    FROM partner.Partner p
    JOIN (SELECT CustomerCode, BrandCode = MAX(BrandCode) FROM #s WHERE BrandCode IS NOT NULL GROUP BY CustomerCode) b ON b.CustomerCode = p.PartnerCode
    WHERE p.PartnerType = 'Customer' AND p.BrandCode IS NULL;
    INSERT ref.MaterialType (MaterialTypeCode, Name)
    OUTPUT @BatchId, 'materialType', inserted.MaterialTypeCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT MaterialTypeCode, ISNULL(MAX(MaterialTypeName), MaterialTypeCode) FROM #b b
    WHERE MaterialTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.MaterialType x WHERE x.MaterialTypeCode = b.MaterialTypeCode) GROUP BY MaterialTypeCode;
    INSERT ref.Uom (UomCode, Name)
    OUTPUT @BatchId, 'uom', inserted.UomCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
    SELECT DISTINCT UomCode, UomCode FROM #b b
    WHERE UomCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.Uom x WHERE x.UomCode = b.UomCode);
    INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy)
    OUTPUT @BatchId, 'supplier', inserted.PartnerCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
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
    OUTPUT @BatchId, inserted.MaterialId, 'N' INTO staging.ImportUndoMaterial (BatchId, MaterialId, Kind)
    SELECT m.MaterialCode, m.MaterialDescription, m.MaterialTypeCode, m.ContentClassCode, @CommittedBy
    FROM #m m WHERE NOT EXISTS (SELECT 1 FROM mat.Material x WHERE x.MaterialCode = m.MaterialCode);
    UPDATE x SET Description = m.MaterialDescription, MaterialTypeCode = m.MaterialTypeCode, ContentClassCode = m.ContentClassCode,
                 UpdatedBy = @CommittedBy, UpdatedUtc = @now
    OUTPUT @BatchId, deleted.MaterialId, 'U', deleted.Description, deleted.MaterialTypeCode, deleted.ContentClassCode
    INTO staging.ImportUndoMaterial (BatchId, MaterialId, Kind, Description, MaterialTypeCode, ContentClassCode)
    FROM mat.Material x JOIN #m m ON m.MaterialCode = x.MaterialCode
    WHERE EXISTS (SELECT x.Description, x.MaterialTypeCode, x.ContentClassCode EXCEPT SELECT m.MaterialDescription, m.MaterialTypeCode, m.ContentClassCode);

    /* Style headers: update replaced styles, insert new ones. */
    UPDATE st SET
        Description = s.Description, ModelCode = s.ModelCode, ModelName = s.ModelName, WeaveTypeCode = s.WeaveTypeCode,
        ProductTypeCode = s.ProductTypeCode, Gender = s.Gender, GarmentLeadTimeDays = s.LeadTimeDays, BusinessUnitCode = s.BusinessUnitCode,
        SourceCreatedUtc = s.SourceCreatedUtc, SketchUrl = ISNULL(s.SketchUrl, st.SketchUrl), ImageUrl = ISNULL(s.ImageUrl, st.ImageUrl),
        CustomerName = s.CustomerName,
        ImportBatchId = @BatchId, UpdatedBy = @CommittedBy, UpdatedUtc = @now
    FROM style.Style st
    JOIN #w w ON w.ExistingStyleId = st.StyleId
    JOIN (SELECT *, n = ROW_NUMBER() OVER (PARTITION BY StyleKey ORDER BY RowNo) FROM #s) s ON s.StyleKey = w.StyleKey AND s.n = 1;

    INSERT style.Style (CustomerId, SeasonCode, StyleNo, Description, ModelCode, ModelName, WeaveTypeCode, ProductTypeCode, Gender,
                        GarmentLeadTimeDays, BusinessUnitCode, SketchUrl, ImageUrl, SourceCreatedUtc, CustomerName, ImportBatchId, CreatedBy)
    SELECT p.PartnerId, s.SeasonCode, s.StyleNo, s.Description, s.ModelCode, s.ModelName, s.WeaveTypeCode, s.ProductTypeCode, s.Gender,
           s.LeadTimeDays, s.BusinessUnitCode, s.SketchUrl, s.ImageUrl, s.SourceCreatedUtc, s.CustomerName, @BatchId, @CommittedBy
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

    INSERT staging.ImportUndoStyle (BatchId, StyleId, Kind)
    SELECT @BatchId, StyleId, 'N' FROM #w WHERE ExistingStyleId IS NULL AND StyleId IS NOT NULL;

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
           UomCode = MAX(b.UomCode), ConsumptionAllowance = MAX(b.ConsumptionAllowance), ImageUrl = MAX(b.ImageUrl)
    INTO #lines
    FROM #b b JOIN #w w ON w.StyleKey = b.StyleKey
    GROUP BY w.StyleId, b.LineKey;

    INSERT style.BomLine (StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode,
                          NominatedSupplierCode, NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode,
                          ConsumptionAllowance, ImageUrl, CreatedBy)
    SELECT l.StyleId, l.LineSeq, l.PartNo, m.MaterialId, l.MaterialDescription, l.MaterialTypeCode, l.ContentClassCode,
           l.NominatedSupplierCode, l.NominatedSupplierName, p.PartnerId, l.LcoConsumption, l.BrandConsumption, l.UomCode, l.ConsumptionAllowance,
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
    DECLARE @families nvarchar(max) = (SELECT DISTINCT c = st.CustomerId, b = st.BaseStyleNo, m = st.ModelCode
                                       FROM style.Style st JOIN #w w ON w.StyleId = st.StyleId WHERE st.ModelCode IS NOT NULL FOR JSON PATH);
    EXEC staging.usp_StyleImport_RebuildHistory @families, @CommittedBy;

    /* Close the batch. */
    UPDATE i SET Committed = 1 FROM staging.ImportStyle i JOIN #w w ON w.StyleKey = i.StyleKey WHERE i.BatchId = @BatchId;

    DECLARE @written int = (SELECT COUNT(*) FROM #w), @new int = (SELECT COUNT(*) FROM #w WHERE ExistingStyleId IS NULL);
    UPDATE staging.ImportBatch SET
        Status = 'Committed', FinishedBy = @CommittedBy, FinishedUtc = SYSUTCDATETIME(),   -- after every write of the commit
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

/* ---------- Roll back a commit ---------- */

/*
    Undoes a commit made in the last @MaxAgeDays days: removes the styles it added (their colorways, BOM, renders and
    picture data), puts the styles it replaced back as they were, restores the materials it changed, removes materials
    it added that nothing uses, rebuilds style history, then removes the reference codes it added that nothing uses
    (each on its own: a code still in use stays, the foreign keys decide).
    Stops without changing anything when a style was changed after the import (edited, replaced by a later import,
    colorways or BOM lines changed, AI renders made, history linked by hand) or a material it touched was edited:
    the first result set lists why. Second result set: the batch.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Rollback
    @BatchId      int,
    @RolledBackBy nvarchar(64),
    @MaxAgeDays   int = 30
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @b TABLE (StyleNo nvarchar(100) NULL, SeasonCode nvarchar(16) NULL, Reason nvarchar(400) NOT NULL);

    BEGIN TRANSACTION;
    EXEC sp_getapplock @Resource = N'style.import', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;

    DECLARE @at datetime2(3), @status varchar(16);
    SELECT @at = FinishedUtc, @status = Status FROM staging.ImportBatch WITH (UPDLOCK) WHERE BatchId = @BatchId;
    IF @status IS NULL THROW 50404, 'This import does not exist.', 1;
    IF @status <> 'Committed' THROW 50409, 'Only a committed import can be rolled back.', 1;
    IF NOT EXISTS (SELECT 1 FROM staging.ImportUndoStyle WHERE BatchId = @BatchId)
        THROW 50409, 'This import cannot be rolled back: it was committed before rollback was recorded, or more than 30 days ago.', 1;
    IF @at < DATEADD(DAY, -@MaxAgeDays, SYSUTCDATETIME())
    BEGIN
        DECLARE @old nvarchar(400) = CONCAT(N'Only imports committed in the last ', @MaxAgeDays, N' days can be rolled back.');
        THROW 50409, @old, 1;
    END

    SELECT u.StyleId, u.Kind, u.Snapshot, Present = IIF(st.StyleId IS NULL, 0, 1),
           StyleNo = ISNULL(st.StyleNo, JSON_VALUE(u.Snapshot, '$.StyleNo')), SeasonCode = ISNULL(st.SeasonCode, JSON_VALUE(u.Snapshot, '$.SeasonCode')),
           st.CustomerId, st.BaseStyleNo, st.ModelCode, st.ImportBatchId, st.UpdatedBy, st.UpdatedUtc
    INTO #u
    FROM staging.ImportUndoStyle u LEFT JOIN style.Style st ON st.StyleId = u.StyleId
    WHERE u.BatchId = @BatchId;
    CREATE UNIQUE CLUSTERED INDEX IX_u ON #u (StyleId);

    /* What changed after the import. */
    INSERT @b SELECT StyleNo, SeasonCode, N'The style was deleted after the import.' FROM #u WHERE Present = 0 AND Kind = 'R';
    INSERT @b SELECT StyleNo, SeasonCode, CONCAT(N'Replaced by a later import (#', ImportBatchId, N').')
    FROM #u WHERE Present = 1 AND ISNULL(ImportBatchId, 0) <> @BatchId;
    INSERT @b SELECT StyleNo, SeasonCode, CONCAT(N'Edited after the import by ', UpdatedBy, N' (', CONVERT(nvarchar(16), UpdatedUtc, 120), N' UTC).')
    FROM #u WHERE Present = 1 AND ImportBatchId = @BatchId AND UpdatedUtc > @at;
    INSERT @b SELECT u.StyleNo, u.SeasonCode, N'Colorways were added or changed after the import.'
    FROM #u u WHERE EXISTS (SELECT 1 FROM style.Colorway k WHERE k.StyleId = u.StyleId AND (k.CreatedUtc > @at OR k.UpdatedUtc > @at));
    INSERT @b SELECT u.StyleNo, u.SeasonCode, N'BOM lines were added or changed after the import.'
    FROM #u u WHERE EXISTS (SELECT 1 FROM style.BomLine bl WHERE bl.StyleId = u.StyleId AND (bl.CreatedUtc > @at OR bl.UpdatedUtc > @at));
    INSERT @b SELECT u.StyleNo, u.SeasonCode, CONCAT(COUNT(*), N' AI render(s) were made after the import.')
    FROM #u u JOIN style.AiRender r ON r.StyleId = u.StyleId WHERE r.CreatedUtc > @at GROUP BY u.StyleNo, u.SeasonCode;
    INSERT @b SELECT u.StyleNo, u.SeasonCode, N'Style history was linked by hand after the import.'
    FROM #u u WHERE EXISTS (SELECT 1 FROM style.StyleHistory h
                            WHERE h.LinkSource = 'Manual' AND (h.StyleId = u.StyleId OR h.SourceStyleId = u.StyleId)
                              AND ISNULL(h.UpdatedUtc, h.CreatedUtc) > @at);
    INSERT @b SELECT NULL, NULL, CONCAT(N'Material ', m.MaterialCode, N' was edited after the import.')
    FROM staging.ImportUndoMaterial um JOIN mat.Material m ON m.MaterialId = um.MaterialId
    WHERE um.BatchId = @BatchId AND m.UpdatedUtc > @at;

    IF EXISTS (SELECT 1 FROM @b)
    BEGIN
        ROLLBACK TRANSACTION;
        SELECT StyleNo, SeasonCode, Reason FROM @b ORDER BY IIF(StyleNo IS NULL, 1, 0), StyleNo, SeasonCode, Reason;
        EXEC staging.usp_StyleImport_Get @BatchId;
        RETURN;
    END

    DECLARE @now datetime2(3) = SYSUTCDATETIME();

    -- Families to relink at the end: as they are now (added styles leave theirs) and as they will be.
    SELECT DISTINCT CustomerId, BaseStyleNo, ModelCode INTO #fk FROM #u WHERE Present = 1 AND ModelCode IS NOT NULL;

    /* 1. Styles the import added. Renders and picture data go with them (cascade). */
    SELECT StyleId INTO #n FROM #u WHERE Kind = 'N' AND Present = 1;
    DELETE h FROM style.StyleHistory h WHERE h.StyleId IN (SELECT StyleId FROM #n) OR h.SourceStyleId IN (SELECT StyleId FROM #n);
    DELETE blc FROM style.BomLineColorway blc JOIN style.BomLine bl ON bl.BomLineId = blc.BomLineId JOIN #n n ON n.StyleId = bl.StyleId;
    DELETE bl FROM style.BomLine bl JOIN #n n ON n.StyleId = bl.StyleId;
    DELETE k FROM style.Colorway k JOIN #n n ON n.StyleId = k.StyleId;
    DELETE st FROM style.Style st JOIN #n n ON n.StyleId = st.StyleId;

    /* 2. Styles the import replaced: back to the copy taken before. */
    SELECT u.StyleId, h.*
    INTO #rh
    FROM #u u
    CROSS APPLY OPENJSON(u.Snapshot) WITH (
        Description nvarchar(400), ModelCode nvarchar(60), ModelName nvarchar(100), WeaveTypeCode nvarchar(8), ProductTypeCode nvarchar(40),
        Gender nvarchar(16), GarmentLeadTimeDays smallint, BusinessUnitCode nvarchar(16), SketchUrl nvarchar(500), ImageUrl nvarchar(500),
        CustomerName nvarchar(150), SourceCreatedUtc datetime2(3), ImportBatchId int, UpdatedBy nvarchar(64), UpdatedUtc datetime2(3)) h
    WHERE u.Kind = 'R' AND u.Present = 1;

    SELECT u.StyleId, c.*
    INTO #rc
    FROM #u u
    CROSS APPLY OPENJSON(u.Snapshot, '$.colorways') WITH (
        ColorwayId int, SortOrder smallint, ColorwayCode nvarchar(100), ColorwayName nvarchar(150), Status varchar(10), ImageUrl nvarchar(500),
        CreatedBy nvarchar(64), CreatedUtc datetime2(3), UpdatedBy nvarchar(64), UpdatedUtc datetime2(3)) c
    WHERE u.Kind = 'R' AND u.Present = 1;

    SELECT u.StyleId, l.*
    INTO #rl
    FROM #u u
    CROSS APPLY OPENJSON(u.Snapshot, '$.bomLines') WITH (
        LineSeq int, PartNo int, MaterialId int, MaterialDescription nvarchar(4000), MaterialTypeCode nvarchar(16), ContentClassCode nvarchar(8),
        NominatedSupplierCode nvarchar(32), NominatedSupplierName nvarchar(100), SupplierId int, LcoConsumption decimal(18, 6),
        BrandConsumption decimal(18, 6), UomCode nvarchar(8), ConsumptionAllowance decimal(18, 6), ImageUrl nvarchar(500),
        CreatedBy nvarchar(64), CreatedUtc datetime2(3), UpdatedBy nvarchar(64), UpdatedUtc datetime2(3), colorways nvarchar(max) AS JSON) l
    WHERE u.Kind = 'R' AND u.Present = 1;

    DELETE blc FROM style.BomLineColorway blc JOIN style.BomLine bl ON bl.BomLineId = blc.BomLineId JOIN #rh r ON r.StyleId = bl.StyleId;
    DELETE bl FROM style.BomLine bl JOIN #rh r ON r.StyleId = bl.StyleId;

    -- Colorways: those the import added go (with their picture data), changed ones get their values back, removed ones return.
    SELECT k.StyleId, k.ColorwayId INTO #gone
    FROM style.Colorway k JOIN #rh r ON r.StyleId = k.StyleId
    WHERE NOT EXISTS (SELECT 1 FROM #rc c WHERE c.ColorwayId = k.ColorwayId);
    DELETE k FROM style.Colorway k JOIN #gone g ON g.ColorwayId = k.ColorwayId;
    UPDATE k SET SortOrder = c.SortOrder, ColorwayName = c.ColorwayName, Status = c.Status, ImageUrl = c.ImageUrl,
                 UpdatedBy = c.UpdatedBy, UpdatedUtc = c.UpdatedUtc
    FROM style.Colorway k JOIN #rc c ON c.ColorwayId = k.ColorwayId;
    INSERT style.Colorway (StyleId, SortOrder, ColorwayCode, ColorwayName, Status, ImageUrl, CreatedBy, CreatedUtc, UpdatedBy, UpdatedUtc)
    SELECT c.StyleId, c.SortOrder, c.ColorwayCode, c.ColorwayName, c.Status, c.ImageUrl, c.CreatedBy, c.CreatedUtc, c.UpdatedBy, c.UpdatedUtc
    FROM #rc c WHERE NOT EXISTS (SELECT 1 FROM style.Colorway k WHERE k.ColorwayId = c.ColorwayId);
    -- Colorway picture data of colorways that are no longer there.
    DELETE i FROM style.ImageIndex i JOIN #rh r ON r.StyleId = i.StyleId
    WHERE i.Source = 'Colorway' AND NOT EXISTS (SELECT 1 FROM style.Colorway k WHERE k.ColorwayId = i.ItemId AND k.StyleId = i.StyleId);
    DELETE v FROM style.ImageVector v JOIN #rh r ON r.StyleId = v.StyleId
    WHERE v.Source = 'Colorway' AND NOT EXISTS (SELECT 1 FROM style.Colorway k WHERE k.ColorwayId = v.ItemId AND k.StyleId = v.StyleId);

    INSERT style.BomLine (StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
                          NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ConsumptionAllowance, ImageUrl,
                          CreatedBy, CreatedUtc, UpdatedBy, UpdatedUtc)
    SELECT StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
           NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ConsumptionAllowance, ImageUrl,
           CreatedBy, CreatedUtc, UpdatedBy, UpdatedUtc
    FROM #rl;
    -- Line colorways by line number and colorway code (colorways that came back have new ids).
    INSERT style.BomLineColorway (BomLineId, ColorwayId, MaterialColorCode, MaterialColorDescription)
    SELECT DISTINCT bl.BomLineId, k.ColorwayId, x.MaterialColorCode, x.MaterialColorDescription
    FROM #rl l
    CROSS APPLY OPENJSON(l.colorways) WITH (ColorwayId int, MaterialColorCode nvarchar(80), MaterialColorDescription nvarchar(150)) x
    JOIN #rc c ON c.StyleId = l.StyleId AND c.ColorwayId = x.ColorwayId
    JOIN style.Colorway k ON k.StyleId = l.StyleId AND k.ColorwayCode = c.ColorwayCode
    JOIN style.BomLine bl ON bl.StyleId = l.StyleId AND bl.LineSeq = l.LineSeq;

    UPDATE st SET Description = r.Description, ModelCode = r.ModelCode, ModelName = r.ModelName, WeaveTypeCode = r.WeaveTypeCode,
                  ProductTypeCode = r.ProductTypeCode, Gender = r.Gender, GarmentLeadTimeDays = r.GarmentLeadTimeDays,
                  BusinessUnitCode = r.BusinessUnitCode, SketchUrl = r.SketchUrl, ImageUrl = r.ImageUrl, CustomerName = r.CustomerName,
                  SourceCreatedUtc = r.SourceCreatedUtc, ImportBatchId = r.ImportBatchId, UpdatedBy = r.UpdatedBy, UpdatedUtc = r.UpdatedUtc
    FROM style.Style st JOIN #rh r ON r.StyleId = st.StyleId;

    /* 3. Materials: values the import overwrote come back; materials it added and nothing uses go (with their readings). */
    UPDATE m SET Description = u.Description, MaterialTypeCode = u.MaterialTypeCode, ContentClassCode = u.ContentClassCode,
                 UpdatedBy = @RolledBackBy, UpdatedUtc = @now
    FROM mat.Material m JOIN staging.ImportUndoMaterial u ON u.MaterialId = m.MaterialId
    WHERE u.BatchId = @BatchId AND u.Kind = 'U';
    DELETE m FROM mat.Material m JOIN staging.ImportUndoMaterial u ON u.MaterialId = m.MaterialId
    WHERE u.BatchId = @BatchId AND u.Kind = 'N' AND NOT EXISTS (SELECT 1 FROM style.BomLine bl WHERE bl.MaterialId = m.MaterialId);

    -- Brands the import gave existing customers.
    UPDATE p SET BrandCode = NULL, UpdatedBy = @RolledBackBy, UpdatedUtc = @now
    FROM partner.Partner p JOIN staging.ImportUndoCode u ON u.BatchId = @BatchId AND u.Kind = 'customerBrand' AND u.Code = p.PartnerCode
    WHERE p.PartnerType = 'Customer';

    /* 4. Style history of the families touched. */
    INSERT #fk SELECT st.CustomerId, st.BaseStyleNo, st.ModelCode FROM style.Style st JOIN #rh r ON r.StyleId = st.StyleId WHERE st.ModelCode IS NOT NULL;
    DECLARE @families nvarchar(max) = (SELECT DISTINCT c = CustomerId, b = BaseStyleNo, m = ModelCode FROM #fk FOR JSON PATH);
    EXEC staging.usp_StyleImport_RebuildHistory @families, @RolledBackBy;

    DECLARE @removed int = (SELECT COUNT(*) FROM #n), @restored int = (SELECT COUNT(*) FROM #rh);
    UPDATE staging.ImportStyle SET Committed = 0 WHERE BatchId = @BatchId;
    UPDATE staging.ImportBatch SET
        Status = 'RolledBack', RolledBackBy = @RolledBackBy, RolledBackUtc = @now,
        Summary = JSON_MODIFY(CAST(Summary AS nvarchar(max)), '$.rolledBack', JSON_QUERY(
                    (SELECT removed = @removed, restored = @restored FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)))
    WHERE BatchId = @BatchId;
    COMMIT TRANSACTION;

    /* 5. Reference codes the import added, each on its own: one still in use stays (its foreign keys refuse). */
    DECLARE @kind varchar(16), @code nvarchar(64), @codes int = 0;
    -- Brands last: their customers go first.
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
        SELECT Kind, Code FROM staging.ImportUndoCode WHERE BatchId = @BatchId AND Kind <> 'customerBrand' ORDER BY IIF(Kind = 'brand', 1, 0);
    OPEN c;
    FETCH NEXT FROM c INTO @kind, @code;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            IF @kind = 'season'            DELETE ref.Season WHERE SeasonCode = @code;
            ELSE IF @kind = 'weaveType'    DELETE ref.WeaveType WHERE WeaveTypeCode = @code;
            ELSE IF @kind = 'productType'  DELETE ref.ProductType WHERE ProductTypeCode = @code;
            ELSE IF @kind = 'businessUnit' DELETE ref.BusinessUnit WHERE BusinessUnitCode = @code;
            ELSE IF @kind = 'materialType' DELETE ref.MaterialType WHERE MaterialTypeCode = @code;
            ELSE IF @kind = 'uom'          DELETE ref.Uom WHERE UomCode = @code;
            ELSE IF @kind = 'customer'     DELETE partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @code;
            ELSE IF @kind = 'supplier'     DELETE partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = @code;
            ELSE IF @kind = 'brand'        DELETE ref.Brand WHERE BrandCode = @code;
            SET @codes += @@ROWCOUNT;
        END TRY
        BEGIN CATCH
            -- still used (e.g. by a later import): kept
        END CATCH
        FETCH NEXT FROM c INTO @kind, @code;
    END
    CLOSE c;
    DEALLOCATE c;

    UPDATE staging.ImportBatch SET Summary = JSON_MODIFY(CAST(Summary AS nvarchar(max)), '$.rolledBack.codes', @codes) WHERE BatchId = @BatchId;
    DELETE staging.ImportUndoStyle WHERE BatchId = @BatchId;
    DELETE staging.ImportUndoMaterial WHERE BatchId = @BatchId;
    DELETE staging.ImportUndoCode WHERE BatchId = @BatchId;

    SELECT StyleNo, SeasonCode, Reason FROM @b;   -- empty: done
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
      season, customer, productType, weaveType, businessUnit, materialType, uom, supplier -> added at commit (Blocking = 0)
      contentClass, status                                                        -> the row is blocked (Blocking = 1)
      gender                                                                      -> left blank (Blocking = 0)
    Value is as the check reads it (e.g. units in lower case). Label is the file's own name for the value, if any.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_NewCodes
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #v (List varchar(16) NOT NULL, Value nvarchar(150) NOT NULL, Label nvarchar(150) NULL, Rows int NOT NULL, Styles int NOT NULL, Blocking bit NOT NULL,
                     Brand nvarchar(100) NULL);   -- customers: the brand the file (or an answer) gives the account

    INSERT #v
    SELECT 'season', UPPER(r.SeasonId), NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.SeasonId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM ref.Season x WHERE x.SeasonCode = UPPER(r.SeasonId))
    GROUP BY UPPER(r.SeasonId);

    INSERT #v
    SELECT 'brand', UPPER(TRIM(r.CustBrand)), NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND NULLIF(TRIM(r.CustBrand), N'') IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM ref.Brand x WHERE x.BrandCode = UPPER(TRIM(r.CustBrand)))
    GROUP BY UPPER(TRIM(r.CustBrand));

    INSERT #v
    SELECT 'customer', r.Cust, MAX(r.CustName), COUNT(*), COUNT(DISTINCT r.StyleId), 0, MAX(UPPER(TRIM(r.CustBrand)))
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.Cust IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM partner.Partner p WHERE p.PartnerType = 'Customer' AND p.PartnerCode = r.Cust)
    GROUP BY r.Cust;

    INSERT #v
    SELECT 'productType', r.ProdTypeId, MAX(r.ProdTypeDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.ProdTypeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ProductType x WHERE x.ProductTypeCode = r.ProdTypeId)
    GROUP BY r.ProdTypeId;

    INSERT #v
    SELECT 'weaveType', UPPER(r.WeaveTypeId), MAX(r.WeaveTypeDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.WeaveTypeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.WeaveType x WHERE x.WeaveTypeCode = UPPER(r.WeaveTypeId))
    GROUP BY UPPER(r.WeaveTypeId);

    INSERT #v
    SELECT 'businessUnit', r.BuTeam, MAX(r.BuDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.StyleRow r
    WHERE r.BatchId = @BatchId AND r.BuTeam IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.BusinessUnit x WHERE x.BusinessUnitCode = r.BuTeam)
    GROUP BY r.BuTeam;

    INSERT #v
    SELECT 'gender', r.Gender, NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.StyleRow r CROSS APPLY (SELECT g = UPPER(r.Gender)) x
    WHERE r.BatchId = @BatchId AND r.Gender IS NOT NULL
      AND x.g NOT IN (N'MALE', N'MEN', N'MENS', N'M', N'FEMALE', N'WOMEN', N'WOMENS', N'W', N'LADIES', N'UNISEX', N'U',
                      N'KIDS', N'KID', N'BOYS', N'GIRLS', N'YOUTH', N'INFANT')
    GROUP BY r.Gender;

    INSERT #v
    SELECT 'status', r.Status, NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 1, NULL
    FROM staging.ArticleRow r
    WHERE r.BatchId = @BatchId AND r.Status IS NOT NULL AND UPPER(r.Status) NOT IN (N'INRANGE', N'DROPPED')
    GROUP BY r.Status;

    INSERT #v
    SELECT 'materialType', UPPER(r.MatTypeId), MAX(r.MatTypeDesc), COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.MatTypeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.MaterialType x WHERE x.MaterialTypeCode = UPPER(r.MatTypeId))
    GROUP BY UPPER(r.MatTypeId);

    INSERT #v
    SELECT 'uom', LOWER(r.Uom), NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.Uom IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.Uom x WHERE x.UomCode = LOWER(r.Uom))
    GROUP BY LOWER(r.Uom);

    INSERT #v
    SELECT 'contentClass', UPPER(r.ContentClass), NULL, COUNT(*), COUNT(DISTINCT r.StyleId), 1, NULL
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.ContentClass IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ContentClass x WHERE x.ContentClassCode = UPPER(r.ContentClass))
    GROUP BY UPPER(r.ContentClass);

    INSERT #v
    SELECT 'supplier', r.SapSupplierId, MAX(r.SapSupplierName), COUNT(*), COUNT(DISTINCT r.StyleId), 0, NULL
    FROM staging.BomRow r
    WHERE r.BatchId = @BatchId AND r.SapSupplierId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM partner.Partner p WHERE p.PartnerType = 'Supplier' AND p.PartnerCode = r.SapSupplierId)
    GROUP BY r.SapSupplierId;

    -- What each list already holds (code + name), to suggest from.
    CREATE TABLE #ref (List varchar(16) NOT NULL, Code nvarchar(64) NOT NULL, Name nvarchar(150) NULL);
    INSERT #ref SELECT 'season', SeasonCode, Description FROM ref.Season;
    INSERT #ref SELECT 'brand', BrandCode, Name FROM ref.Brand;
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

    /* Comparison keys worked out once per value and per code (not once per pair: a file with hundreds of new values
       against every supplier and customer took most of a minute). */
    SELECT v.*,
           ValueKey    = staging.fn_CodeKey(v.Value),
           LabelKey    = staging.fn_CodeKey(v.Label),
           -- SAP numbers written with leading zeros ('0001100131' = '1100131')
           Num         = IIF(v.List = 'supplier', TRY_CONVERT(bigint, v.Value), NULL),
           -- '26FW' / 'FW26' -> '2026-FW'
           SeasonCanon = IIF(v.List = 'season', (SELECT CONCAT(sp.SeasonYear, N'-', sp.Term) FROM ref.fn_SeasonParts(v.Value) sp WHERE sp.Term IS NOT NULL), NULL)
    INTO #vk FROM #v v;
    SELECT r.*, CodeKey = staging.fn_CodeKey(r.Code), NameKey = staging.fn_CodeKey(r.Name),
           Num = IIF(r.List = 'supplier', TRY_CONVERT(bigint, r.Code), NULL)
    INTO #rk FROM #ref r;
    CREATE INDEX IX_rk ON #rk (List, CodeKey) INCLUDE (Code, Name, NameKey, Num);

    SELECT v.List, v.Value, v.Label, v.Rows, v.Styles, v.Blocking, SuggestedCode = s.Code, SuggestedName = s.Name, v.Brand
    FROM #vk v
    OUTER APPLY (
        SELECT TOP 1 r.Code, r.Name
        FROM #rk r
        WHERE r.List = v.List
          AND (r.CodeKey IN (v.ValueKey, v.LabelKey) OR r.NameKey IN (v.ValueKey, v.LabelKey)
               OR (v.Num IS NOT NULL AND r.Num = v.Num)
               OR (v.SeasonCanon IS NOT NULL AND r.Code = v.SeasonCanon))
        ORDER BY IIF(r.CodeKey = v.ValueKey, 0, 1), r.Code) s
    ORDER BY v.Blocking DESC, CASE v.List WHEN 'season' THEN -2 WHEN 'brand' THEN -1 WHEN 'customer' THEN 0 WHEN 'productType' THEN 1 WHEN 'weaveType' THEN 2 WHEN 'businessUnit' THEN 3
             WHEN 'gender' THEN 4 WHEN 'status' THEN 5 WHEN 'contentClass' THEN 6 WHEN 'materialType' THEN 7 WHEN 'uom' THEN 8 ELSE 9 END, v.Rows DESC;

    -- The lists' current codes (for the "use existing" pick lists and for the AI).
    SELECT List, Code, Name FROM #ref ORDER BY List, Code;
END
GO

/*
    Uses existing codes for values in a staged workbook: @Items = json [{"list":..,"value":..,"code":..}]. Rewrites the
    staged rows, remembers the answers for later imports when @Remember = 1, and checks the batch again (once).
    Refuses (nothing changed) when a code is not on its list.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_MapCodes
    @BatchId  int,
    @Items    nvarchar(max),
    @Remember bit = 1,
    @MappedBy nvarchar(64) = N''
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM staging.ImportBatch WHERE BatchId = @BatchId AND Status = 'Staged')
        THROW 50404, 'This import does not exist or is no longer waiting to be committed.', 1;
    IF ISJSON(@Items) = 0 THROW 50400, 'The codes to use must be a JSON array.', 1;

    SELECT DISTINCT List = CAST(List AS varchar(16)), Value = CAST(Value AS nvarchar(150)), Code = CAST(Code AS nvarchar(64))
    INTO #m
    FROM OPENJSON(@Items) WITH (List varchar(16) '$.list', Value nvarchar(150) '$.value', Code nvarchar(64) '$.code')
    WHERE List IS NOT NULL AND Value IS NOT NULL AND Code IS NOT NULL;
    IF EXISTS (SELECT 1 FROM #m WHERE staging.fn_IsKnownCode(List, Code) = 0)
        THROW 50400, 'Choose a code that is already on the list.', 1;

    BEGIN TRANSACTION;
    DECLARE @List varchar(16), @Value nvarchar(150), @Code nvarchar(64);
    DECLARE m CURSOR LOCAL FAST_FORWARD FOR SELECT List, Value, Code FROM #m;
    OPEN m;
    FETCH NEXT FROM m INTO @List, @Value, @Code;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC staging.usp_StyleImport_RewriteCode @BatchId, @List, @Value, @Code;
        FETCH NEXT FROM m INTO @List, @Value, @Code;
    END
    CLOSE m;
    DEALLOCATE m;

    IF @Remember = 1
        MERGE staging.ImportCodeAlias AS t
        USING #m AS x ON t.List = x.List AND t.Value = x.Value
        WHEN MATCHED THEN UPDATE SET Code = x.Code, CreatedBy = @MappedBy, CreatedUtc = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN INSERT (List, Value, Code, CreatedBy) VALUES (x.List, x.Value, x.Code, @MappedBy);
    COMMIT TRANSACTION;

    EXEC staging.usp_StyleImport_Check @BatchId;
END
GO

/* One value (the New codes tab's "Use"): see usp_StyleImport_MapCodes. */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_MapCode
    @BatchId  int,
    @List     varchar(16),
    @Value    nvarchar(150),
    @Code     nvarchar(64),
    @Remember bit = 1,
    @MappedBy nvarchar(64) = N''
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @items nvarchar(max) = (SELECT list = @List, value = @Value, code = @Code FOR JSON PATH);
    EXEC staging.usp_StyleImport_MapCodes @BatchId, @items, @Remember, @MappedBy;
END
GO

/*
    Customer accounts that are one customer (several POLO accounts): adds customer @Code (named @Name) if it is new,
    then uses it for every account in @Values (json array of customer values), as usp_StyleImport_MapCodes does.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_GroupCustomers
    @BatchId  int,
    @Values   nvarchar(max),
    @Code     nvarchar(32),
    @Name     nvarchar(150),
    @Remember bit = 1,
    @MappedBy nvarchar(64) = N''
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM staging.ImportBatch WHERE BatchId = @BatchId AND Status = 'Staged')
        THROW 50404, 'This import does not exist or is no longer waiting to be committed.', 1;
    IF ISJSON(@Values) = 0 THROW 50400, 'The accounts must be a JSON array.', 1;

    IF NOT EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @Code)
    BEGIN
        INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy)
        VALUES ('Customer', @Code, ISNULL(NULLIF(TRIM(@Name), N''), @Code), @MappedBy);
        -- Added for this import: a rollback removes it again when unused.
        INSERT staging.ImportUndoCode (BatchId, Kind, Code) VALUES (@BatchId, 'customer', @Code);
    END

    DECLARE @items nvarchar(max) = (SELECT list = 'customer', value = [value], code = @Code FROM OPENJSON(@Values) FOR JSON PATH);
    EXEC staging.usp_StyleImport_MapCodes @BatchId, @items, @Remember, @MappedBy;
END
GO

/*
    Customer accounts that stay separate customers but share a brand (11 POLO accounts -> brand POLO): adds brand
    @Brand (named @Name) if it is new, gives it to @AlsoCustomer (an existing customer they look like, e.g. SKE) when
    that customer has no brand yet, then gives it to every account in @Values (json array) in the staged rows, as
    usp_StyleImport_MapCodes does (list customerBrand, remembered when @Remember = 1).
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_BrandCustomers
    @BatchId      int,
    @Values       nvarchar(max),
    @Brand        nvarchar(32),
    @Name         nvarchar(100),
    @AlsoCustomer nvarchar(32) = NULL,
    @Remember     bit = 1,
    @MappedBy     nvarchar(64) = N''
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM staging.ImportBatch WHERE BatchId = @BatchId AND Status = 'Staged')
        THROW 50404, 'This import does not exist or is no longer waiting to be committed.', 1;
    IF ISJSON(@Values) = 0 THROW 50400, 'The accounts must be a JSON array.', 1;

    BEGIN TRANSACTION;
    -- Added / given for this import: recorded so a rollback undoes them (brand removed when unused).
    IF NOT EXISTS (SELECT 1 FROM ref.Brand WHERE BrandCode = @Brand)
    BEGIN
        INSERT ref.Brand (BrandCode, Name) VALUES (@Brand, ISNULL(NULLIF(TRIM(@Name), N''), @Brand));
        INSERT staging.ImportUndoCode (BatchId, Kind, Code) VALUES (@BatchId, 'brand', @Brand);
    END
    IF @AlsoCustomer IS NOT NULL
        UPDATE partner.Partner SET BrandCode = @Brand, UpdatedBy = @MappedBy, UpdatedUtc = SYSUTCDATETIME()
        OUTPUT @BatchId, 'customerBrand', inserted.PartnerCode INTO staging.ImportUndoCode (BatchId, Kind, Code)
        WHERE PartnerType = 'Customer' AND PartnerCode = @AlsoCustomer AND BrandCode IS NULL
          AND NOT EXISTS (SELECT 1 FROM staging.ImportUndoCode u WHERE u.BatchId = @BatchId AND u.Kind = 'customerBrand' AND u.Code = @AlsoCustomer);
    COMMIT TRANSACTION;

    DECLARE @items nvarchar(max) = (SELECT list = 'customerBrand', value = [value], code = @Brand FROM OPENJSON(@Values) FOR JSON PATH);
    EXEC staging.usp_StyleImport_MapCodes @BatchId, @items, @Remember, @MappedBy;
END
GO

/* Remembered answers this import used. */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_AppliedAliases
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT List, Value, Code, Rows FROM staging.ImportAppliedAlias WHERE BatchId = @BatchId ORDER BY List, Value;
END
GO

/* Forgets a remembered answer (later imports ask again; rows already rewritten in a staged import stay as they are). */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_ForgetAlias
    @List  varchar(16),
    @Value nvarchar(150)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE staging.ImportCodeAlias WHERE List = @List AND Value = @Value;
    SELECT Deleted = @@ROWCOUNT;
END
GO

/* ---------- Kept workbooks: map again, resume, delete ---------- */

/* The kept workbook and the mapping of a batch (null StoredFile: not kept, or released). */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_GetFile
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BatchId, FileName, Status, StoredFile, Mapping FROM staging.ImportBatch WHERE BatchId = @BatchId;
END
GO

/* Saves the mapping being edited (Map columns) for an import that has not been committed. */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_SaveMapping
    @BatchId int,
    @Mapping nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    IF ISJSON(@Mapping) = 0 THROW 50400, 'The mapping must be JSON.', 1;
    UPDATE staging.ImportBatch SET Mapping = @Mapping WHERE BatchId = @BatchId AND Status IN ('Mapping', 'Staged', 'Cancelled');
    IF @@ROWCOUNT = 0 THROW 50409, 'The mapping of a committed import cannot change.', 1;
END
GO

/*
    Stages a kept workbook again with a (new) mapping: a draft (Mapping), an import waiting to commit or a cancelled one
    becomes Staged with no rows; the caller then bulk-copies the rows and runs usp_StyleImport_Check.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Restage
    @BatchId    int,
    @Mapping    nvarchar(max),
    @UploadedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM staging.ImportBatch WITH (UPDLOCK)
                   WHERE BatchId = @BatchId AND Status IN ('Mapping', 'Staged', 'Cancelled') AND StoredFile IS NOT NULL)
        THROW 50409, 'This import cannot be mapped again: it was committed, or its workbook is no longer kept. Upload the file again.', 1;
    DELETE staging.StyleRow WHERE BatchId = @BatchId;
    DELETE staging.ArticleRow WHERE BatchId = @BatchId;
    DELETE staging.BomRow WHERE BatchId = @BatchId;
    DELETE staging.ImportStyle WHERE BatchId = @BatchId;
    DELETE staging.ImportIssue WHERE BatchId = @BatchId;
    DELETE staging.ImportAppliedAlias WHERE BatchId = @BatchId;
    UPDATE staging.ImportBatch
    SET Status = 'Staged', Mapping = @Mapping, Summary = NULL, UploadedBy = @UploadedBy, UploadedUtc = SYSUTCDATETIME(),
        FinishedBy = NULL, FinishedUtc = NULL
    WHERE BatchId = @BatchId;
    COMMIT TRANSACTION;
END
GO

/* Deletes an import that was not committed (its rows go with it). Returns its kept workbook's name, to delete the file. */
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Delete
    @BatchId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @status varchar(16), @file nvarchar(64);
    BEGIN TRANSACTION;
    SELECT @status = Status, @file = StoredFile FROM staging.ImportBatch WITH (UPDLOCK) WHERE BatchId = @BatchId;
    IF @status IS NULL THROW 50404, 'This import does not exist.', 1;
    IF @status IN ('Committed', 'RolledBack')
        THROW 50409, 'A committed import stays in the list (it can be rolled back instead).', 1;
    DELETE staging.ImportBatch WHERE BatchId = @BatchId;   -- rows, styles, issues and applied answers cascade
    -- Brands given to existing customers from the Questions panel for this import go back.
    UPDATE p SET BrandCode = NULL FROM partner.Partner p
    JOIN staging.ImportUndoCode u ON u.BatchId = @BatchId AND u.Kind = 'customerBrand' AND u.Code = p.PartnerCode WHERE p.PartnerType = 'Customer';
    COMMIT TRANSACTION;

    -- Customers and brands the Questions panel added for it, when nothing uses them (each on its own).
    DECLARE @kind varchar(16), @code nvarchar(64);
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
        SELECT Kind, Code FROM staging.ImportUndoCode WHERE BatchId = @BatchId AND Kind IN ('customer', 'brand') ORDER BY IIF(Kind = 'brand', 1, 0);
    OPEN c;
    FETCH NEXT FROM c INTO @kind, @code;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            IF @kind = 'customer' DELETE partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @code;
            ELSE DELETE ref.Brand WHERE BrandCode = @code;
        END TRY
        BEGIN CATCH
        END CATCH
        FETCH NEXT FROM c INTO @kind, @code;
    END
    CLOSE c;
    DEALLOCATE c;
    DELETE staging.ImportUndoCode WHERE BatchId = @BatchId;
    SELECT StoredFile = @file;
END
GO

/*
    Kept workbooks no longer needed: committed or rolled-back imports at once; cancelled ones and unfinished mappings
    after 7 days (unfinished mappings are deleted). Returns the file names to delete.
*/
CREATE OR ALTER PROCEDURE staging.usp_StyleImport_Purge
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @files TABLE (StoredFile nvarchar(64) NOT NULL);
    DECLARE @cutoff datetime2(3) = DATEADD(DAY, -7, SYSUTCDATETIME());
    BEGIN TRANSACTION;
    DELETE staging.ImportBatch
    OUTPUT deleted.StoredFile INTO @files
    WHERE Status = 'Mapping' AND UploadedUtc < @cutoff AND StoredFile IS NOT NULL;
    DELETE staging.ImportBatch WHERE Status = 'Mapping' AND UploadedUtc < @cutoff;
    UPDATE staging.ImportBatch SET StoredFile = NULL
    OUTPUT deleted.StoredFile INTO @files
    WHERE StoredFile IS NOT NULL
      AND (Status IN ('Committed', 'RolledBack') OR (Status = 'Cancelled' AND ISNULL(FinishedUtc, UploadedUtc) < @cutoff));
    COMMIT TRANSACTION;
    SELECT StoredFile FROM @files;
END
GO

/* ---------- Saved column mappings (Settings > Import: Map columns) ---------- */

CREATE OR ALTER PROCEDURE staging.usp_ImportMapping_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT MappingId, Name, Definition, UpdatedBy = ISNULL(UpdatedBy, CreatedBy), UpdatedUtc = ISNULL(UpdatedUtc, CreatedUtc)
    FROM staging.ImportMapping
    ORDER BY ISNULL(UpdatedUtc, CreatedUtc) DESC;
END
GO

/* Saves a mapping under its name; a mapping with the same name is replaced. Returns the saved row. */
CREATE OR ALTER PROCEDURE staging.usp_ImportMapping_Save
    @Name       nvarchar(100),
    @Definition nvarchar(max),
    @SavedBy    nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@Definition) = 0 THROW 50400, 'The mapping must be JSON.', 1;

    BEGIN TRANSACTION;
    UPDATE staging.ImportMapping WITH (UPDLOCK, SERIALIZABLE)
    SET Definition = @Definition, UpdatedBy = @SavedBy, UpdatedUtc = SYSUTCDATETIME()
    WHERE Name = @Name;
    IF @@ROWCOUNT = 0
        INSERT staging.ImportMapping (Name, Definition, CreatedBy) VALUES (@Name, @Definition, @SavedBy);
    COMMIT TRANSACTION;

    SELECT MappingId, Name, Definition, UpdatedBy = ISNULL(UpdatedBy, CreatedBy), UpdatedUtc = ISNULL(UpdatedUtc, CreatedUtc)
    FROM staging.ImportMapping WHERE Name = @Name;
END
GO

CREATE OR ALTER PROCEDURE staging.usp_ImportMapping_Delete
    @MappingId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE staging.ImportMapping WHERE MappingId = @MappingId;
    SELECT Deleted = @@ROWCOUNT;
END
GO

PRINT 'Style Library import procedures ready.';
GO
