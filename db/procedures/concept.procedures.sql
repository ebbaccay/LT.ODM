/*
    LT ODM Concepts - LT's own design ideas (roadmap 5.7; tables in tables\concept.tables.sql)

    Reads:  usp_Concept_List, usp_Concept_Get, usp_Concept_ForStyle, usp_Concept_Lookups, usp_Concept_MatchContext
    Writes: usp_Concept_Save / _SaveBrief / _Delete, usp_Concept_DevelopStyle (an LT design as a new style, roadmap 5.21)
            usp_ConceptImage_Add / _Remove / _Save / _ToRead / _SetVector   (inspiration pictures, look-alike vectors, shown to the client)
            usp_ConceptStyle_Add / _Save / _Remove                   (design provenance, roadmap 5.1)
    Brand closeness (roadmap 5.2): usp_Concept_Closeness

    Same rules as the Style Packages procedures: writes to the concept row take its RowVer and refuse with 50409 if someone
    else saved it first; rule errors are THROWn as 50400, 50404 or 50409 with a message for the user; @ChangedBy is the
    signed-in user (from the API). Pictures, inspiration styles and the AI brief are saved on their own (no RowVer), so
    they do not clash with an edit of the details; the brief returns the concept's new RowVer.

    Requires tables\style.tables.sql, tables\concept.tables.sql, tables\pack.tables.sql and procedures\style.procedures.sql
    (usp_EnsureSeason). Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\concept.procedures.sql
*/
SET NOCOUNT ON;
GO

/* ---------- Reads ---------- */

