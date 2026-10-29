/*
    LT ODM Style Packages - library styles offered to a client (roadmap 5.8; tables in tables\pack.tables.sql)

    Reads:  usp_Package_List, usp_Package_Get, usp_Package_ForStyle, usp_Offer_Suggest (What to offer)
    Writes: usp_Package_Save / _Delete, usp_PackageItem_Add / _AddCopy / _Save / _Remove / _Reorder
    Share links (no sign-in for the client): usp_Share_Create / _List / _Token / _Revoke / _Open / _Feedback / _ImageAllowed
    Hit rate: usp_PackageItem_SetOutcome, usp_HitRate
    Brand closeness (roadmap 5.2): usp_Package_Closeness
    Client profiles (roadmap 5.13): usp_ClientProfile_List / _Get / _Save / _Check (fn_ClientKey)
    Follow-up reminders (roadmap 5.24): usp_FollowUp_List / _Dismiss
    Notices (the bell): usp_Notice_List / _Read; usp_Share_Feedback makes them

    Same rules as the Styles procedures: writes to an existing row take its RowVer and refuse with 50409 if someone
    else saved it first; rule errors are THROWn as 50400, 50404 or 50409 with a message for the user; @ChangedBy is the
    signed-in user (from the API). A customer code typed by the user that is new is added to the customer list.
    Item changes do not change the package's RowVer, so a header edit and item edits made side by side do not clash.

    Brand rule (decision 4, ODM pitch guardrail): only styles of the package's brand are added directly - the same
    customer, or a customer with the same brand (partner.Partner.BrandCode). Another brand's style is offered as a copy
    with its own style number (roadmap 5.12); usp_PackageItem_Add reports it as OtherBrand and leaves it out.

    Requires tables\style.tables.sql, tables\concept.tables.sql, tables\pack.tables.sql and procedures\style.procedures.sql
    (usp_EnsureSeason).
    Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\pack.procedures.sql
*/
SET NOCOUNT ON;
GO

/* ---------- Reads ---------- */

/*
    Packages for the list, newest change first. @Mine = 1 keeps the packages owned by @UserName. LastChangedUtc is the
    latest change to the package or its items. CoverUrl is the first item's picture (or sketch) with one.
*/
CREATE OR ALTER PROCEDURE pack.usp_Package_List
    @Search       nvarchar(100) = NULL,
    @CustomerCode nvarchar(32)  = NULL,
    @SeasonCode   nvarchar(16)  = NULL,
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

    SELECT p.PackageId
    INTO #p
    FROM pack.Package p
    JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    WHERE (@CustomerCode IS NULL OR c.PartnerCode = @CustomerCode)
      AND (@SeasonCode IS NULL OR p.SeasonCode = @SeasonCode)
      AND (@Status IS NULL OR p.Status = @Status)
      AND (@Mine = 0 OR p.OwnerName = @UserName)
      AND (@Search IS NULL OR p.Name LIKE N'%' + @Search + N'%' OR p.Description LIKE N'%' + @Search + N'%'
           OR c.PartnerCode = @Search OR c.Name LIKE N'%' + @Search + N'%'
           OR EXISTS (SELECT 1 FROM pack.PackageItem i JOIN style.Style s ON s.StyleId = i.StyleId
                      WHERE i.PackageId = p.PackageId AND (s.StyleNo LIKE @Search + N'%' OR s.ModelName LIKE N'%' + @Search + N'%')));

    SELECT Total = COUNT(*) FROM #p;

    SELECT p.PackageId, p.Name, CustomerCode = c.PartnerCode, CustomerName = c.Name, p.SeasonCode, p.Status, p.OwnerName,
           p.SentUtc, Items = ISNULL(n.Items, 0), CoverUrl = cv.Url,
           LastChangedUtc = (SELECT MAX(v) FROM (VALUES (p.CreatedUtc), (p.UpdatedUtc), (n.LastItemUtc)) x (v))
    FROM #p f
    JOIN pack.Package p ON p.PackageId = f.PackageId
    JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    OUTER APPLY (SELECT Items = COUNT(*), LastItemUtc = MAX(ISNULL(i.UpdatedUtc, i.CreatedUtc))
                 FROM pack.PackageItem i WHERE i.PackageId = p.PackageId) n
    OUTER APPLY (SELECT TOP (1) Url = ISNULL(s.ImageUrl, s.SketchUrl)
                 FROM pack.PackageItem i JOIN style.Style s ON s.StyleId = i.StyleId
                 WHERE i.PackageId = p.PackageId AND (s.ImageUrl IS NOT NULL OR s.SketchUrl IS NOT NULL)
                 ORDER BY i.SortOrder, i.PackageItemId) cv
    ORDER BY LastChangedUtc DESC, p.PackageId DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

/*
    A package with its items (style details in package order) and, per item, every colourway of the style with
    Offered = 1 when it is in the offer, plus chosen codes that are no longer on the style (Missing = 1).
    OtherBrand = 1 marks an item whose style is no longer of the package's brand (the package's customer was changed
    after it was added); such items are to be replaced by a copy (roadmap 5.12). ConceptId / ConceptName: the concept the
    style was offered for (roadmap 5.7); OriginStyleId: the style it was copied from (5.12); StyleConceptId: set when the style
    is an LT design developed from a concept (5.21).
    FabricLines / RecycledFabricLines count the style's fabric BOM lines and those written as recycled (mat.fn_IsRecycled).
    The fourth result set is each item's key materials (cards, line sheet): up to 3 fabrics (largest consumption first) and
    up to 4 trims (in BOM order), one row per material, with the fibre composition when the description reader has read
    it (mat.MaterialSpec) and Recycled from the description. The last is the clients' answers per item through share
    links (Like / Pass, comment, the name they typed, the link's label, sample request). Then the concept stories for the client
    (roadmap 5.22) and their pictures.
*/
CREATE OR ALTER PROCEDURE pack.usp_Package_Get
    @PackageId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PackageId, p.Name, CustomerCode = c.PartnerCode, CustomerName = c.Name, BrandCode = c.BrandCode, p.SeasonCode,
           p.Description, p.Status, p.OwnerName, p.SentUtc, p.CreatedBy, p.CreatedUtc, p.UpdatedBy, p.UpdatedUtc, p.RowVer
    FROM pack.Package p
    JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    WHERE p.PackageId = @PackageId;

    SELECT i.PackageItemId, i.StyleId, i.SortOrder, i.PitchNote, i.InternalNote, i.AllColorways, i.AddedFrom,
           s.StyleNo, s.SeasonCode, CustomerCode = sc.PartnerCode, s.Description, s.ModelName, s.ProductTypeCode,
           ProductTypeName = pt.Name, s.Gender, s.WeaveTypeCode, WeaveTypeName = w.Name, s.GarmentLeadTimeDays,
           s.ImageUrl, s.SketchUrl, s.IsActive,
           OtherBrand = CAST(IIF(s.CustomerId = p.CustomerId OR (pc.BrandCode IS NOT NULL AND sc.BrandCode = pc.BrandCode), 0, 1) AS bit),
           FabricLines = ISNULL(f.Lines, 0), RecycledFabricLines = ISNULL(f.Recycled, 0),
           i.CreatedBy, i.CreatedUtc, i.UpdatedBy, i.UpdatedUtc, i.RowVer, i.Outcome, i.OutcomeBy, i.OutcomeUtc,
           i.ConceptId, ConceptName = cn.Name, s.OriginStyleId, StyleConceptId = s.ConceptId
    FROM pack.PackageItem i
    LEFT JOIN concept.Concept cn ON cn.ConceptId = i.ConceptId
    JOIN pack.Package p ON p.PackageId = i.PackageId
    JOIN partner.Partner pc ON pc.PartnerId = p.CustomerId
    JOIN style.Style s ON s.StyleId = i.StyleId
    JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    LEFT JOIN ref.WeaveType w ON w.WeaveTypeCode = s.WeaveTypeCode
    OUTER APPLY (SELECT Lines = COUNT(*), Recycled = SUM(CAST(mat.fn_IsRecycled(b.MaterialDescription) AS int))
                 FROM style.BomLine b WHERE b.StyleId = s.StyleId AND b.ContentClassCode = N'FAB') f
    WHERE i.PackageId = @PackageId
    ORDER BY i.SortOrder, i.PackageItemId;

    SELECT i.PackageItemId, k.ColorwayCode, k.ColorwayName, k.Status, k.ImageUrl, k.SortOrder,
           Offered = CAST(IIF(i.AllColorways = 1 OR pc.ColorwayCode IS NOT NULL, 1, 0) AS bit), Missing = CAST(0 AS bit)
    FROM pack.PackageItem i
    JOIN style.Colorway k ON k.StyleId = i.StyleId
    LEFT JOIN pack.PackageItemColorway pc ON pc.PackageItemId = i.PackageItemId AND pc.ColorwayCode = k.ColorwayCode
    WHERE i.PackageId = @PackageId
    UNION ALL
    SELECT pc.PackageItemId, pc.ColorwayCode, NULL, NULL, NULL, 32767, CAST(1 AS bit), CAST(1 AS bit)
    FROM pack.PackageItemColorway pc
    JOIN pack.PackageItem i ON i.PackageItemId = pc.PackageItemId
    WHERE i.PackageId = @PackageId
      AND NOT EXISTS (SELECT 1 FROM style.Colorway k WHERE k.StyleId = i.StyleId AND k.ColorwayCode = pc.ColorwayCode)
    ORDER BY PackageItemId, SortOrder, ColorwayCode;

    WITH m AS (
        SELECT i.PackageItemId, Kind = IIF(b.ContentClassCode = N'FAB', 'Fabric', 'Trim'), mm.MaterialCode,
               Description = ISNULL(b.MaterialDescription, mm.Description), sp.Composition,
               Recycled = CAST(mat.fn_IsRecycled(b.MaterialDescription) AS bit),
               -- one row per material; fabrics by consumption, trims in BOM order
               Dup = ROW_NUMBER() OVER (PARTITION BY i.PackageItemId, b.MaterialId ORDER BY ISNULL(b.LcoConsumption, b.BrandConsumption) DESC, b.LineSeq),
               Rnk = ROW_NUMBER() OVER (PARTITION BY i.PackageItemId, b.ContentClassCode
                                        ORDER BY IIF(b.ContentClassCode = N'FAB', -ISNULL(ISNULL(b.LcoConsumption, b.BrandConsumption), 0), 0), b.LineSeq)
        FROM pack.PackageItem i
        JOIN style.BomLine b ON b.StyleId = i.StyleId AND b.ContentClassCode IN (N'FAB', N'TRI')
        JOIN mat.Material mm ON mm.MaterialId = b.MaterialId
        LEFT JOIN mat.MaterialSpec sp ON sp.MaterialId = b.MaterialId
        WHERE i.PackageId = @PackageId)
    SELECT PackageItemId, Kind, MaterialCode, Description, Composition, Recycled,
           Rank = ROW_NUMBER() OVER (PARTITION BY PackageItemId, Kind ORDER BY Rnk)
    INTO #m
    FROM m WHERE Dup = 1;
    SELECT PackageItemId, Kind, MaterialCode, Description, Composition, Recycled
    FROM #m
    WHERE (Kind = 'Fabric' AND Rank <= 3) OR (Kind = 'Trim' AND Rank <= 4)
    ORDER BY PackageItemId, IIF(Kind = 'Fabric', 0, 1), Rank;

    SELECT f.PackageItemId, f.ShareId, sh.Label, f.Verdict, f.Comment, f.Name, f.UpdatedUtc, f.Sample, f.SampleNote
    FROM pack.ShareFeedback f
    JOIN pack.PackageShare sh ON sh.ShareId = f.ShareId
    WHERE sh.PackageId = @PackageId
    ORDER BY f.PackageItemId, f.UpdatedUtc DESC, f.VoterKey;

    -- Concept stories (roadmap 5.22): concepts of the package's styles (offered for a concept, or LT designs from one) with a
    -- story for the client, in the order of their first style; then their pictures shown to the client (up to 4 each).
    SELECT i.PackageItemId, ConceptId = ISNULL(i.ConceptId, s.ConceptId), i.SortOrder
    INTO #sc
    FROM pack.PackageItem i JOIN style.Style s ON s.StyleId = i.StyleId
    WHERE i.PackageId = @PackageId AND ISNULL(i.ConceptId, s.ConceptId) IS NOT NULL;

    SELECT k.ConceptId, k.Name, k.ClientStory,
           Tags = (SELECT STRING_AGG(r.Name, N' · ') WITHIN GROUP (ORDER BY r.Name)
                   FROM concept.ConceptTag t JOIN ref.TrendTag r ON r.TrendTagCode = t.TrendTagCode WHERE t.ConceptId = k.ConceptId),
           PackageItemIds = (SELECT STRING_AGG(CAST(x.PackageItemId AS varchar(12)), ',') WITHIN GROUP (ORDER BY x.SortOrder, x.PackageItemId)
                             FROM #sc x WHERE x.ConceptId = k.ConceptId)
    FROM concept.Concept k
    WHERE k.ClientStory IS NOT NULL AND k.ConceptId IN (SELECT ConceptId FROM #sc)
    ORDER BY (SELECT MIN(x.SortOrder) FROM #sc x WHERE x.ConceptId = k.ConceptId), k.ConceptId;

    SELECT ConceptId, ImageUrl, Caption
    FROM (SELECT ci.ConceptId, ci.ImageUrl, ci.Caption, Rn = ROW_NUMBER() OVER (PARTITION BY ci.ConceptId ORDER BY ci.SortOrder, ci.ConceptImageId)
          FROM concept.ConceptImage ci
          JOIN concept.Concept k ON k.ConceptId = ci.ConceptId
          WHERE ci.ForClient = 1 AND k.ClientStory IS NOT NULL AND ci.ConceptId IN (SELECT ConceptId FROM #sc)) x
    WHERE Rn <= 4
    ORDER BY ConceptId, Rn;
END
GO

/* Packages a style is in (style page). */
CREATE OR ALTER PROCEDURE pack.usp_Package_ForStyle
    @StyleId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.PackageId, p.Name, CustomerCode = c.PartnerCode, p.SeasonCode, p.Status, p.OwnerName, i.PackageItemId
    FROM pack.PackageItem i
    JOIN pack.Package p ON p.PackageId = i.PackageId
    JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    WHERE i.StyleId = @StyleId
    ORDER BY ISNULL(p.UpdatedUtc, p.CreatedUtc) DESC;
END
GO

/* ---------- Packages ---------- */

/*
    Creates (@PackageId NULL) or updates a package's header. Returns PackageId. @OwnerName NULL keeps the owner (the
    creator for a new package). The customer cannot change to another brand while the package holds styles: they
    would become another brand's styles.
*/
CREATE OR ALTER PROCEDURE pack.usp_Package_Save
    @PackageId    int            = NULL,
    @RowVer       binary(8)      = NULL,
    @Name         nvarchar(150),
    @CustomerCode nvarchar(32),
    @SeasonCode   nvarchar(16)   = NULL,
    @Description  nvarchar(2000) = NULL,
    @Status       varchar(10)    = 'Draft',
    @OwnerName    nvarchar(64)   = NULL,
    @ChangedBy    nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Name = NULLIF(LTRIM(RTRIM(@Name)), N'');
    SET @CustomerCode = NULLIF(LTRIM(RTRIM(@CustomerCode)), N'');
    SET @SeasonCode = NULLIF(LTRIM(RTRIM(@SeasonCode)), N'');
    SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');
    SET @OwnerName = NULLIF(LTRIM(RTRIM(@OwnerName)), N'');

    IF @Name IS NULL THROW 50400, 'Give the package a name.', 1;
    IF @CustomerCode IS NULL THROW 50400, 'Choose the customer the package is for.', 1;
    IF @Status IS NULL OR @Status NOT IN ('Draft', 'Sent', 'Viewed', 'Sampling', 'Ordered', 'Closed')
        THROW 50400, 'Choose a status from the list.', 1;

    BEGIN TRANSACTION;
    IF @PackageId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pack.Package WITH (UPDLOCK) WHERE PackageId = @PackageId)
        THROW 50404, 'This package no longer exists.', 1;
    IF @PackageId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pack.Package WHERE PackageId = @PackageId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this package after you opened it. Reload and try again.', 1;

    IF @SeasonCode IS NOT NULL EXEC style.usp_EnsureSeason @SeasonCode;
    IF NOT EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode)
        INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy) VALUES ('Customer', @CustomerCode, @CustomerCode, @ChangedBy);
    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = PartnerId, @brand = BrandCode FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode;

    IF @PackageId IS NULL
    BEGIN
        INSERT pack.Package (Name, CustomerId, SeasonCode, Description, Status, OwnerName, SentUtc, CreatedBy)
        VALUES (@Name, @customerId, @SeasonCode, @Description, @Status, ISNULL(@OwnerName, @ChangedBy),
                IIF(@Status = 'Draft', NULL, SYSUTCDATETIME()), @ChangedBy);
        SET @PackageId = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM pack.PackageItem i
                   JOIN style.Style s ON s.StyleId = i.StyleId
                   JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
                   WHERE i.PackageId = @PackageId
                     AND s.CustomerId <> @customerId AND (@brand IS NULL OR ISNULL(sc.BrandCode, N'') <> @brand))
            THROW 50409, 'This package holds styles that are not of the new customer''s brand. Remove them first, or start a new package for the other customer.', 1;

        UPDATE pack.Package
        SET Name = @Name, CustomerId = @customerId, SeasonCode = @SeasonCode, Description = @Description, Status = @Status,
            OwnerName = ISNULL(@OwnerName, OwnerName),
            SentUtc = IIF(SentUtc IS NULL AND @Status <> 'Draft', SYSUTCDATETIME(), SentUtc),
            UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        WHERE PackageId = @PackageId;
    END
    COMMIT TRANSACTION;
    SELECT PackageId = @PackageId;
END
GO

/* Deletes a draft package with its items. A package that was sent is kept for the hit rate: close it instead. */
CREATE OR ALTER PROCEDURE pack.usp_Package_Delete
    @PackageId int,
    @RowVer    binary(8),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM pack.Package WITH (UPDLOCK) WHERE PackageId = @PackageId)
        THROW 50404, 'This package no longer exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM pack.Package WHERE PackageId = @PackageId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this package after you opened it. Reload and try again.', 1;
    IF EXISTS (SELECT 1 FROM pack.Package WHERE PackageId = @PackageId AND (Status <> 'Draft' OR SentUtc IS NOT NULL))
        THROW 50409, 'Only a draft package that was never sent can be deleted. Set its status to Closed instead.', 1;

    DELETE pack.PackageNotice WHERE PackageId = @PackageId;
    DELETE f FROM pack.ShareFeedback f JOIN pack.PackageShare sh ON sh.ShareId = f.ShareId WHERE sh.PackageId = @PackageId;
    DELETE a FROM pack.ShareAccess a JOIN pack.PackageShare sh ON sh.ShareId = a.ShareId WHERE sh.PackageId = @PackageId;
    DELETE pack.PackageShare WHERE PackageId = @PackageId;
    DELETE pc FROM pack.PackageItemColorway pc JOIN pack.PackageItem i ON i.PackageItemId = pc.PackageItemId WHERE i.PackageId = @PackageId;
    DELETE pack.PackageItem WHERE PackageId = @PackageId;
    DELETE pack.Package WHERE PackageId = @PackageId;
    COMMIT TRANSACTION;