/*
    Concepts for the list, newest change first. @Mine = 1 keeps the concepts owned by @UserName. CoverUrl is the first
    inspiration picture, else the first inspiration style's picture. Packages = packages with a style offered for it.
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_List
    @Search       nvarchar(100) = NULL,
    @CustomerCode nvarchar(32)  = NULL,
    @SeasonCode   nvarchar(16)  = NULL,
    @Category     nvarchar(20)  = NULL,
    @Status       varchar(10)   = NULL,
    @Mine         bit           = 0,
    @UserName     nvarchar(64)  = NULL,
    @Skip         int           = 0,
    @Take         int           = 50
AS
BEGIN
    SET NOCOUNT ON;
    SET @Search = NULLIF(LTRIM(RTRIM(@Search)), N'');
    SET @Skip = IIF(@Skip < 0, 0, @Skip);
    SET @Take = IIF(@Take BETWEEN 1 AND 200, @Take, 50);

    SELECT k.ConceptId
    INTO #c
    FROM concept.Concept k
    LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    WHERE (@CustomerCode IS NULL OR c.PartnerCode = @CustomerCode)
      AND (@SeasonCode IS NULL OR k.SeasonCode = @SeasonCode)
      AND (@Category IS NULL OR k.ProductCategoryCode = @Category)
      AND (@Status IS NULL OR k.Status = @Status)
      AND (@Mine = 0 OR k.OwnerName = @UserName)
      AND (@Search IS NULL OR k.Name LIKE N'%' + @Search + N'%' OR k.Description LIKE N'%' + @Search + N'%'
           OR c.PartnerCode = @Search OR c.Name LIKE N'%' + @Search + N'%'
           OR EXISTS (SELECT 1 FROM concept.ConceptTag t JOIN ref.TrendTag r ON r.TrendTagCode = t.TrendTagCode
                      WHERE t.ConceptId = k.ConceptId AND (t.TrendTagCode = @Search OR r.Name LIKE N'%' + @Search + N'%'))
           OR EXISTS (SELECT 1 FROM concept.ConceptStyle cs JOIN style.Style s ON s.StyleId = cs.StyleId
                      WHERE cs.ConceptId = k.ConceptId AND s.StyleNo LIKE @Search + N'%'));

    SELECT Total = COUNT(*) FROM #c;

    SELECT k.ConceptId, k.Name, CustomerCode = c.PartnerCode, CustomerName = c.Name, k.SeasonCode, k.ProductCategoryCode,
           ProductCategoryName = pc.Name, TargetMarketName = tm.Name, k.Gender, k.Status, k.OwnerName,
           Images = (SELECT COUNT(*) FROM concept.ConceptImage i WHERE i.ConceptId = k.ConceptId),
           Styles = (SELECT COUNT(*) FROM concept.ConceptStyle cs WHERE cs.ConceptId = k.ConceptId),
           Packages = (SELECT COUNT(DISTINCT pi.PackageId) FROM pack.PackageItem pi WHERE pi.ConceptId = k.ConceptId),
           Tags = (SELECT STRING_AGG(r.Name, N', ') WITHIN GROUP (ORDER BY r.Name)
                   FROM concept.ConceptTag t JOIN ref.TrendTag r ON r.TrendTagCode = t.TrendTagCode WHERE t.ConceptId = k.ConceptId),
           CoverUrl = COALESCE(
               (SELECT TOP (1) i.ImageUrl FROM concept.ConceptImage i WHERE i.ConceptId = k.ConceptId ORDER BY i.SortOrder, i.ConceptImageId),
               (SELECT TOP (1) ISNULL(s.ImageUrl, s.SketchUrl) FROM concept.ConceptStyle cs JOIN style.Style s ON s.StyleId = cs.StyleId
                WHERE cs.ConceptId = k.ConceptId AND (s.ImageUrl IS NOT NULL OR s.SketchUrl IS NOT NULL) ORDER BY cs.AddedUtc, cs.StyleId)),
           LastChangedUtc = (SELECT MAX(v) FROM (VALUES (k.CreatedUtc), (k.UpdatedUtc), (k.BriefUtc),
                                ((SELECT MAX(i.AddedUtc) FROM concept.ConceptImage i WHERE i.ConceptId = k.ConceptId)),
                                ((SELECT MAX(cs.AddedUtc) FROM concept.ConceptStyle cs WHERE cs.ConceptId = k.ConceptId))) x (v))
    FROM #c f
    JOIN concept.Concept k ON k.ConceptId = f.ConceptId
    LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    LEFT JOIN ref.ProductCategory pc ON pc.ProductCategoryCode = k.ProductCategoryCode
    LEFT JOIN ref.TargetMarket tm ON tm.TargetMarketCode = k.TargetMarketCode
    ORDER BY LastChangedUtc DESC, k.ConceptId DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

/*
    A concept with: its trend tags; its inspiration pictures (HasVector = read by @Model, the image model; NULL @Model =
    never); its inspiration styles (SameBrand = the concept customer's own or its brand's; NULL when the concept has no
    customer); the package items offered for it; and the styles developed from it (LT designs, style.Style.ConceptId).
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_Get
    @ConceptId int,
    @Model     nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = k.CustomerId, @brand = c.BrandCode
    FROM concept.Concept k LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId WHERE k.ConceptId = @ConceptId;

    SELECT k.ConceptId, k.Name, CustomerCode = c.PartnerCode, CustomerName = c.Name, c.BrandCode, k.SeasonCode,
           k.TargetMarketCode, TargetMarketName = tm.Name, k.ProductCategoryCode, ProductCategoryName = pc.Name, k.Gender,
           k.TargetPrice, k.Description, k.Status, k.OwnerName, k.BriefSummary, k.BriefJson, k.BriefBy, k.BriefUtc,
           k.CreatedBy, k.CreatedUtc, k.UpdatedBy, k.UpdatedUtc, k.RowVer, k.ClientStory
    FROM concept.Concept k
    LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    LEFT JOIN ref.TargetMarket tm ON tm.TargetMarketCode = k.TargetMarketCode
    LEFT JOIN ref.ProductCategory pc ON pc.ProductCategoryCode = k.ProductCategoryCode
    WHERE k.ConceptId = @ConceptId;

    SELECT t.TrendTagCode, r.Name
    FROM concept.ConceptTag t JOIN ref.TrendTag r ON r.TrendTagCode = t.TrendTagCode
    WHERE t.ConceptId = @ConceptId
    ORDER BY r.Name;

    SELECT i.ConceptImageId, i.ImageUrl, i.Caption, i.SortOrder,
           HasVector = CAST(IIF(i.Vector IS NOT NULL AND i.Model = @Model, 1, 0) AS bit), i.VectorError, i.AddedBy, i.AddedUtc, i.ForClient
    FROM concept.ConceptImage i
    WHERE i.ConceptId = @ConceptId
    ORDER BY i.SortOrder, i.ConceptImageId;

    SELECT cs.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = sc.PartnerCode, sc.BrandCode, s.ModelName, s.Description,
           ProductTypeName = pt.Name, s.Gender, s.ImageUrl, s.SketchUrl, s.IsActive,
           SameBrand = CAST(CASE WHEN @customerId IS NULL THEN NULL
                                 WHEN s.CustomerId = @customerId OR (@brand IS NOT NULL AND sc.BrandCode = @brand) THEN 1 ELSE 0 END AS bit),
           cs.Note, cs.AddedFrom, cs.AddedBy, cs.AddedUtc
    FROM concept.ConceptStyle cs
    JOIN style.Style s ON s.StyleId = cs.StyleId
    JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE cs.ConceptId = @ConceptId
    ORDER BY cs.AddedUtc, cs.StyleId;

    SELECT pi.PackageItemId, pi.PackageId, PackageName = p.Name, PackageStatus = p.Status, CustomerCode = c.PartnerCode,
           pi.StyleId, s.StyleNo, s.SeasonCode, pi.Outcome
    FROM pack.PackageItem pi
    JOIN pack.Package p ON p.PackageId = pi.PackageId
    JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    JOIN style.Style s ON s.StyleId = pi.StyleId
    WHERE pi.ConceptId = @ConceptId
    ORDER BY p.PackageId DESC, pi.SortOrder;

    SELECT s.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, s.ImageUrl, s.SketchUrl, s.OriginStyleId,
           OriginStyleNo = o.StyleNo, s.CreatedBy, s.CreatedUtc,
           Packages = (SELECT COUNT(*) FROM pack.PackageItem pi WHERE pi.StyleId = s.StyleId)
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN style.Style o ON o.StyleId = s.OriginStyleId
    WHERE s.ConceptId = @ConceptId
    ORDER BY s.CreatedUtc, s.StyleId;
END
GO

/* Concepts of a style (style page): Kind Developed = the style is an LT design developed from it; Inspired = it inspired them. */
CREATE OR ALTER PROCEDURE concept.usp_Concept_ForStyle
    @StyleId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT k.ConceptId, k.Name, CustomerCode = c.PartnerCode, k.SeasonCode, k.Status, k.OwnerName, Note = CAST(NULL AS nvarchar(500)),
           Kind = 'Developed'
    FROM style.Style s
    JOIN concept.Concept k ON k.ConceptId = s.ConceptId
    LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    WHERE s.StyleId = @StyleId
    UNION ALL
    SELECT k.ConceptId, k.Name, c.PartnerCode, k.SeasonCode, k.Status, k.OwnerName, cs.Note, 'Inspired'
    FROM concept.ConceptStyle cs
    JOIN concept.Concept k ON k.ConceptId = cs.ConceptId
    LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    WHERE cs.StyleId = @StyleId
    ORDER BY Kind, Name;
END
GO

/* The concept pick lists (active rows): target markets, trend tags, product categories. */
CREATE OR ALTER PROCEDURE concept.usp_Concept_Lookups
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Code = TargetMarketCode, Name FROM ref.TargetMarket WHERE IsActive = 1 ORDER BY Name;
    SELECT Code = TrendTagCode, Name FROM ref.TrendTag WHERE IsActive = 1 ORDER BY Name;
    SELECT Code = ProductCategoryCode, Name FROM ref.ProductCategory WHERE IsActive = 1 ORDER BY Name;
END
GO

/* ---------- Concepts ---------- */

/*
    Creates (@ConceptId NULL) or updates a concept's details and trend tags (@TrendTags: JSON array of codes; NULL leaves
    the tags as they are). A customer code typed by the user that is new is added to the customer list; codes of the other
    pick lists must be on them. Returns ConceptId.
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_Save
    @ConceptId           int            = NULL,
    @RowVer              binary(8)      = NULL,
    @Name                nvarchar(150),
    @CustomerCode        nvarchar(32)   = NULL,
    @SeasonCode          nvarchar(16)   = NULL,
    @TargetMarketCode    nvarchar(16)   = NULL,
    @ProductCategoryCode nvarchar(20)   = NULL,
    @Gender              nvarchar(16)   = NULL,
    @TargetPrice         nvarchar(50)   = NULL,
    @Description         nvarchar(2000) = NULL,
    @Status              varchar(10)    = 'Draft',
    @OwnerName           nvarchar(64)   = NULL,
    @TrendTags           nvarchar(max)  = NULL,
    @ClientStory         nvarchar(2000) = NULL,
    @ChangedBy           nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Name = NULLIF(LTRIM(RTRIM(@Name)), N'');
    SET @CustomerCode = NULLIF(LTRIM(RTRIM(@CustomerCode)), N'');
    SET @SeasonCode = NULLIF(LTRIM(RTRIM(@SeasonCode)), N'');
    SET @TargetMarketCode = NULLIF(LTRIM(RTRIM(@TargetMarketCode)), N'');
    SET @ProductCategoryCode = NULLIF(LTRIM(RTRIM(@ProductCategoryCode)), N'');
    SET @Gender = NULLIF(LTRIM(RTRIM(@Gender)), N'');
    SET @TargetPrice = NULLIF(LTRIM(RTRIM(@TargetPrice)), N'');
    SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');
    SET @OwnerName = NULLIF(LTRIM(RTRIM(@OwnerName)), N'');
    SET @ClientStory = NULLIF(LTRIM(RTRIM(@ClientStory)), N'');

    IF @Name IS NULL THROW 50400, 'Give the concept a name.', 1;
    IF @Status IS NULL OR @Status NOT IN ('Draft', 'Ready', 'Dropped') THROW 50400, 'Choose a status from the list.', 1;
    IF @Gender IS NOT NULL AND @Gender NOT IN ('MALE', 'FEMALE', 'UNISEX', 'KIDS') THROW 50400, 'Choose a gender from the list.', 1;
    IF @TargetMarketCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.TargetMarket WHERE TargetMarketCode = @TargetMarketCode)
        THROW 50400, 'Choose a target market from the list.', 1;
    IF @ProductCategoryCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ProductCategory WHERE ProductCategoryCode = @ProductCategoryCode)
        THROW 50400, 'Choose a product category from the list.', 1;
    IF @TrendTags IS NOT NULL AND ISJSON(@TrendTags) = 0 THROW 50400, 'The trend tags must be a JSON array.', 1;

    DECLARE @tags TABLE (Code nvarchar(40) NOT NULL PRIMARY KEY);
    INSERT @tags SELECT DISTINCT LEFT(TRIM([value]), 40) FROM OPENJSON(ISNULL(@TrendTags, N'[]')) WHERE TRIM([value]) <> N'';
    IF EXISTS (SELECT 1 FROM @tags t WHERE NOT EXISTS (SELECT 1 FROM ref.TrendTag r WHERE r.TrendTagCode = t.Code))
        THROW 50400, 'A trend tag is not on the list. Reload the page and choose again.', 1;

    BEGIN TRANSACTION;
    IF @ConceptId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM concept.Concept WITH (UPDLOCK) WHERE ConceptId = @ConceptId)
        THROW 50404, 'This concept no longer exists.', 1;
    IF @ConceptId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM concept.Concept WHERE ConceptId = @ConceptId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this concept after you opened it. Reload and try again.', 1;

    IF @SeasonCode IS NOT NULL EXEC style.usp_EnsureSeason @SeasonCode;
    IF @CustomerCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode)
        INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy) VALUES ('Customer', @CustomerCode, @CustomerCode, @ChangedBy);
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);

    IF @ConceptId IS NULL
    BEGIN
        INSERT concept.Concept (Name, CustomerId, SeasonCode, TargetMarketCode, ProductCategoryCode, Gender, TargetPrice, Description, Status,
                                OwnerName, ClientStory, CreatedBy)
        VALUES (@Name, @customerId, @SeasonCode, @TargetMarketCode, @ProductCategoryCode, @Gender, @TargetPrice, @Description, @Status,
                ISNULL(@OwnerName, @ChangedBy), @ClientStory, @ChangedBy);
        SET @ConceptId = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
        UPDATE concept.Concept
        SET Name = @Name, CustomerId = @customerId, SeasonCode = @SeasonCode, TargetMarketCode = @TargetMarketCode,
            ProductCategoryCode = @ProductCategoryCode, Gender = @Gender, TargetPrice = @TargetPrice, Description = @Description,
            Status = @Status, OwnerName = ISNULL(@OwnerName, OwnerName), ClientStory = @ClientStory, UpdatedBy = @ChangedBy,
            UpdatedUtc = SYSUTCDATETIME()
        WHERE ConceptId = @ConceptId;

    IF @TrendTags IS NOT NULL
    BEGIN
        DELETE t FROM concept.ConceptTag t WHERE t.ConceptId = @ConceptId AND NOT EXISTS (SELECT 1 FROM @tags x WHERE x.Code = t.TrendTagCode);
        INSERT concept.ConceptTag (ConceptId, TrendTagCode)
        SELECT @ConceptId, x.Code FROM @tags x
        WHERE NOT EXISTS (SELECT 1 FROM concept.ConceptTag t WHERE t.ConceptId = @ConceptId AND t.TrendTagCode = x.Code);
    END
    COMMIT TRANSACTION;
    SELECT ConceptId = @ConceptId;
END
GO

/*
    Saves the AI brief (or clears it: @Summary and @BriefJson NULL). Not checked against RowVer (the user asked for it on
    the concept as shown); returns the concept's new RowVer so the page can keep editing.
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_SaveBrief
    @ConceptId int,
    @Summary   nvarchar(4000) = NULL,
    @BriefJson nvarchar(max)  = NULL,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @BriefJson IS NOT NULL AND ISJSON(@BriefJson) = 0 THROW 50400, 'The brief could not be read.', 1;
    UPDATE concept.Concept
    SET BriefSummary = NULLIF(@Summary, N''), BriefJson = @BriefJson,
        BriefBy = IIF(@Summary IS NULL AND @BriefJson IS NULL, NULL, @ChangedBy),
        BriefUtc = IIF(@Summary IS NULL AND @BriefJson IS NULL, NULL, SYSUTCDATETIME())
    WHERE ConceptId = @ConceptId;
    IF @@ROWCOUNT = 0 THROW 50404, 'This concept no longer exists.', 1;
    SELECT RowVer FROM concept.Concept WHERE ConceptId = @ConceptId;
END
GO

/*
    Deletes a concept with its tags, pictures (the rows; the files stay in the image store) and inspiration styles. A concept
    with styles offered in a package is kept: set it to Dropped instead.
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_Delete
    @ConceptId int,
    @RowVer    binary(8),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM concept.Concept WITH (UPDLOCK) WHERE ConceptId = @ConceptId)
        THROW 50404, 'This concept no longer exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM concept.Concept WHERE ConceptId = @ConceptId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this concept after you opened it. Reload and try again.', 1;
    IF EXISTS (SELECT 1 FROM pack.PackageItem WHERE ConceptId = @ConceptId)
        THROW 50409, 'Styles were offered in a package for this concept, so it is kept for the record. Set it to Dropped instead.', 1;
    IF EXISTS (SELECT 1 FROM style.Style WHERE ConceptId = @ConceptId)
        THROW 50409, 'Styles were developed from this concept, so it is kept for the record. Set it to Dropped instead.', 1;
    DELETE concept.Concept WHERE ConceptId = @ConceptId;   -- tags, pictures and inspiration styles go with it (cascade)
    COMMIT TRANSACTION;
END
GO

/* ---------- Develop as style (roadmap 5.21) ---------- */