END
GO

/* ---------- Items ---------- */

/*
    Adds styles (@StyleIds: JSON array of StyleId) to the end of a package, in the order given. Returns one row per
    style asked for: Result = Added, AlreadyIn, OtherBrand (another brand's style: offer it as a copy, roadmap 5.12)
    or NotFound, with PackageItemId for Added and AlreadyIn. @ConceptId: the concept the styles are offered for (added
    styles keep it; AddedFrom is then Concept).
*/
CREATE OR ALTER PROCEDURE pack.usp_PackageItem_Add
    @PackageId int,
    @StyleIds  nvarchar(max),
    @AddedFrom varchar(12) = NULL,
    @ConceptId int         = NULL,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF ISJSON(@StyleIds) = 0 THROW 50400, 'The styles to add must be a JSON array.', 1;
    IF @AddedFrom IS NOT NULL AND @AddedFrom NOT IN ('Styles', 'StylePage', 'Similar', 'ImageSearch', 'Assistant', 'Concept', 'Copy', 'Offer')
        THROW 50400, 'Unknown place the styles were added from.', 1;

    DECLARE @ask TABLE (Seq int NOT NULL, StyleId int NOT NULL);
    INSERT @ask (Seq, StyleId)
    SELECT MIN(CAST([key] AS int)), TRY_CAST([value] AS int) FROM OPENJSON(@StyleIds)
    WHERE TRY_CAST([value] AS int) IS NOT NULL GROUP BY TRY_CAST([value] AS int);
    IF NOT EXISTS (SELECT 1 FROM @ask) THROW 50400, 'Choose at least one style to add.', 1;
    IF @ConceptId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM concept.Concept WHERE ConceptId = @ConceptId)
        THROW 50404, 'This concept no longer exists.', 1;
    IF @ConceptId IS NOT NULL SET @AddedFrom = 'Concept';

    BEGIN TRANSACTION;
    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = p.CustomerId, @brand = c.BrandCode
    FROM pack.Package p WITH (UPDLOCK) JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    WHERE p.PackageId = @PackageId;
    IF @customerId IS NULL THROW 50404, 'This package no longer exists.', 1;

    SELECT a.Seq, a.StyleId,
           Result = CASE WHEN s.StyleId IS NULL THEN 'NotFound'
                         WHEN i.PackageItemId IS NOT NULL THEN 'AlreadyIn'
                         WHEN s.CustomerId = @customerId OR (@brand IS NOT NULL AND sc.BrandCode = @brand) THEN 'Added'
                         ELSE 'OtherBrand' END,
           i.PackageItemId
    INTO #r
    FROM @ask a
    LEFT JOIN style.Style s ON s.StyleId = a.StyleId
    LEFT JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
    LEFT JOIN pack.PackageItem i ON i.PackageId = @PackageId AND i.StyleId = a.StyleId;

    DECLARE @next int = (SELECT ISNULL(MAX(SortOrder), 0) FROM pack.PackageItem WHERE PackageId = @PackageId);
    INSERT pack.PackageItem (PackageId, StyleId, SortOrder, AddedFrom, ConceptId, CreatedBy)
    SELECT @PackageId, StyleId, @next + ROW_NUMBER() OVER (ORDER BY Seq), @AddedFrom, @ConceptId, @ChangedBy
    FROM #r WHERE Result = 'Added';

    UPDATE r SET PackageItemId = i.PackageItemId
    FROM #r r JOIN pack.PackageItem i ON i.PackageId = @PackageId AND i.StyleId = r.StyleId
    WHERE r.Result = 'Added';
    COMMIT TRANSACTION;

    SELECT r.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = sc.PartnerCode, r.Result, r.PackageItemId
    FROM #r r
    LEFT JOIN style.Style s ON s.StyleId = r.StyleId
    LEFT JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
    ORDER BY r.Seq;
END
GO