/*
    Makes an LT original design from the concept: a new style of the concept's customer (the offered product becomes the brand's
    design once adopted), with the season and style number typed by the user, ConceptId set, the concept's name as model name and
    its idea as description, and @SketchUrl (a concept picture already copied into the style image store by the API) as sketch.
    The BOM starts from @BaseStyleId, one of the concept's inspiration styles (NULL = no BOM yet):
      - a style of the customer's own brand: its whole BOM, and its product type, weave, lead time and business unit;
      - another brand's style: know-how only, as a package copy (fabric, trim and accessory lines; product facts), and
        OriginStyleId = that style, so the copy's history and the brand closeness check follow it.
    Pictures, colourways, model code and BOM line pictures are never taken over. Returns StyleId and SameBrandBase
    (NULL without a base).
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_DevelopStyle
    @ConceptId   int,
    @SeasonCode  nvarchar(16),
    @StyleNo     nvarchar(100),
    @BaseStyleId int           = NULL,
    @SketchUrl   nvarchar(500) = NULL,
    @ChangedBy   nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @SeasonCode = UPPER(NULLIF(LTRIM(RTRIM(@SeasonCode)), N''));
    SET @StyleNo = NULLIF(LTRIM(RTRIM(@StyleNo)), N'');
    IF @SeasonCode IS NULL THROW 50400, 'Choose the season of the new style.', 1;
    IF @StyleNo IS NULL THROW 50400, 'Give the new style its own style number.', 1;

    BEGIN TRANSACTION;
    DECLARE @customerId int, @brand nvarchar(32), @name nvarchar(150), @idea nvarchar(2000), @gender nvarchar(16);
    SELECT @customerId = k.CustomerId, @brand = c.BrandCode, @name = k.Name, @idea = k.Description, @gender = k.Gender
    FROM concept.Concept k WITH (UPDLOCK) LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    WHERE k.ConceptId = @ConceptId;
    IF @name IS NULL THROW 50404, 'This concept no longer exists.', 1;
    IF @customerId IS NULL THROW 50409, 'Choose the concept''s customer first (Edit): the new style becomes that customer''s design.', 1;
    IF @BaseStyleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM concept.ConceptStyle WHERE ConceptId = @ConceptId AND StyleId = @BaseStyleId)
        THROW 50400, 'Start the BOM from one of the concept''s inspiration styles.', 1;

    DECLARE @sameBrand bit = (SELECT IIF(s.CustomerId = @customerId OR (@brand IS NOT NULL AND sc.BrandCode = @brand), 1, 0)
                              FROM style.Style s JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId WHERE s.StyleId = @BaseStyleId);
    EXEC style.usp_EnsureSeason @SeasonCode;
    IF EXISTS (SELECT 1 FROM style.Style WHERE CustomerId = @customerId AND SeasonCode = @SeasonCode AND StyleNo = @StyleNo)
        THROW 50409, 'The customer already has a style with this number in this season.', 1;

    INSERT style.Style (CustomerId, SeasonCode, StyleNo, Description, ModelName, WeaveTypeCode, ProductTypeCode, Gender, GarmentLeadTimeDays,
                        BusinessUnitCode, SketchUrl, OriginStyleId, ConceptId, CreatedBy)
    SELECT @customerId, @SeasonCode, @StyleNo, LEFT(ISNULL(@idea, @name), 400), LEFT(@name, 100), b.WeaveTypeCode, b.ProductTypeCode,
           ISNULL(@gender, b.Gender), b.GarmentLeadTimeDays, b.BusinessUnitCode, @SketchUrl, IIF(@sameBrand = 0, @BaseStyleId, NULL), @ConceptId, @ChangedBy
    FROM (SELECT x = 1) one
    LEFT JOIN style.Style b ON b.StyleId = @BaseStyleId;
    DECLARE @newId int = CAST(SCOPE_IDENTITY() AS int);

    INSERT style.BomLine (StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
                          NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ConsumptionAllowance, LcoIsEstimate, CreatedBy)
    SELECT @newId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
           NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ConsumptionAllowance, LcoIsEstimate, @ChangedBy
    FROM style.BomLine
    WHERE StyleId = @BaseStyleId AND (@sameBrand = 1 OR ContentClassCode IN (N'FAB', N'TRI', N'ACC'));
    COMMIT TRANSACTION;

    SELECT StyleId = @newId, SameBrandBase = @sameBrand;
END
GO

/* ---------- Inspiration pictures ---------- */

/*
    Adds a picture (already stored in the concept image store) at the end, with its look-alike vector when the image
    model could read it (@Vector: JSON array of 768 numbers; @VectorError when it could not). At most 24 per concept.
    Returns ConceptImageId.
*/
CREATE OR ALTER PROCEDURE concept.usp_ConceptImage_Add
    @ConceptId   int,
    @ImageUrl    nvarchar(500),
    @Caption     nvarchar(200) = NULL,
    @Model       nvarchar(200) = NULL,
    @Vector      nvarchar(max) = NULL,
    @VectorError nvarchar(300) = NULL,
    @AddedBy     nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM concept.Concept WITH (UPDLOCK) WHERE ConceptId = @ConceptId)
        THROW 50404, 'This concept no longer exists.', 1;
    IF (SELECT COUNT(*) FROM concept.ConceptImage WHERE ConceptId = @ConceptId) >= 24
        THROW 50409, 'A concept holds at most 24 pictures. Remove one first.', 1;
    INSERT concept.ConceptImage (ConceptId, ImageUrl, Caption, SortOrder, Model, Vector, VectorError, AddedBy)
    VALUES (@ConceptId, @ImageUrl, NULLIF(LTRIM(RTRIM(@Caption)), N''),
            (SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM concept.ConceptImage WHERE ConceptId = @ConceptId),
            IIF(@Vector IS NULL AND @VectorError IS NULL, NULL, @Model), IIF(@Vector IS NULL, NULL, CAST(@Vector AS vector(768))),
            LEFT(@VectorError, 300), @AddedBy);
    DECLARE @id int = CAST(SCOPE_IDENTITY() AS int);
    COMMIT TRANSACTION;
    SELECT ConceptImageId = @id;