/*
    Offers a style of another customer (usually another brand) as a new style of the package's customer and adds it to the
    end of the package (roadmap 5.12). The copy gets the season and style number typed by the user and
    style.Style.OriginStyleId = @StyleId, exactly the style picked (a suffixed version stays the reference; earlier
    versions are followed through that style's own history).
    Only know-how goes across: description, product type, weave, gender, lead time, business unit and the fabric, trim and
    accessory BOM lines (materials, consumption, suppliers). The brand's identity stays behind: pictures, colourways (their
    codes and names are the brand's), model code and name, labels / packaging / artwork lines and BOM line pictures.
    A style of the package's own customer is reused with Reuse on its page instead. Returns StyleId and PackageItemId.
    @ConceptId: the concept the copy is offered for (the item keeps it; AddedFrom stays Copy).
*/
CREATE OR ALTER PROCEDURE pack.usp_PackageItem_AddCopy
    @PackageId  int,
    @StyleId    int,
    @SeasonCode nvarchar(16),
    @StyleNo    nvarchar(100),
    @ConceptId  int          = NULL,
    @ChangedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @SeasonCode = UPPER(NULLIF(LTRIM(RTRIM(@SeasonCode)), N''));
    SET @StyleNo = NULLIF(LTRIM(RTRIM(@StyleNo)), N'');
    IF @SeasonCode IS NULL THROW 50400, 'Choose the season of the new style.', 1;
    IF @StyleNo IS NULL THROW 50400, 'Give the new style its own style number.', 1;
    IF @ConceptId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM concept.Concept WHERE ConceptId = @ConceptId)
        THROW 50404, 'This concept no longer exists.', 1;

    BEGIN TRANSACTION;
    DECLARE @customerId int = (SELECT CustomerId FROM pack.Package WITH (UPDLOCK) WHERE PackageId = @PackageId);
    IF @customerId IS NULL THROW 50404, 'This package no longer exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM style.Style WHERE StyleId = @StyleId) THROW 50404, 'The style to copy no longer exists.', 1;
    IF EXISTS (SELECT 1 FROM style.Style WHERE StyleId = @StyleId AND CustomerId = @customerId)
        THROW 50409, 'This style is already the package customer''s: add it as it is, or use Reuse on its page for a new version.', 1;
    EXEC style.usp_EnsureSeason @SeasonCode;
    IF EXISTS (SELECT 1 FROM style.Style WHERE CustomerId = @customerId AND SeasonCode = @SeasonCode AND StyleNo = @StyleNo)
        THROW 50409, 'The package customer already has a style with this number in this season.', 1;

    INSERT style.Style (CustomerId, SeasonCode, StyleNo, Description, WeaveTypeCode, ProductTypeCode, Gender, GarmentLeadTimeDays,
                        BusinessUnitCode, OriginStyleId, CreatedBy)
    SELECT @customerId, @SeasonCode, @StyleNo, Description, WeaveTypeCode, ProductTypeCode, Gender, GarmentLeadTimeDays,
           BusinessUnitCode, @StyleId, @ChangedBy
    FROM style.Style WHERE StyleId = @StyleId;
    DECLARE @newId int = CAST(SCOPE_IDENTITY() AS int);

    INSERT style.BomLine (StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
                          NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ConsumptionAllowance, LcoIsEstimate, CreatedBy)
    SELECT @newId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
           NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ConsumptionAllowance, LcoIsEstimate, @ChangedBy
    FROM style.BomLine
    WHERE StyleId = @StyleId AND ContentClassCode IN (N'FAB', N'TRI', N'ACC');

    INSERT pack.PackageItem (PackageId, StyleId, SortOrder, AddedFrom, ConceptId, CreatedBy)
    VALUES (@PackageId, @newId, (SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM pack.PackageItem WHERE PackageId = @PackageId), 'Copy', @ConceptId,
            @ChangedBy);
    DECLARE @itemId int = CAST(SCOPE_IDENTITY() AS int);
    COMMIT TRANSACTION;

    SELECT StyleId = @newId, PackageItemId = @itemId;
END
GO

/*
    Saves an item's notes and offered colourways. @AllColorways = 1 offers every colourway; 0 offers the codes in
    @ColorwayCodes (JSON array), which must be on the style unless they were already chosen (a chosen code that is no
    longer on the style may stay until it is unticked).
*/
CREATE OR ALTER PROCEDURE pack.usp_PackageItem_Save
    @PackageId     int,
    @PackageItemId int,
    @RowVer        binary(8),
    @PitchNote     nvarchar(1000) = NULL,
    @InternalNote  nvarchar(1000) = NULL,
    @AllColorways  bit            = 1,
    @ColorwayCodes nvarchar(max)  = N'[]',
    @ChangedBy     nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @PitchNote = NULLIF(LTRIM(RTRIM(@PitchNote)), N'');
    SET @InternalNote = NULLIF(LTRIM(RTRIM(@InternalNote)), N'');
    SET @ColorwayCodes = ISNULL(@ColorwayCodes, N'[]');
    IF ISJSON(@ColorwayCodes) = 0 THROW 50400, 'The colourways must be a JSON array.', 1;

    DECLARE @codes TABLE (ColorwayCode nvarchar(100) NOT NULL PRIMARY KEY);
    IF @AllColorways = 0
        INSERT @codes SELECT DISTINCT LTRIM(RTRIM(CAST([value] AS nvarchar(100)))) FROM OPENJSON(@ColorwayCodes)
        WHERE NULLIF(LTRIM(RTRIM(CAST([value] AS nvarchar(100)))), N'') IS NOT NULL;

    BEGIN TRANSACTION;
    DECLARE @styleId int = (SELECT StyleId FROM pack.PackageItem WITH (UPDLOCK) WHERE PackageItemId = @PackageItemId AND PackageId = @PackageId);
    IF @styleId IS NULL THROW 50404, 'This style is no longer in the package.', 1;
    IF NOT EXISTS (SELECT 1 FROM pack.PackageItem WHERE PackageItemId = @PackageItemId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this style in the package after you opened it. Reload and try again.', 1;
    IF EXISTS (SELECT 1 FROM @codes c
               WHERE NOT EXISTS (SELECT 1 FROM style.Colorway k WHERE k.StyleId = @styleId AND k.ColorwayCode = c.ColorwayCode)
                 AND NOT EXISTS (SELECT 1 FROM pack.PackageItemColorway pc WHERE pc.PackageItemId = @PackageItemId AND pc.ColorwayCode = c.ColorwayCode))
        THROW 50400, 'A chosen colourway is not on this style. Reload and try again.', 1;
    IF @AllColorways = 0 AND NOT EXISTS (SELECT 1 FROM @codes)
        THROW 50400, 'Choose at least one colourway, or offer all colourways.', 1;

    DELETE pack.PackageItemColorway WHERE PackageItemId = @PackageItemId;
    INSERT pack.PackageItemColorway (PackageItemId, ColorwayCode) SELECT @PackageItemId, ColorwayCode FROM @codes;

    UPDATE pack.PackageItem
    SET PitchNote = @PitchNote, InternalNote = @InternalNote, AllColorways = @AllColorways,
        UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    WHERE PackageItemId = @PackageItemId;
    COMMIT TRANSACTION;
END
GO

/* Removes items (@PackageItemIds: JSON array) from a package. Items already gone are skipped. */
CREATE OR ALTER PROCEDURE pack.usp_PackageItem_Remove
    @PackageId      int,
    @PackageItemIds nvarchar(max),
    @ChangedBy      nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@PackageItemIds) = 0 THROW 50400, 'The styles to remove must be a JSON array.', 1;

    DECLARE @ids TABLE (PackageItemId int NOT NULL PRIMARY KEY);
    INSERT @ids SELECT DISTINCT TRY_CAST([value] AS int) FROM OPENJSON(@PackageItemIds) WHERE TRY_CAST([value] AS int) IS NOT NULL;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM pack.Package WITH (UPDLOCK) WHERE PackageId = @PackageId)
        THROW 50404, 'This package no longer exists.', 1;
    DELETE pc FROM pack.PackageItemColorway pc
    JOIN pack.PackageItem i ON i.PackageItemId = pc.PackageItemId
    JOIN @ids x ON x.PackageItemId = i.PackageItemId
    WHERE i.PackageId = @PackageId;
    DELETE f FROM pack.ShareFeedback f
    JOIN pack.PackageItem i ON i.PackageItemId = f.PackageItemId
    JOIN @ids x ON x.PackageItemId = i.PackageItemId
    WHERE i.PackageId = @PackageId;
    DELETE i FROM pack.PackageItem i JOIN @ids x ON x.PackageItemId = i.PackageItemId WHERE i.PackageId = @PackageId;
    COMMIT TRANSACTION;
END
GO

/*
    Puts a package's items in the order given (@PackageItemIds: JSON array). Items left out keep their relative
    order after the listed ones, so a list loaded before someone else added a style does not lose it.
*/
CREATE OR ALTER PROCEDURE pack.usp_PackageItem_Reorder
    @PackageId      int,
    @PackageItemIds nvarchar(max),
    @ChangedBy      nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@PackageItemIds) = 0 THROW 50400, 'The order must be a JSON array.', 1;

    DECLARE @order TABLE (PackageItemId int NOT NULL PRIMARY KEY, Seq int NOT NULL);
    INSERT @order
    SELECT TRY_CAST([value] AS int), MIN(CAST([key] AS int)) FROM OPENJSON(@PackageItemIds)
    WHERE TRY_CAST([value] AS int) IS NOT NULL GROUP BY TRY_CAST([value] AS int);

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM pack.Package WITH (UPDLOCK) WHERE PackageId = @PackageId)
        THROW 50404, 'This package no longer exists.', 1;

    WITH n AS (
        SELECT i.SortOrder,
               NewOrder = ROW_NUMBER() OVER (ORDER BY IIF(o.Seq IS NULL, 1, 0), o.Seq, i.SortOrder, i.PackageItemId)
        FROM pack.PackageItem i LEFT JOIN @order o ON o.PackageItemId = i.PackageItemId
        WHERE i.PackageId = @PackageId)
    UPDATE n SET SortOrder = NewOrder WHERE SortOrder <> NewOrder;
    COMMIT TRANSACTION;
END
GO

/* ---------- What to offer (roadmap 5.14) ---------- */

/*
    What to offer a client, from the library alone (rules, no AI). The client is a customer and every customer of its brand.
    Garments are read once per product type (style.fn_GarmentCategory on the type's name), and from the description only for
    styles without a product type; seasons are ordered by year and term (a season without a year counts as oldest).
    Result sets:
      1. Profile: brand, accounts, styles, first and latest season (one row; no rows when the customer has no styles).
      2. Their garments: styles per garment category, most first, with the latest season.
      3. Suggestions, @Top per section, best first. Section:
           Repitch - the brand's own active styles not carried over since (no later version linked to them), from earlier
                     seasons than its latest, in the garments it buys; with a picture, more colourways and a BOM first.
                     Detail1 = colourways, Detail2 = seasons since.
           Gap     - garments the brand has (almost) never bought (under 3% of its styles) that LT made at least 10 times for
                     other brands: up to 3 examples per garment from other brands, offered as copies. Detail1 = the brand's
                     styles of that garment, Detail2 = LT's.
           Fabric  - other brands' styles built on at least 2 fabrics this brand already buys, offered as copies.
                     Detail1 = shared fabrics.
         Styles already in @PackageId are left out; a style is suggested once (first section wins). Variants (another
         version in the same season, StyleHistory Relation Variant) are left out, and each section keeps one style per
         customer and base style number (S2508MR1212_SS27 and S2508MR1212_FW26 are one family), so a family shows once.
    Learning from past packages (hit rate, roadmap 5.17): for the brand's packages sent so far, per garment, Offered and Won
    (outcome Sampling or Ordered), Liked (most votes likes) and Lost (Dropped, or only passes). A garment's results move its styles up
    or down (Won counts double; at most 400 points either way). Styles this brand passed on or dropped are not suggested
    again, nor another brand's style already copied for it. Garments and suggestions carry the garment's Offered / Won.
*/
CREATE OR ALTER PROCEDURE pack.usp_Offer_Suggest
    @CustomerCode nvarchar(32),
    @PackageId    int = NULL,
    @Top          int = 12