END
GO

/* Removes a picture from the concept (the file stays in the image store). */
CREATE OR ALTER PROCEDURE concept.usp_ConceptImage_Remove
    @ConceptId      int,
    @ConceptImageId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE concept.ConceptImage WHERE ConceptId = @ConceptId AND ConceptImageId = @ConceptImageId;
    IF @@ROWCOUNT = 0 THROW 50404, 'This picture is no longer on the concept.', 1;
END
GO

/* Shows a picture on the client's story page or not (the API checks the brand closeness first), and its caption. */
CREATE OR ALTER PROCEDURE concept.usp_ConceptImage_Save
    @ConceptId      int,
    @ConceptImageId int,
    @ForClient      bit,
    @Caption        nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE concept.ConceptImage SET ForClient = @ForClient, Caption = NULLIF(LTRIM(RTRIM(@Caption)), N'')
    WHERE ConceptId = @ConceptId AND ConceptImageId = @ConceptImageId;
    IF @@ROWCOUNT = 0 THROW 50404, 'This picture is no longer on the concept.', 1;
END
GO

/* The concept's pictures still to read with @Model (never read, read by another model, or no vector and no error yet). */
CREATE OR ALTER PROCEDURE concept.usp_ConceptImage_ToRead
    @ConceptId int,
    @Model     nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ConceptImageId, ImageUrl
    FROM concept.ConceptImage
    WHERE ConceptId = @ConceptId AND (Model IS NULL OR Model <> @Model OR (Vector IS NULL AND VectorError IS NULL))
    ORDER BY SortOrder, ConceptImageId;