AS
BEGIN
    SET NOCOUNT ON;
    SET @Top = IIF(@Top BETWEEN 1 AND 50, @Top, 12);

    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = PartnerId, @brand = BrandCode FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode;
    IF @customerId IS NULL THROW 50404, 'This customer is not in the library.', 1;

    -- Garment per product type (once per type), and the seasons in order.
    SELECT pt.ProductTypeCode, Category = style.fn_GarmentCategory(pt.Name) INTO #ptc FROM ref.ProductType pt;
    SELECT se.SeasonCode, Rank = ISNULL(se.SeasonYear, 0) * 10 + ISNULL(t.SortOrder, 0) INTO #season
    FROM ref.Season se LEFT JOIN ref.SeasonTerm t ON t.Term = se.Term;

    SELECT s.StyleId, s.CustomerId, s.SeasonCode, SeasonRank = ISNULL(sr.Rank, 0), s.IsActive, s.BaseStyleNo,
           IsVariant = IIF(EXISTS (SELECT 1 FROM style.StyleHistory h WHERE h.StyleId = s.StyleId AND h.Relation = 'Variant'), 1, 0),
           Category = ISNULL(pc.Category, IIF(s.ProductTypeCode IS NULL, style.fn_GarmentCategory(s.Description), NULL)),
           Mine = CAST(IIF(s.CustomerId = @customerId OR (@brand IS NOT NULL AND c.BrandCode = @brand), 1, 0) AS bit),
           HasPicture = IIF(s.ImageUrl IS NOT NULL OR s.SketchUrl IS NOT NULL, 1, 0),
           Colorways = (SELECT COUNT(*) FROM style.Colorway k WHERE k.StyleId = s.StyleId),
           HasBom = IIF(EXISTS (SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId), 1, 0),
           InPackage = IIF(@PackageId IS NOT NULL AND EXISTS (SELECT 1 FROM pack.PackageItem i WHERE i.PackageId = @PackageId AND i.StyleId = s.StyleId), 1, 0)
    INTO #s
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN #ptc pc ON pc.ProductTypeCode = s.ProductTypeCode
    LEFT JOIN #season sr ON sr.SeasonCode = s.SeasonCode;
    CREATE UNIQUE CLUSTERED INDEX IX_s ON #s (StyleId);

    -- What this brand made of the styles offered so far (packages sent), per garment and per style.
    SELECT x.Category, i.StyleId,
           Won = IIF(i.Outcome IN ('Sampling', 'Ordered'), 1, 0),
           -- votes: one per person; a style counts as liked when most votes are likes
           Liked = IIF(ISNULL(v.Likes, 0) > ISNULL(v.Passes, 0), 1, 0),
           Lost = IIF(i.Outcome = 'Dropped'
                      OR (ISNULL(i.Outcome, '') NOT IN ('Sampling', 'Ordered') AND ISNULL(v.Passes, 0) > 0 AND ISNULL(v.Likes, 0) = 0), 1, 0)
    INTO #past
    FROM pack.PackageItem i
    JOIN pack.Package p ON p.PackageId = i.PackageId
    JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    JOIN #s x ON x.StyleId = i.StyleId
    OUTER APPLY (SELECT Likes = SUM(IIF(f.Verdict = 'Like', 1, 0)), Passes = SUM(IIF(f.Verdict = 'Pass', 1, 0))
                 FROM pack.ShareFeedback f WHERE f.PackageItemId = i.PackageItemId) v
    WHERE p.Status <> 'Draft' AND p.SentUtc IS NOT NULL
      AND (p.CustomerId = @customerId OR (@brand IS NOT NULL AND c.BrandCode = @brand));
    SELECT Category, Offered = COUNT(*), Won = SUM(Won),
           Boost = GREATEST(-400, LEAST(400, (SUM(Won) * 2 + SUM(Liked) - SUM(Lost)) * 60))
    INTO #hist FROM #past WHERE Category IS NOT NULL GROUP BY Category;

    ALTER TABLE #s ADD Boost int NOT NULL DEFAULT 0, Skip bit NOT NULL DEFAULT 0;
    UPDATE x SET Boost = h.Boost FROM #s x JOIN #hist h ON h.Category = x.Category;
    -- Not again: styles this brand passed on or dropped (and never won or liked), and styles already copied for it.
    UPDATE x SET Skip = 1 FROM #s x
    WHERE EXISTS (SELECT 1 FROM #past r WHERE r.StyleId = x.StyleId AND r.Lost = 1)
      AND NOT EXISTS (SELECT 1 FROM #past r WHERE r.StyleId = x.StyleId AND (r.Won = 1 OR r.Liked = 1));
    UPDATE x SET Skip = 1 FROM #s x
    WHERE x.Mine = 0 AND EXISTS (SELECT 1 FROM style.Style k JOIN #s m ON m.StyleId = k.StyleId AND m.Mine = 1 WHERE k.OriginStyleId = x.StyleId);

    DECLARE @mine int = (SELECT COUNT(*) FROM #s WHERE Mine = 1);
    DECLARE @latest int = (SELECT MAX(SeasonRank) FROM #s WHERE Mine = 1);

    /* 1. Profile */
    SELECT Brand = ISNULL(@brand, @CustomerCode), CustomerName = (SELECT Name FROM partner.Partner WHERE PartnerId = @customerId),
           Accounts = COUNT(DISTINCT x.CustomerId), Styles = COUNT(*),
           FirstSeason = (SELECT TOP (1) SeasonCode FROM #s WHERE Mine = 1 ORDER BY SeasonRank, SeasonCode),
           LatestSeason = (SELECT TOP (1) SeasonCode FROM #s WHERE Mine = 1 ORDER BY SeasonRank DESC, SeasonCode DESC)
    FROM #s x WHERE x.Mine = 1 HAVING COUNT(*) > 0;

    /* 2. Their garments */
    SELECT Category, Styles = COUNT(*), LatestSeason = (SELECT TOP (1) y.SeasonCode FROM #s y WHERE y.Mine = 1 AND y.Category = x.Category ORDER BY y.SeasonRank DESC)
    INTO #theirs
    FROM #s x WHERE x.Mine = 1 AND x.Category IS NOT NULL GROUP BY Category;
    SELECT t.Category, t.Styles, t.LatestSeason, Offered = ISNULL(h.Offered, 0), Won = ISNULL(h.Won, 0)
    FROM #theirs t LEFT JOIN #hist h ON h.Category = t.Category ORDER BY t.Styles DESC, t.Category;

    /* 3. Suggestions */
    CREATE TABLE #sug (Section varchar(8) NOT NULL, StyleId int NOT NULL PRIMARY KEY, Score int NOT NULL, Detail1 int NULL, Detail2 int NULL);

    -- Re-pitch: not carried over since, earlier than the latest season, in garments they buy; the best of each family.
    WITH r AS (SELECT x.StyleId, x.Colorways,
                      Score = x.HasPicture * 1000 + LEAST(x.Colorways, 20) * 20 + x.HasBom * 100 + ISNULL(t.Styles, 0) + (x.SeasonRank / 10) % 100 + x.Boost,
                      Since = (SELECT COUNT(DISTINCT y.SeasonRank) FROM #s y WHERE y.Mine = 1 AND y.SeasonRank > x.SeasonRank),
                      Nth = ROW_NUMBER() OVER (PARTITION BY x.CustomerId, x.BaseStyleNo ORDER BY x.HasPicture DESC, x.Colorways DESC, x.SeasonRank DESC, x.StyleId DESC)
               FROM #s x
               JOIN #theirs t ON t.Category = x.Category
               WHERE x.Mine = 1 AND x.IsActive = 1 AND x.IsVariant = 0 AND x.InPackage = 0 AND x.Skip = 0 AND x.SeasonRank < @latest
                 AND NOT EXISTS (SELECT 1 FROM style.StyleHistory h WHERE h.SourceStyleId = x.StyleId))
    INSERT #sug
    SELECT TOP (@Top) 'Repitch', StyleId, Score, Colorways, Since FROM r WHERE Nth = 1 ORDER BY Score DESC, StyleId DESC;

    -- Gaps: garments the brand (almost) never bought that LT made often for others; up to 3 examples each.
    WITH lt AS (SELECT Category, Styles = COUNT(*) FROM #s WHERE Mine = 0 AND Category IS NOT NULL GROUP BY Category HAVING COUNT(*) >= 10),
         gap AS (SELECT lt.Category, LtStyles = lt.Styles, TheirStyles = ISNULL(t.Styles, 0)
                 FROM lt LEFT JOIN #theirs t ON t.Category = lt.Category
                 WHERE ISNULL(t.Styles, 0) * 100 < 3 * @mine),
         fam AS (SELECT x.StyleId, x.CustomerId, x.BaseStyleNo, g.Category, g.LtStyles, g.TheirStyles, x.HasPicture, x.HasBom, x.SeasonRank,
                        Score = x.HasPicture * 1000 + x.HasBom * 300 + LEAST(x.Colorways, 20) * 10 + (x.SeasonRank / 10) % 100 + x.Boost,
                        InFamily = ROW_NUMBER() OVER (PARTITION BY x.CustomerId, x.BaseStyleNo ORDER BY x.HasPicture DESC, x.HasBom DESC, x.SeasonRank DESC, x.StyleId DESC)
                 FROM gap g JOIN #s x ON x.Category = g.Category
                 WHERE x.Mine = 0 AND x.IsActive = 1 AND x.IsVariant = 0 AND x.InPackage = 0 AND x.Skip = 0 AND NOT EXISTS (SELECT 1 FROM #sug u WHERE u.StyleId = x.StyleId)),
         pick AS (SELECT StyleId, LtStyles, TheirStyles, Score,
                         Nth = ROW_NUMBER() OVER (PARTITION BY Category ORDER BY HasPicture DESC, HasBom DESC, SeasonRank DESC, StyleId DESC)
                  FROM fam WHERE InFamily = 1)
    INSERT #sug
    SELECT TOP (@Top) 'Gap', StyleId, Score + LtStyles, TheirStyles, LtStyles FROM pick WHERE Nth <= 3 ORDER BY LtStyles DESC, Nth, Score DESC;

    -- Fabrics they already buy, on other brands' styles.
    SELECT DISTINCT b.MaterialId INTO #fab
    FROM style.BomLine b JOIN #s x ON x.StyleId = b.StyleId WHERE x.Mine = 1 AND b.ContentClassCode = N'FAB';
    WITH shared AS (SELECT x.StyleId, x.CustomerId, x.BaseStyleNo, Fabrics = COUNT(DISTINCT b.MaterialId),
                           Score = COUNT(DISTINCT b.MaterialId) * 100 + MAX(x.HasPicture) * 50 + MAX(x.SeasonRank) / 10 % 100 + MAX(x.Boost)
                    FROM #s x JOIN style.BomLine b ON b.StyleId = x.StyleId AND b.ContentClassCode = N'FAB' JOIN #fab f ON f.MaterialId = b.MaterialId
                    WHERE x.Mine = 0 AND x.IsActive = 1 AND x.IsVariant = 0 AND x.InPackage = 0 AND x.Skip = 0 AND NOT EXISTS (SELECT 1 FROM #sug u WHERE u.StyleId = x.StyleId)
                    GROUP BY x.StyleId, x.CustomerId, x.BaseStyleNo
                    HAVING COUNT(DISTINCT b.MaterialId) >= 2),
         best AS (SELECT *, Nth = ROW_NUMBER() OVER (PARTITION BY CustomerId, BaseStyleNo ORDER BY Score DESC, StyleId DESC) FROM shared)
    INSERT #sug
    SELECT TOP (@Top) 'Fabric', StyleId, Score, Fabrics, NULL FROM best WHERE Nth = 1 ORDER BY Score DESC, StyleId DESC;

    SELECT u.Section, u.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, s.Description, ProductTypeName = pt.Name,
           x.Category, s.ImageUrl, s.SketchUrl, SameBrand = x.Mine, u.Detail1, u.Detail2,
           CategoryOffered = ISNULL(h.Offered, 0), CategoryWon = ISNULL(h.Won, 0)
    FROM #sug u
    JOIN #s x ON x.StyleId = u.StyleId
    LEFT JOIN #hist h ON h.Category = x.Category
    JOIN style.Style s ON s.StyleId = u.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    ORDER BY CASE u.Section WHEN 'Repitch' THEN 1 WHEN 'Gap' THEN 2 ELSE 3 END, u.Score DESC, u.StyleId DESC;
END
GO

/* ---------- Share links (roadmap 5.16) ---------- */

/*
    Makes a share link: the API makes the secret and passes its SHA-256 (for lookups) and the secret encrypted with the web
    server's keys (to copy the link again, usp_Share_Token). A draft package becomes Sent: its link is about to go to the
    client. Returns ShareId.
*/
CREATE OR ALTER PROCEDURE pack.usp_Share_Create
    @PackageId      int,
    @TokenHash      binary(32),
    @Label          nvarchar(150),
    @ExpiresUtc     datetime2(3),
    @ChangedBy      nvarchar(64),
    @TokenProtected nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Label = NULLIF(LTRIM(RTRIM(@Label)), N'');
    IF @Label IS NULL THROW 50400, 'Say who the link is for.', 1;
    IF @ExpiresUtc <= SYSUTCDATETIME() THROW 50400, 'Choose an expiry date in the future.', 1;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM pack.Package WITH (UPDLOCK) WHERE PackageId = @PackageId)
        THROW 50404, 'This package no longer exists.', 1;
    INSERT pack.PackageShare (PackageId, TokenHash, TokenProtected, Label, ExpiresUtc, CreatedBy)
    VALUES (@PackageId, @TokenHash, @TokenProtected, @Label, @ExpiresUtc, @ChangedBy);
    DECLARE @shareId int = CAST(SCOPE_IDENTITY() AS int);
    UPDATE pack.Package SET Status = 'Sent', SentUtc = ISNULL(SentUtc, SYSUTCDATETIME()), UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    WHERE PackageId = @PackageId AND Status = 'Draft';
    COMMIT TRANSACTION;
    SELECT ShareId = @shareId;
END
GO

/*
    A package's links, newest first, with views and the client's answers so far. Active = not revoked and not expired.
    CanCopy = active and kept encrypted (links made before 2026-10-28 have only their fingerprint).
*/
CREATE OR ALTER PROCEDURE pack.usp_Share_List
    @PackageId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT sh.ShareId, sh.Label, sh.ExpiresUtc, sh.RevokedUtc, sh.RevokedBy, sh.Views, sh.FirstViewedUtc, sh.LastViewedUtc, sh.CreatedBy, sh.CreatedUtc,
           Active = CAST(IIF(sh.RevokedUtc IS NULL AND sh.ExpiresUtc > SYSUTCDATETIME(), 1, 0) AS bit),
           CanCopy = CAST(IIF(sh.RevokedUtc IS NULL AND sh.ExpiresUtc > SYSUTCDATETIME() AND sh.TokenProtected IS NOT NULL, 1, 0) AS bit),
           Likes = (SELECT COUNT(*) FROM pack.ShareFeedback f WHERE f.ShareId = sh.ShareId AND f.Verdict = 'Like'),
           Passes = (SELECT COUNT(*) FROM pack.ShareFeedback f WHERE f.ShareId = sh.ShareId AND f.Verdict = 'Pass'),
           Comments = (SELECT COUNT(*) FROM pack.ShareFeedback f WHERE f.ShareId = sh.ShareId AND f.Comment IS NOT NULL)
    FROM pack.PackageShare sh
    WHERE sh.PackageId = @PackageId
    ORDER BY sh.CreatedUtc DESC;
END
GO

/* The encrypted secret of an active link of the package (to copy it again); no row when revoked, expired or not kept. */
CREATE OR ALTER PROCEDURE pack.usp_Share_Token
    @PackageId int,
    @ShareId   int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TokenProtected FROM pack.PackageShare
    WHERE ShareId = @ShareId AND PackageId = @PackageId AND RevokedUtc IS NULL AND ExpiresUtc > SYSUTCDATETIME() AND TokenProtected IS NOT NULL;
END
GO

CREATE OR ALTER PROCEDURE pack.usp_Share_Revoke
    @PackageId int,
    @ShareId   int,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM pack.PackageShare WHERE ShareId = @ShareId AND PackageId = @PackageId)
        THROW 50404, 'This link no longer exists.', 1;
    UPDATE pack.PackageShare SET RevokedUtc = SYSUTCDATETIME(), RevokedBy = @ChangedBy WHERE ShareId = @ShareId AND RevokedUtc IS NULL;
END
GO

/*
    The client opens a link: a live link (not revoked, not expired) is counted and logged, and a Sent package becomes
    Viewed. Returns the link (ShareId, PackageId, Label, ExpiresUtc) and this link's answers so far; no rows = no such live
    link (the API answers 404 whatever the reason, so a guessed link learns nothing). Both result sets always come back.
*/
CREATE OR ALTER PROCEDURE pack.usp_Share_Open
    @TokenHash binary(32),
    @Ip        varchar(45)   = NULL,
    @UserAgent nvarchar(300) = NULL,
    @Count     bit           = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @shareId int, @packageId int;
    SELECT @shareId = ShareId, @packageId = PackageId FROM pack.PackageShare
    WHERE TokenHash = @TokenHash AND RevokedUtc IS NULL AND ExpiresUtc > SYSUTCDATETIME();

    IF @shareId IS NOT NULL AND @Count = 1
    BEGIN
        BEGIN TRANSACTION;
        UPDATE pack.PackageShare
        SET Views = Views + 1, FirstViewedUtc = ISNULL(FirstViewedUtc, SYSUTCDATETIME()), LastViewedUtc = SYSUTCDATETIME()
        WHERE ShareId = @shareId;
        INSERT pack.ShareAccess (ShareId, Ip, UserAgent) VALUES (@shareId, @Ip, LEFT(@UserAgent, 300));
        UPDATE pack.Package SET Status = 'Viewed' WHERE PackageId = @packageId AND Status = 'Sent';
        COMMIT TRANSACTION;
    END

    SELECT ShareId, PackageId, Label, ExpiresUtc FROM pack.PackageShare WHERE ShareId = @shareId;
    SELECT PackageItemId, Verdict, Comment, Name, UpdatedUtc, Sample, SampleNote FROM pack.ShareFeedback WHERE ShareId = @shareId;   -- every person's; the API shows the visitor their own
END
GO

/*
    A client's answer on one style through a live link: Like / Pass / none and a comment (both empty removes the answer).
    One answer per person: the name typed (any case; no name = one shared answer for the link).
    Only styles of the package the client may see (not another brand's). 50404 when the link or the style is not there.
    What is new (a like or pass given, a comment added or changed) goes into a notice for the package's owner and whoever
    made the link: added to their unread notice for this link when it changed in the last 10 minutes, else a new one.
    Returns those notices (for the API to push to the users), none when nothing new was said.
*/
CREATE OR ALTER PROCEDURE pack.usp_Share_Feedback
    @TokenHash     binary(32),
    @PackageItemId int,
    @Verdict       varchar(4)     = NULL,
    @Comment       nvarchar(1000) = NULL,
    @Name          nvarchar(100)  = NULL,
    @Sample        bit            = 0,
    @SampleNote    nvarchar(500)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Sample = ISNULL(@Sample, 0);
    SET @SampleNote = IIF(@Sample = 1, NULLIF(LTRIM(RTRIM(@SampleNote)), N''), NULL);
    SET @Comment = NULLIF(LTRIM(RTRIM(@Comment)), N'');
    SET @Name = NULLIF(LTRIM(RTRIM(@Name)), N'');
    IF @Verdict IS NOT NULL AND @Verdict NOT IN ('Like', 'Pass') THROW 50400, 'Choose Like or Pass.', 1;

    DECLARE @shareId int = (
        SELECT sh.ShareId
        FROM pack.PackageShare sh
        JOIN pack.PackageItem i ON i.PackageId = sh.PackageId AND i.PackageItemId = @PackageItemId
        JOIN pack.Package p ON p.PackageId = sh.PackageId
        JOIN partner.Partner pc ON pc.PartnerId = p.CustomerId
        JOIN style.Style s ON s.StyleId = i.StyleId
        JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
        WHERE sh.TokenHash = @TokenHash AND sh.RevokedUtc IS NULL AND sh.ExpiresUtc > SYSUTCDATETIME()
          AND (s.CustomerId = p.CustomerId OR (pc.BrandCode IS NOT NULL AND sc.BrandCode = pc.BrandCode)));
    IF @shareId IS NULL THROW 50404, 'Not found.', 1;

    DECLARE @voter nvarchar(100) = LOWER(ISNULL(@Name, N''));
    DECLARE @oldVerdict varchar(4), @oldComment nvarchar(1000), @oldSample bit, @oldSampleNote nvarchar(500);
    DECLARE @noticed TABLE (NoticeId int NOT NULL);

    BEGIN TRANSACTION;
    SELECT @oldVerdict = Verdict, @oldComment = Comment, @oldSample = Sample, @oldSampleNote = SampleNote FROM pack.ShareFeedback WITH (UPDLOCK)
    WHERE ShareId = @shareId AND PackageItemId = @PackageItemId AND VoterKey = @voter;
    DELETE pack.ShareFeedback WHERE ShareId = @shareId AND PackageItemId = @PackageItemId AND VoterKey = @voter;
    IF @Verdict IS NOT NULL OR @Comment IS NOT NULL OR @Sample = 1
        INSERT pack.ShareFeedback (ShareId, PackageItemId, VoterKey, Verdict, Comment, Name, Sample, SampleNote)
        VALUES (@shareId, @PackageItemId, @voter, @Verdict, @Comment, @Name, @Sample, @SampleNote);

    DECLARE @likes int = IIF(@Verdict = 'Like' AND ISNULL(@oldVerdict, '') <> 'Like', 1, 0),
            @passes int = IIF(@Verdict = 'Pass' AND ISNULL(@oldVerdict, '') <> 'Pass', 1, 0),
            @comments int = IIF(@Comment IS NOT NULL AND @Comment <> ISNULL(@oldComment, N''), 1, 0),
            -- a new request, or a changed note on one
            @samples int = IIF(@Sample = 1 AND (ISNULL(@oldSample, 0) = 0 OR ISNULL(@SampleNote, N'') <> ISNULL(@oldSampleNote, N'')), 1, 0);

    -- The first request moves the style (and a package only sent or viewed) to Sampling; LT can still change it by hand.
    IF @Sample = 1 AND ISNULL(@oldSample, 0) = 0
    BEGIN
        UPDATE pack.PackageItem SET Outcome = 'Sampling', OutcomeBy = LEFT(N'client: ' + ISNULL(@Name, N'share link'), 64), OutcomeUtc = SYSUTCDATETIME()
        WHERE PackageItemId = @PackageItemId AND Outcome IS NULL;
        UPDATE p SET Status = 'Sampling'
        FROM pack.Package p JOIN pack.PackageShare sh ON sh.PackageId = p.PackageId
        WHERE sh.ShareId = @shareId AND p.Status IN ('Sent', 'Viewed');
    END

    IF @likes + @passes + @comments + @samples > 0
    BEGIN
        DECLARE @packageId int = (SELECT PackageId FROM pack.PackageShare WHERE ShareId = @shareId);
        DECLARE @to TABLE (UserName nvarchar(64) NOT NULL PRIMARY KEY);
        INSERT @to SELECT OwnerName FROM pack.Package WHERE PackageId = @packageId
                   UNION SELECT CreatedBy FROM pack.PackageShare WHERE ShareId = @shareId;
        UPDATE n SET Likes = Likes + @likes, Passes = Passes + @passes, Comments = Comments + @comments, Samples = Samples + @samples,
                     ClientName = ISNULL(@Name, ClientName), LastUtc = SYSUTCDATETIME()
        OUTPUT inserted.NoticeId INTO @noticed
        FROM pack.PackageNotice n JOIN @to t ON t.UserName = n.UserName
        WHERE n.ShareId = @shareId AND n.ReadUtc IS NULL AND n.LastUtc > DATEADD(MINUTE, -10, SYSUTCDATETIME());
        INSERT pack.PackageNotice (UserName, PackageId, ShareId, Likes, Passes, Comments, Samples, ClientName)
        OUTPUT inserted.NoticeId INTO @noticed
        SELECT t.UserName, @packageId, @shareId, @likes, @passes, @comments, @samples, @Name
        FROM @to t
        WHERE NOT EXISTS (SELECT 1 FROM pack.PackageNotice n
                          WHERE n.UserName = t.UserName AND n.ShareId = @shareId AND n.ReadUtc IS NULL AND n.LastUtc > DATEADD(MINUTE, -10, SYSUTCDATETIME()));
    END
    COMMIT TRANSACTION;

    SELECT n.NoticeId, n.UserName, n.PackageId, PackageName = p.Name, ShareLabel = sh.Label, n.Likes, n.Passes, n.Comments, n.ClientName, n.FirstUtc, n.LastUtc,
           n.Samples
    FROM @noticed x
    JOIN pack.PackageNotice n ON n.NoticeId = x.NoticeId
    JOIN pack.Package p ON p.PackageId = n.PackageId
    JOIN pack.PackageShare sh ON sh.ShareId = n.ShareId;
END
GO

/*
    1 when the stored image file is shown on a live link's page: the picture or sketch of a style in the package, or a
    picture of one of its colourways. The share page's pictures are served only then.
*/
CREATE OR ALTER PROCEDURE pack.usp_Share_StoryImageAllowed
    @TokenHash binary(32),
    @ImageUrl  nvarchar(500)
AS
BEGIN
    SET NOCOUNT ON;
    -- A concept picture marked for the client, of a concept with a client story, of a style of the package's brand in it.
    SELECT Allowed = CAST(IIF(EXISTS (
        SELECT 1
        FROM pack.PackageShare sh
        JOIN pack.Package p ON p.PackageId = sh.PackageId
        JOIN partner.Partner pc ON pc.PartnerId = p.CustomerId
        JOIN pack.PackageItem i ON i.PackageId = sh.PackageId
        JOIN style.Style s ON s.StyleId = i.StyleId
        JOIN partner.Partner sc ON sc.PartnerId = s.CustomerId
        JOIN concept.Concept k ON k.ConceptId = ISNULL(i.ConceptId, s.ConceptId)
        JOIN concept.ConceptImage ci ON ci.ConceptId = k.ConceptId
        WHERE sh.TokenHash = @TokenHash AND sh.RevokedUtc IS NULL AND sh.ExpiresUtc > SYSUTCDATETIME()
          AND (s.CustomerId = p.CustomerId OR (pc.BrandCode IS NOT NULL AND sc.BrandCode = pc.BrandCode))
          AND k.ClientStory IS NOT NULL AND ci.ForClient = 1 AND ci.ImageUrl = @ImageUrl), 1, 0) AS bit);
END
GO

CREATE OR ALTER PROCEDURE pack.usp_Share_ImageAllowed
    @TokenHash binary(32),
    @ImageUrl  nvarchar(500)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Allowed = CAST(IIF(EXISTS (
        SELECT 1
        FROM pack.PackageShare sh
        JOIN pack.PackageItem i ON i.PackageId = sh.PackageId
        JOIN style.Style s ON s.StyleId = i.StyleId
        WHERE sh.TokenHash = @TokenHash AND sh.RevokedUtc IS NULL AND sh.ExpiresUtc > SYSUTCDATETIME()
          AND (s.ImageUrl = @ImageUrl OR s.SketchUrl = @ImageUrl
               OR EXISTS (SELECT 1 FROM style.Colorway k WHERE k.StyleId = s.StyleId AND k.ImageUrl = @ImageUrl))), 1, 0) AS bit);
END
GO

/* ---------- Hit rate (roadmap 5.17) ---------- */

/* What became of a style offered: Sampling, Ordered, Dropped, or NULL (not known yet). Not checked against RowVer: one click. */
CREATE OR ALTER PROCEDURE pack.usp_PackageItem_SetOutcome
    @PackageId     int,
    @PackageItemId int,
    @Outcome       varchar(10) = NULL,
    @ChangedBy     nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Outcome IS NOT NULL AND @Outcome NOT IN ('Sampling', 'Ordered', 'Dropped') THROW 50400, 'Choose Sampling, Ordered or Dropped.', 1;
    UPDATE pack.PackageItem
    SET Outcome = @Outcome, OutcomeBy = IIF(@Outcome IS NULL, NULL, @ChangedBy), OutcomeUtc = IIF(@Outcome IS NULL, NULL, SYSUTCDATETIME()),
        UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    WHERE PackageItemId = @PackageItemId AND PackageId = @PackageId;
    IF @@ROWCOUNT = 0 THROW 50404, 'This style is no longer in the package.', 1;
END
GO

/*
    Hit rate of the styles offered: every style in a package that went to the client (status Sent or later), sent between
    @FromUtc and @ToUtc (NULL = no limit), optionally for one customer's brand (@CustomerCode: the customer and every
    customer of its brand). Clients' answers are votes: one per person (the name they typed) per link per style.
    Per style: LikeVotes / PassVotes, and its result - AllLiked (only likes), Mixed (likes and passes), AllPassed (only
    passes); Answered = styles with any vote; Outcome (Sampling, Ordered, Dropped) as set on the package page.
    Result sets (each with Offered, Answered, LikeVotes, PassVotes, AllLiked, Mixed, AllPassed, Sampling, Ordered, Dropped,
    Packages): 1. totals; 2. by garment (style.fn_GarmentCategory of the product type, else of the description);
    3. by client (brand of the package's customer); 4. by where the style was added from (Styles, Offer, Copy, ...).
*/
CREATE OR ALTER PROCEDURE pack.usp_HitRate
    @CustomerCode nvarchar(32)  = NULL,
    @FromUtc      datetime2(3)  = NULL,
    @ToUtc        datetime2(3)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int, @brand nvarchar(32);
    IF @CustomerCode IS NOT NULL
    BEGIN
        SELECT @customerId = PartnerId, @brand = BrandCode FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode;
        IF @customerId IS NULL SET @customerId = -1;
    END

    SELECT pt.ProductTypeCode, Category = style.fn_GarmentCategory(pt.Name) INTO #ptc
    FROM ref.ProductType pt
    WHERE EXISTS (SELECT 1 FROM style.Style s JOIN pack.PackageItem i ON i.StyleId = s.StyleId WHERE s.ProductTypeCode = pt.ProductTypeCode);

    SELECT i.PackageItemId, i.PackageId, i.Outcome,
           Source = ISNULL(i.AddedFrom, 'Unknown'),
           Client = ISNULL(c.BrandCode, c.PartnerCode),
           Category = ISNULL(ISNULL(pc.Category, IIF(s.ProductTypeCode IS NULL, style.fn_GarmentCategory(s.Description), NULL)), 'Other'),
           Likes = ISNULL(v.Likes, 0), Passes = ISNULL(v.Passes, 0)
    INTO #o
    FROM pack.PackageItem i
    JOIN pack.Package p ON p.PackageId = i.PackageId
    JOIN partner.Partner c ON c.PartnerId = p.CustomerId
    JOIN style.Style s ON s.StyleId = i.StyleId
    LEFT JOIN #ptc pc ON pc.ProductTypeCode = s.ProductTypeCode
    OUTER APPLY (SELECT Likes = SUM(IIF(f.Verdict = 'Like', 1, 0)), Passes = SUM(IIF(f.Verdict = 'Pass', 1, 0))
                 FROM pack.ShareFeedback f WHERE f.PackageItemId = i.PackageItemId) v
    WHERE p.Status <> 'Draft' AND p.SentUtc IS NOT NULL
      AND (@FromUtc IS NULL OR p.SentUtc >= @FromUtc) AND (@ToUtc IS NULL OR p.SentUtc < @ToUtc)
      AND (@customerId IS NULL OR p.CustomerId = @customerId OR (@brand IS NOT NULL AND c.BrandCode = @brand));

    -- The same columns for each grouping (totals: Key NULL).
    SELECT [Key] = CAST(NULL AS nvarchar(40)), Offered = COUNT(*), Answered = COUNT(CASE WHEN Likes + Passes > 0 THEN 1 END),
           LikeVotes = ISNULL(SUM(Likes), 0), PassVotes = ISNULL(SUM(Passes), 0),
           AllLiked = COUNT(CASE WHEN Likes > 0 AND Passes = 0 THEN 1 END), Mixed = COUNT(CASE WHEN Likes > 0 AND Passes > 0 THEN 1 END),
           AllPassed = COUNT(CASE WHEN Passes > 0 AND Likes = 0 THEN 1 END),
           Sampling = COUNT(CASE WHEN Outcome = 'Sampling' THEN 1 END), Ordered = COUNT(CASE WHEN Outcome = 'Ordered' THEN 1 END),
           Dropped = COUNT(CASE WHEN Outcome = 'Dropped' THEN 1 END), Packages = COUNT(DISTINCT PackageId)
    FROM #o;

    SELECT [Key] = Category, Offered = COUNT(*), Answered = COUNT(CASE WHEN Likes + Passes > 0 THEN 1 END),
           LikeVotes = SUM(Likes), PassVotes = SUM(Passes),
           AllLiked = COUNT(CASE WHEN Likes > 0 AND Passes = 0 THEN 1 END), Mixed = COUNT(CASE WHEN Likes > 0 AND Passes > 0 THEN 1 END),
           AllPassed = COUNT(CASE WHEN Passes > 0 AND Likes = 0 THEN 1 END),
           Sampling = COUNT(CASE WHEN Outcome = 'Sampling' THEN 1 END), Ordered = COUNT(CASE WHEN Outcome = 'Ordered' THEN 1 END),
           Dropped = COUNT(CASE WHEN Outcome = 'Dropped' THEN 1 END), Packages = COUNT(DISTINCT PackageId)
    FROM #o GROUP BY Category ORDER BY Offered DESC, [Key];

    SELECT [Key] = Client, Offered = COUNT(*), Answered = COUNT(CASE WHEN Likes + Passes > 0 THEN 1 END),
           LikeVotes = SUM(Likes), PassVotes = SUM(Passes),
           AllLiked = COUNT(CASE WHEN Likes > 0 AND Passes = 0 THEN 1 END), Mixed = COUNT(CASE WHEN Likes > 0 AND Passes > 0 THEN 1 END),
           AllPassed = COUNT(CASE WHEN Passes > 0 AND Likes = 0 THEN 1 END),
           Sampling = COUNT(CASE WHEN Outcome = 'Sampling' THEN 1 END), Ordered = COUNT(CASE WHEN Outcome = 'Ordered' THEN 1 END),
           Dropped = COUNT(CASE WHEN Outcome = 'Dropped' THEN 1 END), Packages = COUNT(DISTINCT PackageId)
    FROM #o GROUP BY Client ORDER BY Offered DESC, [Key];

    SELECT [Key] = Source, Offered = COUNT(*), Answered = COUNT(CASE WHEN Likes + Passes > 0 THEN 1 END),
           LikeVotes = SUM(Likes), PassVotes = SUM(Passes),
           AllLiked = COUNT(CASE WHEN Likes > 0 AND Passes = 0 THEN 1 END), Mixed = COUNT(CASE WHEN Likes > 0 AND Passes > 0 THEN 1 END),
           AllPassed = COUNT(CASE WHEN Passes > 0 AND Likes = 0 THEN 1 END),
           Sampling = COUNT(CASE WHEN Outcome = 'Sampling' THEN 1 END), Ordered = COUNT(CASE WHEN Outcome = 'Ordered' THEN 1 END),
           Dropped = COUNT(CASE WHEN Outcome = 'Dropped' THEN 1 END), Packages = COUNT(DISTINCT PackageId)
    FROM #o GROUP BY Source ORDER BY Offered DESC, [Key];
END
GO

/* ---------- Notices to LT users when clients answer (the bell) ---------- */

/* A user's package notices, newest first (unread first); read ones older than 90 days are removed on the way. */
CREATE OR ALTER PROCEDURE pack.usp_Notice_List
    @UserName nvarchar(64),
    @Take     int = 30
AS
BEGIN
    SET NOCOUNT ON;
    DELETE pack.PackageNotice WHERE UserName = @UserName AND ReadUtc < DATEADD(DAY, -90, SYSUTCDATETIME());
    SELECT TOP (IIF(@Take BETWEEN 1 AND 100, @Take, 30))
           n.NoticeId, n.PackageId, PackageName = p.Name, ShareLabel = sh.Label, n.Likes, n.Passes, n.Comments, n.ClientName, n.FirstUtc, n.LastUtc,
           IsRead = CAST(IIF(n.ReadUtc IS NULL, 0, 1) AS bit), n.Samples
    FROM pack.PackageNotice n
    JOIN pack.Package p ON p.PackageId = n.PackageId
    JOIN pack.PackageShare sh ON sh.ShareId = n.ShareId
    WHERE n.UserName = @UserName
    ORDER BY IIF(n.ReadUtc IS NULL, 0, 1), n.LastUtc DESC;
END
GO

/* Marks one notice (or, @NoticeId NULL, all) of the user read. */
CREATE OR ALTER PROCEDURE pack.usp_Notice_Read
    @UserName nvarchar(64),
    @NoticeId int = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE pack.PackageNotice SET ReadUtc = SYSUTCDATETIME()
    WHERE UserName = @UserName AND ReadUtc IS NULL AND (@NoticeId IS NULL OR NoticeId = @NoticeId);
END
GO

/* ---------- Brand closeness (roadmap 5.2, ODM pitch guardrail) ---------- */

/*
    How close each copied style (style.Style.OriginStyleId set: another brand's style offered as a new style, 5.12) and each
    LT design (style.Style.ConceptId set, 5.21) in the package looks to any other brand's style, by the look-alike vectors of image search (@Model, the image model built into
    the API). A brand's own styles go straight in and are not checked. One row per such item: Pictures = its pictures
    stored by LT ODM, Indexed = those with a vector of @Model (none until image search indexing reads them), and the
    closest picture of a style of another brand than the package's (best picture per style; the original included):
    MatchStyleId ... Similarity (1 = same picture; NULL when nothing could be compared). The API sorts it into close /
    check / clear by the BrandCloseness thresholds.
*/
CREATE OR ALTER PROCEDURE pack.usp_Package_Closeness
    @PackageId int,
    @Model     nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int, @brand nvarchar(32);
    SELECT @customerId = p.CustomerId, @brand = c.BrandCode
    FROM pack.Package p JOIN partner.Partner c ON c.PartnerId = p.CustomerId WHERE p.PackageId = @PackageId;

    SELECT i.PackageItemId, i.StyleId, s.OriginStyleId
    INTO #items
    FROM pack.PackageItem i JOIN style.Style s ON s.StyleId = i.StyleId
    WHERE i.PackageId = @PackageId AND (s.OriginStyleId IS NOT NULL OR s.ConceptId IS NOT NULL);

    -- the items' current pictures and their vectors
    SELECT x.PackageItemId, v.Vector
    INTO #mine
    FROM #items x
    JOIN style.fn_ImageSlots() p ON p.StyleId = x.StyleId
    LEFT JOIN style.ImageVector v ON v.StyleId = p.StyleId AND v.Source = p.Source AND v.ItemId = p.ItemId AND v.ImageUrl = p.ImageUrl
                                 AND v.Model = @Model AND v.Vector IS NOT NULL;

    -- pictures of other brands' styles (not the package customer, not its brand)
    SELECT v.StyleId, v.ImageUrl, v.Vector
    INTO #other
    FROM style.ImageVector v
    JOIN style.fn_ImageSlots() p ON p.StyleId = v.StyleId AND p.Source = v.Source AND p.ItemId = v.ItemId AND p.ImageUrl = v.ImageUrl
    JOIN style.Style s ON s.StyleId = v.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    WHERE v.Model = @Model AND v.Vector IS NOT NULL
      AND s.CustomerId <> @customerId AND (@brand IS NULL OR ISNULL(c.BrandCode, N'') <> @brand);

    WITH scored AS (
        SELECT m.PackageItemId, o.StyleId, o.ImageUrl, Distance = VECTOR_DISTANCE('cosine', m.Vector, o.Vector)
        FROM #mine m CROSS JOIN #other o
        WHERE m.Vector IS NOT NULL),
    best AS (
        SELECT *, Rn = ROW_NUMBER() OVER (PARTITION BY PackageItemId ORDER BY Distance, StyleId) FROM scored)
    SELECT x.PackageItemId, x.StyleId, x.OriginStyleId,
           Pictures = (SELECT COUNT(*) FROM #mine m WHERE m.PackageItemId = x.PackageItemId),
           Indexed = (SELECT COUNT(m.Vector) FROM #mine m WHERE m.PackageItemId = x.PackageItemId),
           MatchStyleId = b.StyleId, MatchStyleNo = ms.StyleNo, MatchSeasonCode = ms.SeasonCode, MatchCustomerCode = mc.PartnerCode,
           MatchImageUrl = b.ImageUrl, Similarity = CAST(1 - b.Distance AS decimal(9, 4))
    FROM #items x
    LEFT JOIN best b ON b.PackageItemId = x.PackageItemId AND b.Rn = 1
    LEFT JOIN style.Style ms ON ms.StyleId = b.StyleId
    LEFT JOIN partner.Partner mc ON mc.PartnerId = ms.CustomerId
    ORDER BY x.PackageItemId;
END
GO


/* ---------- Client profiles (roadmap 5.13) ---------- */

/* The client a customer belongs to: its brand when it has one, else the customer itself. */
CREATE OR ALTER FUNCTION pack.fn_ClientKey (@CustomerCode nvarchar(32))
RETURNS nvarchar(32)
AS
BEGIN
    RETURN (SELECT ISNULL(BrandCode, PartnerCode) FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
END
GO

/*
    Clients for the Client profiles page: every brand, and every customer without a brand, that has styles or packages, with
    accounts, styles, packages, whether a profile is set and its main rules, the next pitch date (today or later), and the
    account with the most styles (MainCustomerCode, for pages that take a customer).
*/
CREATE OR ALTER PROCEDURE pack.usp_ClientProfile_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClientKey = ISNULL(c.BrandCode, c.PartnerCode), c.PartnerId, c.PartnerCode, c.Name, c.BrandCode,
           Styles = (SELECT COUNT(*) FROM style.Style s WHERE s.CustomerId = c.PartnerId),
           Packages = (SELECT COUNT(*) FROM pack.Package p WHERE p.CustomerId = c.PartnerId)
    INTO #c
    FROM partner.Partner c
    WHERE c.PartnerType = 'Customer'
      AND (EXISTS (SELECT 1 FROM style.Style s WHERE s.CustomerId = c.PartnerId) OR EXISTS (SELECT 1 FROM pack.Package p WHERE p.CustomerId = c.PartnerId)
           OR EXISTS (SELECT 1 FROM pack.ClientProfile x WHERE x.ClientKey = ISNULL(c.BrandCode, c.PartnerCode)));

    SELECT k.ClientKey,
           Name = ISNULL(b.Name, (SELECT TOP (1) x.Name FROM #c x WHERE x.ClientKey = k.ClientKey ORDER BY x.Styles DESC)),
           IsBrand = CAST(IIF(b.BrandCode IS NULL, 0, 1) AS bit),
           k.Accounts, k.Styles, k.Packages,
           HasProfile = CAST(IIF(cp.ClientKey IS NULL, 0, 1) AS bit), cp.PriceMin, cp.PriceMax, cp.Currency, cp.MinRecycledPct, cp.RestrictedTerms,
           cp.AccountManager,
           Mills = (SELECT COUNT(*) FROM pack.ClientProfileMill m WHERE m.ClientKey = k.ClientKey),
           NextPitchDate = pd.PitchDate, NextPitchSeason = pd.SeasonCode,
           MainCustomerCode = (SELECT TOP (1) x.PartnerCode FROM #c x WHERE x.ClientKey = k.ClientKey ORDER BY x.Styles DESC, x.PartnerCode)
    FROM (SELECT ClientKey, Accounts = COUNT(*), Styles = SUM(Styles), Packages = SUM(Packages) FROM #c GROUP BY ClientKey) k
    LEFT JOIN ref.Brand b ON b.BrandCode = k.ClientKey
    LEFT JOIN pack.ClientProfile cp ON cp.ClientKey = k.ClientKey
    OUTER APPLY (SELECT TOP (1) d.PitchDate, d.SeasonCode FROM pack.ClientPitchDate d
                 WHERE d.ClientKey = k.ClientKey AND d.PitchDate >= CAST(SYSUTCDATETIME() AS date) ORDER BY d.PitchDate) pd
    ORDER BY IIF(cp.ClientKey IS NULL, 1, 0), k.Styles DESC, k.ClientKey;
END
GO

/* A client's profile (no first row when none is set yet: the page starts an empty one), its preferred mills and pitch dates. */
CREATE OR ALTER PROCEDURE pack.usp_ClientProfile_Get
    @ClientKey nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT cp.ClientKey, cp.PriceMin, cp.PriceMax, cp.Currency, cp.MinRecycledPct, cp.RestrictedTerms, cp.AccountManager, cp.Notes, cp.UpdatedBy,
           cp.UpdatedUtc, cp.RowVer
    FROM pack.ClientProfile cp WHERE cp.ClientKey = @ClientKey;

    SELECT SupplierCode = s.PartnerCode, s.Name
    FROM pack.ClientProfileMill m JOIN partner.Partner s ON s.PartnerId = m.SupplierId
    WHERE m.ClientKey = @ClientKey
    ORDER BY s.PartnerCode;

    SELECT d.PitchDateId, d.SeasonCode, d.PitchDate, d.Note
    FROM pack.ClientPitchDate d WHERE d.ClientKey = @ClientKey
    ORDER BY d.PitchDate;
END
GO

/*
    Creates (@RowVer NULL) or updates a client's profile, with its preferred mills (@Mills: JSON array of supplier codes) and
    pitch dates (@PitchDates: JSON array of {"seasonCode","pitchDate":"yyyy-MM-dd","note"}; both replace what was there).
    The client must exist: a brand, or a customer without a brand.
*/
CREATE OR ALTER PROCEDURE pack.usp_ClientProfile_Save
    @ClientKey       nvarchar(32),
    @RowVer          binary(8)      = NULL,
    @PriceMin        decimal(10, 2) = NULL,
    @PriceMax        decimal(10, 2) = NULL,
    @Currency        char(3)        = 'USD',
    @MinRecycledPct  tinyint        = NULL,
    @RestrictedTerms nvarchar(1000) = NULL,
    @AccountManager  nvarchar(64)   = NULL,
    @Notes           nvarchar(2000) = NULL,
    @Mills           nvarchar(max)  = N'[]',
    @PitchDates      nvarchar(max)  = N'[]',
    @ChangedBy       nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM ref.Brand WHERE BrandCode = @ClientKey)
       AND NOT EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @ClientKey AND BrandCode IS NULL)
        THROW 50404, 'This client is not in the library (a brand, or a customer without a brand).', 1;
    IF ISJSON(@Mills) = 0 OR ISJSON(@PitchDates) = 0 THROW 50400, 'Mills and pitch dates must be JSON arrays.', 1;
    IF @PriceMin > @PriceMax THROW 50400, 'The lowest price is above the highest.', 1;

    DECLARE @mill TABLE (Code nvarchar(32) NOT NULL PRIMARY KEY);
    INSERT @mill SELECT DISTINCT TRIM([value]) FROM OPENJSON(@Mills) WHERE TRIM([value]) <> N'';
    IF EXISTS (SELECT 1 FROM @mill m WHERE NOT EXISTS (SELECT 1 FROM partner.Partner s WHERE s.PartnerType = 'Supplier' AND s.PartnerCode = m.Code))
        THROW 50400, 'A preferred mill is not on the supplier list (Settings > Reference lists > SAP suppliers).', 1;
    DECLARE @dates TABLE (SeasonCode nvarchar(16) NULL, PitchDate date NOT NULL, Note nvarchar(200) NULL);
    INSERT @dates
    SELECT UPPER(NULLIF(TRIM(j.SeasonCode), N'')), j.PitchDate, NULLIF(TRIM(j.Note), N'')
    FROM OPENJSON(@PitchDates) WITH (SeasonCode nvarchar(16) '$.seasonCode', PitchDate date '$.pitchDate', Note nvarchar(200) '$.note') j;
    IF EXISTS (SELECT 1 FROM @dates WHERE PitchDate IS NULL) THROW 50400, 'Give every pitch date a date.', 1;

    BEGIN TRANSACTION;
    IF @RowVer IS NULL AND EXISTS (SELECT 1 FROM pack.ClientProfile WITH (UPDLOCK) WHERE ClientKey = @ClientKey)
        THROW 50409, 'Someone else set up this client''s profile meanwhile. Reload and try again.', 1;
    IF @RowVer IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pack.ClientProfile WITH (UPDLOCK) WHERE ClientKey = @ClientKey AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this profile after you opened it. Reload and try again.', 1;

    DECLARE @season nvarchar(16);
    DECLARE seasons CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT SeasonCode FROM @dates WHERE SeasonCode IS NOT NULL;
    OPEN seasons;
    FETCH NEXT FROM seasons INTO @season;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC style.usp_EnsureSeason @season;
        FETCH NEXT FROM seasons INTO @season;
    END
    CLOSE seasons;
    DEALLOCATE seasons;

    IF @RowVer IS NULL
        INSERT pack.ClientProfile (ClientKey, PriceMin, PriceMax, Currency, MinRecycledPct, RestrictedTerms, AccountManager, Notes, UpdatedBy)
        VALUES (@ClientKey, @PriceMin, @PriceMax, UPPER(ISNULL(NULLIF(@Currency, ''), 'USD')), @MinRecycledPct, NULLIF(TRIM(@RestrictedTerms), N''),
                NULLIF(TRIM(@AccountManager), N''), NULLIF(TRIM(@Notes), N''), @ChangedBy);
    ELSE
        UPDATE pack.ClientProfile
        SET PriceMin = @PriceMin, PriceMax = @PriceMax, Currency = UPPER(ISNULL(NULLIF(@Currency, ''), 'USD')), MinRecycledPct = @MinRecycledPct,
            RestrictedTerms = NULLIF(TRIM(@RestrictedTerms), N''), AccountManager = NULLIF(TRIM(@AccountManager), N''), Notes = NULLIF(TRIM(@Notes), N''),
            UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        WHERE ClientKey = @ClientKey;

    DELETE pack.ClientProfileMill WHERE ClientKey = @ClientKey;
    INSERT pack.ClientProfileMill (ClientKey, SupplierId)
    SELECT @ClientKey, s.PartnerId FROM @mill m JOIN partner.Partner s ON s.PartnerType = 'Supplier' AND s.PartnerCode = m.Code;
    DELETE pack.ClientPitchDate WHERE ClientKey = @ClientKey;
    INSERT pack.ClientPitchDate (ClientKey, SeasonCode, PitchDate, Note) SELECT @ClientKey, SeasonCode, PitchDate, Note FROM @dates;
    COMMIT TRANSACTION;
END
GO

/*
    Checks styles (@StyleIds: JSON array) against the profile of @CustomerCode's client. One row per finding:
      Recycled   : the share of fabric lines written as recycled is below MinRecycledPct (Detail: the share and the minimum;
                   'none' when the style has no fabric lines to judge);
      Restricted : a restricted word appears in a BOM line's description (Detail: the word and the material);
      Mill       : fabric lines from suppliers that are not preferred mills (Detail: up to 3 supplier names; lines without a
                   supplier are not counted).
    No rows when the client has no profile. The first result set is the profile's key (NULL = none).
*/
CREATE OR ALTER PROCEDURE pack.usp_ClientProfile_Check
    @CustomerCode nvarchar(32),
    @StyleIds     nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @key nvarchar(32) = pack.fn_ClientKey(@CustomerCode);
    DECLARE @min tinyint, @terms nvarchar(1000), @mills bit;
    SELECT @min = MinRecycledPct, @terms = RestrictedTerms, @mills = IIF(EXISTS (SELECT 1 FROM pack.ClientProfileMill m WHERE m.ClientKey = cp.ClientKey), 1, 0)
    FROM pack.ClientProfile cp WHERE cp.ClientKey = @key;
    SELECT ClientKey = IIF(@mills IS NULL, NULL, @key);
    IF @mills IS NULL RETURN;

    DECLARE @s TABLE (StyleId int NOT NULL PRIMARY KEY);
    INSERT @s SELECT DISTINCT TRY_CAST([value] AS int) FROM OPENJSON(ISNULL(@StyleIds, N'[]')) WHERE TRY_CAST([value] AS int) IS NOT NULL;
    DECLARE @t TABLE (Term nvarchar(60) NOT NULL PRIMARY KEY, Pattern nvarchar(80) NOT NULL);
    INSERT @t
    SELECT DISTINCT t, N'%' + REPLACE(REPLACE(REPLACE(t, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%'
    FROM (SELECT t = UPPER(LEFT(TRIM(value), 60)) FROM STRING_SPLIT(REPLACE(ISNULL(@terms, N''), N';', N','), N',')) x WHERE LEN(t) >= 2;

    -- Recycled content
    SELECT s.StyleId, Kind = 'Recycled',
           Detail = IIF(f.Lines = 0, N'none', CONCAT(f.Recycled * 100 / f.Lines, N'% (needs ', @min, N'%)'))
    FROM @s s
    CROSS APPLY (SELECT Lines = COUNT(*), Recycled = ISNULL(SUM(CAST(mat.fn_IsRecycled(b.MaterialDescription) AS int)), 0)
                 FROM style.BomLine b WHERE b.StyleId = s.StyleId AND b.ContentClassCode = N'FAB') f
    WHERE @min IS NOT NULL AND @min > 0 AND (f.Lines = 0 OR f.Recycled * 100 < @min * f.Lines)
    UNION ALL
    -- Restricted words, one row per style and word (the first material holding it)
    SELECT x.StyleId, 'Restricted', CONCAT(x.Term, N': ', LEFT(x.Material, 120))
    FROM (SELECT s.StyleId, t.Term, Material = ISNULL(b.MaterialDescription, m.MaterialCode),
                 Rn = ROW_NUMBER() OVER (PARTITION BY s.StyleId, t.Term ORDER BY b.LineSeq)
          FROM @s s
          JOIN style.BomLine b ON b.StyleId = s.StyleId
          JOIN mat.Material m ON m.MaterialId = b.MaterialId
          JOIN @t t ON UPPER(ISNULL(b.MaterialDescription, m.Description)) COLLATE Latin1_General_100_BIN2 LIKE t.Pattern) x
    WHERE x.Rn = 1
    UNION ALL
    -- Fabrics from mills that are not preferred
    SELECT s.StyleId, 'Mill', o.Names
    FROM @s s
    CROSS APPLY (SELECT Names = STRING_AGG(n.Name, N', ')
                 FROM (SELECT DISTINCT TOP (3) Name = sp.Name FROM style.BomLine b JOIN partner.Partner sp ON sp.PartnerId = b.SupplierId
                       WHERE b.StyleId = s.StyleId AND b.ContentClassCode = N'FAB'
                         AND NOT EXISTS (SELECT 1 FROM pack.ClientProfileMill m WHERE m.ClientKey = @key AND m.SupplierId = b.SupplierId)) n) o
    WHERE @mills = 1 AND o.Names IS NOT NULL
    ORDER BY 1, 2;
END
GO

/* ---------- Follow-up reminders (roadmap 5.24) ---------- */

/*
    A user's follow-ups, worked out now (nothing is stored but what was marked done or put off, pack.FollowUpDismissal):
      NotOpened : an active link made @NotOpenedDays or more ago and never opened;
      NoAnswer  : an active link first opened @NoAnswerDays or more ago with no answer through it;
      Expiring  : an active link that ends within @ExpiringDays;
      Sample    : a client's sample request @SampleDays or more old whose style still has the outcome the client set (LT has not
                  set it by hand since);
      Pitch     : a pitch date of a client whose account manager is the user, within @PitchDays, with no package of that
                  client (for that season, when one is given) sent yet.
    Link and sample reminders go to the package owner and the link maker; packages Ordered or Closed are left out.
    RefKey identifies the thing reminded about (S+ShareId, I+PackageItemId, P+PitchDateId).
*/
CREATE OR ALTER PROCEDURE pack.usp_FollowUp_List
    @UserName      nvarchar(64),
    @NotOpenedDays int = 5,
    @NoAnswerDays  int = 7,
    @ExpiringDays  int = 3,
    @SampleDays    int = 3,
    @PitchDays     int = 14
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now datetime2(3) = SYSUTCDATETIME(), @today date = CAST(SYSUTCDATETIME() AS date);
    DELETE pack.FollowUpDismissal WHERE UserName = @UserName AND UntilUtc IS NOT NULL AND UntilUtc < DATEADD(DAY, -30, @now);

    WITH shares AS (
        SELECT sh.ShareId, sh.PackageId, sh.Label, sh.Views, sh.CreatedUtc, sh.FirstViewedUtc, sh.ExpiresUtc, p.Name, p.OwnerName, sh.CreatedBy,
               Answered = IIF(EXISTS (SELECT 1 FROM pack.ShareFeedback f WHERE f.ShareId = sh.ShareId), 1, 0)
        FROM pack.PackageShare sh JOIN pack.Package p ON p.PackageId = sh.PackageId
        WHERE sh.RevokedUtc IS NULL AND sh.ExpiresUtc > @now AND p.Status NOT IN ('Ordered', 'Closed')
          AND (p.OwnerName = @UserName OR sh.CreatedBy = @UserName)),
    items AS (
        SELECT Kind = CAST('NotOpened' AS varchar(16)), RefKey = CONCAT(N'S', ShareId), PackageId, PackageName = Name, ShareId, ShareLabel = Label,
               PackageItemId = CAST(NULL AS int), StyleNo = CAST(NULL AS nvarchar(100)), ClientKey = CAST(NULL AS nvarchar(32)),
               SeasonCode = CAST(NULL AS nvarchar(16)), Days = DATEDIFF(DAY, CreatedUtc, @now), DueDate = CAST(NULL AS date), Note = CAST(NULL AS nvarchar(500))
        FROM shares WHERE Views = 0 AND CreatedUtc <= DATEADD(DAY, -@NotOpenedDays, @now)
        UNION ALL
        SELECT 'NoAnswer', CONCAT(N'S', ShareId), PackageId, Name, ShareId, Label, NULL, NULL, NULL, NULL, DATEDIFF(DAY, FirstViewedUtc, @now), NULL, NULL
        FROM shares WHERE Views > 0 AND Answered = 0 AND FirstViewedUtc <= DATEADD(DAY, -@NoAnswerDays, @now)
        UNION ALL
        SELECT 'Expiring', CONCAT(N'S', ShareId), PackageId, Name, ShareId, Label, NULL, NULL, NULL, NULL, DATEDIFF(DAY, @now, ExpiresUtc),
               CAST(ExpiresUtc AS date), NULL
        FROM shares WHERE ExpiresUtc <= DATEADD(DAY, @ExpiringDays, @now)
        UNION ALL
        SELECT 'Sample', CONCAT(N'I', i.PackageItemId), p.PackageId, p.Name, MIN(f.ShareId), MIN(sh.Label), i.PackageItemId, s.StyleNo, NULL, NULL,
               DATEDIFF(DAY, MIN(f.UpdatedUtc), @now), NULL, MAX(CONCAT(f.Name, IIF(f.SampleNote IS NULL, N'', N': ' + f.SampleNote)))
        FROM pack.ShareFeedback f
        JOIN pack.PackageShare sh ON sh.ShareId = f.ShareId
        JOIN pack.PackageItem i ON i.PackageItemId = f.PackageItemId
        JOIN pack.Package p ON p.PackageId = i.PackageId
        JOIN style.Style s ON s.StyleId = i.StyleId
        WHERE f.Sample = 1 AND i.Outcome = 'Sampling' AND i.OutcomeBy LIKE N'client:%' AND p.Status NOT IN ('Ordered', 'Closed')
          AND (p.OwnerName = @UserName OR sh.CreatedBy = @UserName)
        GROUP BY i.PackageItemId, p.PackageId, p.Name, s.StyleNo
        HAVING MIN(f.UpdatedUtc) <= DATEADD(DAY, -@SampleDays, @now)
        UNION ALL
        SELECT 'Pitch', CONCAT(N'P', d.PitchDateId), NULL, NULL, NULL, NULL, NULL, NULL, d.ClientKey, d.SeasonCode, DATEDIFF(DAY, @today, d.PitchDate),
               d.PitchDate, d.Note
        FROM pack.ClientPitchDate d
        JOIN pack.ClientProfile cp ON cp.ClientKey = d.ClientKey
        WHERE cp.AccountManager = @UserName AND d.PitchDate BETWEEN @today AND DATEADD(DAY, @PitchDays, @today)
          AND NOT EXISTS (SELECT 1 FROM pack.Package p JOIN partner.Partner c ON c.PartnerId = p.CustomerId
                          WHERE ISNULL(c.BrandCode, c.PartnerCode) = d.ClientKey AND p.Status <> 'Draft'
                            AND (d.SeasonCode IS NULL OR p.SeasonCode = d.SeasonCode)))
    SELECT x.*
    FROM items x
    WHERE NOT EXISTS (SELECT 1 FROM pack.FollowUpDismissal z
                      WHERE z.UserName = @UserName AND z.Kind = x.Kind AND z.RefKey = x.RefKey AND (z.UntilUtc IS NULL OR z.UntilUtc > @now))
    ORDER BY CASE x.Kind WHEN 'Sample' THEN 1 WHEN 'Expiring' THEN 2 WHEN 'Pitch' THEN 3 WHEN 'NoAnswer' THEN 4 ELSE 5 END, x.Days DESC;
END
GO

/* Marks a follow-up done for the user (@Days NULL) or puts it off for @Days days. */
CREATE OR ALTER PROCEDURE pack.usp_FollowUp_Dismiss
    @UserName nvarchar(64),
    @Kind     varchar(16),
    @RefKey   nvarchar(40),
    @Days     int = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kind NOT IN ('NotOpened', 'NoAnswer', 'Expiring', 'Sample', 'Pitch') THROW 50400, 'Unknown kind of follow-up.', 1;
    DECLARE @until datetime2(3) = IIF(@Days IS NULL, NULL, DATEADD(DAY, @Days, SYSUTCDATETIME()));
    UPDATE pack.FollowUpDismissal SET UntilUtc = @until, SetUtc = SYSUTCDATETIME() WHERE UserName = @UserName AND Kind = @Kind AND RefKey = @RefKey;
    IF @@ROWCOUNT = 0 INSERT pack.FollowUpDismissal (UserName, Kind, RefKey, UntilUtc) VALUES (@UserName, @Kind, @RefKey, @until);
END
GO