END
GO

/* Stores a picture's look-alike vector (@Vector JSON) or why it could not be read (@Error). */
CREATE OR ALTER PROCEDURE concept.usp_ConceptImage_SetVector
    @ConceptImageId int,
    @Model          nvarchar(200),
    @Vector         nvarchar(max) = NULL,
    @Error          nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE concept.ConceptImage
    SET Model = @Model, Vector = IIF(@Vector IS NULL, NULL, CAST(@Vector AS vector(768))), VectorError = IIF(@Vector IS NULL, LEFT(@Error, 300), NULL)
    WHERE ConceptImageId = @ConceptImageId;
END
GO

/* ---------- Inspiration styles (design provenance, roadmap 5.1) ---------- */

/*
    Records library styles (@StyleIds: JSON array) as inspirations of the concept. One row per style asked for: Result =
    Added, AlreadyIn or NotFound. @AddedFrom: Pick, Match or LookAlike.
*/
CREATE OR ALTER PROCEDURE concept.usp_ConceptStyle_Add
    @ConceptId int,
    @StyleIds  nvarchar(max),
    @AddedFrom varchar(12) = NULL,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@StyleIds) = 0 THROW 50400, 'The styles to add must be a JSON array.', 1;
    IF @AddedFrom IS NOT NULL AND @AddedFrom NOT IN ('Pick', 'Match', 'LookAlike') THROW 50400, 'Unknown place the styles were added from.', 1;

    DECLARE @ask TABLE (Seq int NOT NULL, StyleId int NOT NULL);
    INSERT @ask (Seq, StyleId)
    SELECT MIN(CAST([key] AS int)), TRY_CAST([value] AS int) FROM OPENJSON(@StyleIds)
    WHERE TRY_CAST([value] AS int) IS NOT NULL GROUP BY TRY_CAST([value] AS int);
    IF NOT EXISTS (SELECT 1 FROM @ask) THROW 50400, 'Choose at least one style.', 1;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM concept.Concept WITH (UPDLOCK) WHERE ConceptId = @ConceptId)
        THROW 50404, 'This concept no longer exists.', 1;

    SELECT a.Seq, a.StyleId,
           Result = CASE WHEN s.StyleId IS NULL THEN 'NotFound' WHEN cs.StyleId IS NOT NULL THEN 'AlreadyIn' ELSE 'Added' END
    INTO #r
    FROM @ask a
    LEFT JOIN style.Style s ON s.StyleId = a.StyleId
    LEFT JOIN concept.ConceptStyle cs ON cs.ConceptId = @ConceptId AND cs.StyleId = a.StyleId;

    INSERT concept.ConceptStyle (ConceptId, StyleId, AddedFrom, AddedBy)
    SELECT @ConceptId, StyleId, @AddedFrom, @ChangedBy FROM #r WHERE Result = 'Added';
    COMMIT TRANSACTION;

    SELECT r.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = sc.PartnerCode, r.Result
    FROM #r r
    LEFT JOIN style.Style s ON s.StyleId = r.StyleId
    LEFT JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
    ORDER BY r.Seq;
END
GO

/* What the concept takes from an inspiration style (empty clears the note). */
CREATE OR ALTER PROCEDURE concept.usp_ConceptStyle_Save
    @ConceptId int,
    @StyleId   int,
    @Note      nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE concept.ConceptStyle SET Note = NULLIF(LTRIM(RTRIM(@Note)), N'') WHERE ConceptId = @ConceptId AND StyleId = @StyleId;
    IF @@ROWCOUNT = 0 THROW 50404, 'This style is no longer an inspiration of the concept.', 1;
END
GO

CREATE OR ALTER PROCEDURE concept.usp_ConceptStyle_Remove
    @ConceptId int,
    @StyleId   int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE concept.ConceptStyle WHERE ConceptId = @ConceptId AND StyleId = @StyleId;
END
GO

/* ---------- Proven styles in the library (rules first, AI explanation optional) ---------- */

/*
    What to look for in the library for a concept, without AI (Concepts page > Proven styles in the library):
      1. the product types of the concept's category (style.fn_GarmentCategory of the type's name or code);
      2. the customer codes of the concept customer's brand (the customer itself and every account of its brand);
      3. which of @Words (comma separated, up to 30, in order of preference) appear in a fabric BOM description, so only
         words that can match are used (in the order given).
    The API scores the library with these (style.usp_ConceptMatch_Candidates).
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_MatchContext
    @ConceptId int,
    @Words     nvarchar(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @category nvarchar(20), @customerId int, @brand nvarchar(32);
    SELECT @category = k.ProductCategoryCode, @customerId = k.CustomerId, @brand = c.BrandCode
    FROM concept.Concept k LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId WHERE k.ConceptId = @ConceptId;

    SELECT pt.ProductTypeCode
    FROM ref.ProductType pt
    WHERE @category IS NOT NULL AND pt.IsActive = 1
      AND (style.fn_GarmentCategory(pt.Name) = @category OR style.fn_GarmentCategory(pt.ProductTypeCode) = @category)
    ORDER BY pt.ProductTypeCode;

    SELECT c.PartnerCode
    FROM partner.Partner c
    WHERE c.PartnerType = 'Customer' AND (c.PartnerId = @customerId OR (@brand IS NOT NULL AND c.BrandCode = @brand))
    ORDER BY c.PartnerCode;

    SELECT w.Word
    FROM (SELECT TOP (30) Word = UPPER(LEFT(TRIM(value), 40)), Seq = ordinal
          FROM STRING_SPLIT(@Words, N',', 1) WHERE LEN(TRIM(value)) >= 3 ORDER BY ordinal) w
    WHERE EXISTS (SELECT 1 FROM style.BomLine b
                  WHERE b.ContentClassCode = N'FAB'
                    AND UPPER(b.MaterialDescription) COLLATE Latin1_General_100_BIN2
                        LIKE N'%' + REPLACE(REPLACE(REPLACE(w.Word, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%')
    ORDER BY w.Seq;
END
GO

/* ---------- Brand closeness (roadmap 5.2, ODM pitch guardrail) ---------- */

/*
    The library styles whose pictures look closest to each inspiration picture of the concept (look-alike vectors of
    @Model, the image model built into the API; the same vectors as image search). Per picture with a vector: the best
    @Take styles (best picture per style), with SameBrand = the concept customer's own or its brand's (NULL when the
    concept has no customer: every brand counts as another brand). The API sorts them into close / check by the
    BrandCloseness thresholds; a close match of another brand means the concept looks like that brand's design.
*/
CREATE OR ALTER PROCEDURE concept.usp_Concept_Closeness
    @ConceptId int,
    @Model     nvarchar(200),
    @Take      int = 6
AS
BEGIN
    SET NOCOUNT ON;
    SET @Take = IIF(@Take BETWEEN 1 AND 24, @Take, 6);
    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = k.CustomerId, @brand = c.BrandCode
    FROM concept.Concept k LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId WHERE k.ConceptId = @ConceptId;

    SELECT ConceptImageId, Vector INTO #mine
    FROM concept.ConceptImage
    WHERE ConceptId = @ConceptId AND Model = @Model AND Vector IS NOT NULL;

    WITH scored AS (
        SELECT m.ConceptImageId, v.StyleId, v.Source, v.ImageUrl, Distance = VECTOR_DISTANCE('cosine', m.Vector, v.Vector)
        FROM #mine m
        CROSS JOIN style.ImageVector v
        JOIN style.fn_ImageSlots() p ON p.StyleId = v.StyleId AND p.Source = v.Source AND p.ItemId = v.ItemId AND p.ImageUrl = v.ImageUrl
        WHERE v.Model = @Model AND v.Vector IS NOT NULL),
    perStyle AS (
        SELECT *, Rs = ROW_NUMBER() OVER (PARTITION BY ConceptImageId, StyleId ORDER BY Distance, ImageUrl) FROM scored),
    ranked AS (
        SELECT *, Rn = ROW_NUMBER() OVER (PARTITION BY ConceptImageId ORDER BY Distance, StyleId) FROM perStyle WHERE Rs = 1)
    SELECT x.ConceptImageId, x.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = sc.PartnerCode, sc.BrandCode, s.ModelName,
           ProductTypeName = pt.Name, x.Source, x.ImageUrl,
           SameBrand = CAST(CASE WHEN @customerId IS NULL THEN NULL
                                 WHEN s.CustomerId = @customerId OR (@brand IS NOT NULL AND sc.BrandCode = @brand) THEN 1 ELSE 0 END AS bit),
           Similarity = CAST(1 - x.Distance AS decimal(9, 4))
    FROM ranked x
    JOIN style.Style s ON s.StyleId = x.StyleId
    JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE x.Rn <= @Take
    ORDER BY x.ConceptImageId, x.Rn;
END
GO
