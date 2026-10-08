/*
    LT ODM Style Library - styles, colorways, BOM lines and style history (Styles screens, /api/v1/styles)

    Reads:  usp_Style_Lookups, usp_Style_List, usp_Style_Get, usp_Dashboard_Get
    Writes: usp_Style_Save / _Delete / _Copy, usp_Colorway_Save / _Delete, usp_BomLine_Save / _Delete,
            usp_StyleHistory_Set / _Remove

    Every write takes the row's RowVer (rowversion) when it changes an existing row and refuses the change with
    50409 if someone else saved it first. Rule errors are THROWn as 50400 (bad request), 50404 (not found) or
    50409 (conflict) with a message for the user. @ChangedBy is the signed-in user (from the API).
    Codes typed by the user that are new (customer, season, business unit, product type, material, supplier, UOM,
    material type) are added to their lists; content classes and season terms are fixed.
    Requires style.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.procedures.sql
*/
SET NOCOUNT ON;
GO

/* ---------- Reads ---------- */

/* Pick lists for filters and forms. */
CREATE OR ALTER PROCEDURE style.usp_Style_Lookups
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Code = PartnerCode, Name FROM partner.Partner WHERE PartnerType = 'Customer' AND IsActive = 1 ORDER BY PartnerCode;
    SELECT Code = s.SeasonCode, Name = ISNULL(s.Description, s.SeasonCode) FROM ref.Season s JOIN ref.SeasonTerm t ON t.Term = s.Term
    WHERE s.IsActive = 1 ORDER BY s.SeasonYear DESC, t.SortOrder DESC;
    SELECT Code = BusinessUnitCode, Name FROM ref.BusinessUnit WHERE IsActive = 1 ORDER BY Name;
    SELECT Code = ProductTypeCode, Name FROM ref.ProductType WHERE IsActive = 1 ORDER BY Name;
    SELECT Code = WeaveTypeCode, Name FROM ref.WeaveType ORDER BY Name;
    SELECT Code = MaterialTypeCode, Name FROM ref.MaterialType ORDER BY Name;
    SELECT Code = ContentClassCode, Name FROM ref.ContentClass ORDER BY SortOrder;
    SELECT Code = UomCode, Name FROM ref.Uom ORDER BY UomCode;
    SELECT Code = PartnerCode, Name FROM partner.Partner WHERE PartnerType = 'Supplier' AND IsActive = 1 ORDER BY Name;
    SELECT Code = Term, Name FROM ref.SeasonTerm ORDER BY SortOrder;
END
GO

/*
    One page of styles. @Search matches style number, model, description or a colorway code.
    Newest season first, then style number.
*/
CREATE OR ALTER PROCEDURE style.usp_Style_List
    @Search           nvarchar(100) = NULL,
    @CustomerCode     nvarchar(32)  = NULL,
    @SeasonCode       nvarchar(200) = NULL,   -- one code, or several separated by commas
    @BusinessUnitCode nvarchar(200) = NULL,   -- one code, or several separated by commas
    @ProductTypeCode  nvarchar(400) = NULL,   -- one code, or several separated by commas
    @WeaveTypeCode    nvarchar(8)   = NULL,
    @Gender           nvarchar(16)  = NULL,
    @Material         nvarchar(100) = NULL,   -- word(s) in a BOM line's material code or description; several words must all be on the same line
    @IsActive         bit           = NULL,   -- NULL = active and inactive
    @Skip             int           = 0,
    @Take             int           = 50
AS
BEGIN
    SET NOCOUNT ON;
    /* Paged in two steps so the cost stays with the page, not the library: first the ids of the matching styles
       (from the narrow IX_style_Style_List index; the counts and display columns are not touched), then the
       display columns and counts for that page only. */
    /* "Contains" search, case-insensitive: upper-cased and compared in binary (several times faster than the
       database collation for a %...% match, which has to read every style). */
    DECLARE @like nvarchar(110) = UPPER(N'%' + REPLACE(REPLACE(REPLACE(@Search, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%');
    DECLARE @materialWords TABLE (Pattern nvarchar(110) NOT NULL PRIMARY KEY);
    INSERT @materialWords
    SELECT DISTINCT UPPER(N'%' + REPLACE(REPLACE(REPLACE(TRIM(value), N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%')
    FROM STRING_SPLIT(@Material, N' ') WHERE TRIM(value) <> N'';
    DECLARE @businessUnits TABLE (Code nvarchar(16) NOT NULL PRIMARY KEY);
    INSERT @businessUnits SELECT DISTINCT LEFT(TRIM(value), 16) FROM STRING_SPLIT(@BusinessUnitCode, N',') WHERE TRIM(value) <> N'';
    DECLARE @seasons TABLE (Code nvarchar(16) NOT NULL PRIMARY KEY);
    INSERT @seasons SELECT DISTINCT LEFT(TRIM(value), 16) FROM STRING_SPLIT(@SeasonCode, N',') WHERE TRIM(value) <> N'';
    DECLARE @productTypes TABLE (Code nvarchar(40) NOT NULL PRIMARY KEY);
    INSERT @productTypes SELECT DISTINCT LEFT(TRIM(value), 40) FROM STRING_SPLIT(@ProductTypeCode, N',') WHERE TRIM(value) <> N'';
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;

    DECLARE @match TABLE (StyleId int NOT NULL PRIMARY KEY, SeasonYear smallint NOT NULL, TermOrder smallint NOT NULL, StyleNo nvarchar(40) NOT NULL);
    INSERT @match (StyleId, SeasonYear, TermOrder, StyleNo)
    SELECT s.StyleId, se.SeasonYear, t.SortOrder, s.StyleNo
    FROM style.Style s
    JOIN ref.Season se ON se.SeasonCode = s.SeasonCode
    JOIN ref.SeasonTerm t ON t.Term = se.Term
    WHERE (@customerId IS NULL OR s.CustomerId = @customerId)
      AND (@SeasonCode IS NULL OR s.SeasonCode IN (SELECT Code FROM @seasons))
      AND (@BusinessUnitCode IS NULL OR s.BusinessUnitCode IN (SELECT Code FROM @businessUnits))
      AND (@ProductTypeCode IS NULL OR s.ProductTypeCode IN (SELECT Code FROM @productTypes))
      AND (@WeaveTypeCode IS NULL OR s.WeaveTypeCode = @WeaveTypeCode)
      AND (@Gender IS NULL OR s.Gender = @Gender)
      AND (@IsActive IS NULL OR s.IsActive = @IsActive)
      AND (@Material IS NULL OR EXISTS (
               SELECT 1 FROM style.BomLine b JOIN mat.Material m ON m.MaterialId = b.MaterialId
               WHERE b.StyleId = s.StyleId
                 AND NOT EXISTS (SELECT 1 FROM @materialWords w
                                 WHERE UPPER(b.MaterialDescription) COLLATE Latin1_General_100_BIN2 NOT LIKE w.Pattern
                                   AND UPPER(m.MaterialCode) COLLATE Latin1_General_100_BIN2 NOT LIKE w.Pattern)))
      AND (@Search IS NULL OR s.StyleId IN (
               SELECT x.StyleId FROM style.Style x
               WHERE UPPER(x.StyleNo) COLLATE Latin1_General_100_BIN2 LIKE @like OR UPPER(x.ModelName) COLLATE Latin1_General_100_BIN2 LIKE @like
                  OR UPPER(x.ModelCode) COLLATE Latin1_General_100_BIN2 LIKE @like OR UPPER(x.Description) COLLATE Latin1_General_100_BIN2 LIKE @like
               UNION
               SELECT k.StyleId FROM style.Colorway k WHERE UPPER(k.ColorwayCode) COLLATE Latin1_General_100_BIN2 LIKE @like))
    OPTION (RECOMPILE);

    DECLARE @total int = @@ROWCOUNT;

    SELECT s.StyleId, s.StyleNo, s.BaseStyleNo, s.Description, s.ModelCode, s.ModelName,
           CustomerCode = c.PartnerCode, CustomerName = c.Name, s.SeasonCode,
           s.BusinessUnitCode, BusinessUnitName = bu.Name, s.ProductTypeCode, ProductTypeName = pt.Name,
           s.WeaveTypeCode, s.Gender, s.ImageUrl, s.SketchUrl, s.IsActive,
           ColorwayCount = (SELECT COUNT(*) FROM style.Colorway k WHERE k.StyleId = s.StyleId),
           BomLineCount  = (SELECT COUNT(*) FROM style.BomLine b WHERE b.StyleId = s.StyleId),
           HasHistory    = CAST(IIF(EXISTS (SELECT 1 FROM style.StyleHistory h WHERE h.StyleId = s.StyleId OR h.SourceStyleId = s.StyleId), 1, 0) AS bit),
           LastChangedUtc = ISNULL(s.UpdatedUtc, s.CreatedUtc),
           TotalCount = @total
    FROM (SELECT StyleId, SeasonYear, TermOrder, StyleNo FROM @match
          ORDER BY SeasonYear DESC, TermOrder DESC, StyleNo
          OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY) p
    JOIN style.Style s ON s.StyleId = p.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.BusinessUnit bu ON bu.BusinessUnitCode = s.BusinessUnitCode
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    ORDER BY p.SeasonYear DESC, p.TermOrder DESC, p.StyleNo;
END
GO

/*
    A style with everything on its page: header, colorways, BOM lines, the colorways each line uses, and its family
    (same customer + base style number + model, plus any style linked to it by hand), oldest season first.
*/
CREATE OR ALTER PROCEDURE style.usp_Style_Get
    @StyleId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.StyleId, CustomerCode = c.PartnerCode, CustomerName = c.Name, s.SeasonCode, s.StyleNo, s.BaseStyleNo, s.Description,
           s.ModelCode, s.ModelName, s.WeaveTypeCode, WeaveTypeName = w.Name, s.ProductTypeCode, ProductTypeName = pt.Name,
           s.Gender, s.GarmentLeadTimeDays, s.BusinessUnitCode, BusinessUnitName = bu.Name, s.SketchUrl, s.ImageUrl, s.IsActive,
           s.SourceCreatedUtc, s.ImportBatchId, s.CreatedBy, s.CreatedUtc, s.UpdatedBy, s.UpdatedUtc, s.RowVer
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.WeaveType w ON w.WeaveTypeCode = s.WeaveTypeCode
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    LEFT JOIN ref.BusinessUnit bu ON bu.BusinessUnitCode = s.BusinessUnitCode
    WHERE s.StyleId = @StyleId;

    SELECT ColorwayId, SortOrder, ColorwayCode, ColorwayName, Status, ImageUrl, CreatedBy, CreatedUtc, UpdatedBy, UpdatedUtc, RowVer
    FROM style.Colorway WHERE StyleId = @StyleId
    ORDER BY SortOrder, ColorwayCode;

    SELECT bl.BomLineId, bl.MaterialId, bl.LineSeq, bl.PartNo, m.MaterialCode, bl.MaterialDescription, bl.MaterialTypeCode, MaterialTypeName = mt.Name,
           bl.ContentClassCode, ContentClassName = cc.Name, ContentClassSort = cc.SortOrder,
           bl.NominatedSupplierCode, bl.NominatedSupplierName, SupplierCode = p.PartnerCode, SupplierName = p.Name,
           bl.LcoConsumption, bl.BrandConsumption, bl.UomCode, bl.ImageUrl,
           bl.CreatedBy, bl.CreatedUtc, bl.UpdatedBy, bl.UpdatedUtc, bl.RowVer
    FROM style.BomLine bl
    JOIN mat.Material m ON m.MaterialId = bl.MaterialId
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = bl.MaterialTypeCode
    LEFT JOIN ref.ContentClass cc ON cc.ContentClassCode = bl.ContentClassCode
    LEFT JOIN partner.Partner p ON p.PartnerId = bl.SupplierId
    WHERE bl.StyleId = @StyleId
    ORDER BY ISNULL(cc.SortOrder, 99), bl.LineSeq;

    SELECT blc.BomLineId, blc.ColorwayId, blc.MaterialColorCode, blc.MaterialColorDescription
    FROM style.BomLineColorway blc
    JOIN style.BomLine bl ON bl.BomLineId = blc.BomLineId
    WHERE bl.StyleId = @StyleId;

    /* Family: same base number + model, and any style linked to one of them (one step either way). */
    DECLARE @family TABLE (StyleId int PRIMARY KEY);
    INSERT @family
    SELECT f.StyleId FROM style.Style s
    JOIN style.Style f ON f.CustomerId = s.CustomerId AND f.BaseStyleNo = s.BaseStyleNo
                      AND (f.ModelCode = s.ModelCode OR (f.ModelCode IS NULL AND s.ModelCode IS NULL))
    WHERE s.StyleId = @StyleId;
    INSERT @family
    SELECT DISTINCT x.Id FROM style.StyleHistory h
    CROSS APPLY (VALUES (h.StyleId), (h.SourceStyleId)) x (Id)
    WHERE (h.StyleId IN (SELECT StyleId FROM @family) OR h.SourceStyleId IN (SELECT StyleId FROM @family))
      AND x.Id NOT IN (SELECT StyleId FROM @family);

    SELECT s.StyleId, s.StyleNo, s.SeasonCode, s.ModelName, s.ImageUrl,
           h.SourceStyleId, h.Relation, h.Suffix, h.LinkSource, h.Note, LinkedBy = ISNULL(h.UpdatedBy, h.CreatedBy),
           IsCurrent = CAST(IIF(s.StyleId = @StyleId, 1, 0) AS bit)
    FROM @family f
    JOIN style.Style s ON s.StyleId = f.StyleId
    JOIN ref.Season se ON se.SeasonCode = s.SeasonCode
    JOIN ref.SeasonTerm t ON t.Term = se.Term
    LEFT JOIN style.StyleHistory h ON h.StyleId = s.StyleId
    ORDER BY se.SeasonYear, t.SortOrder, LEN(s.StyleNo), s.StyleNo;
END
GO

/*
    Dashboard (landing page): library totals, styles per season, top customers / material types / materials /
    suppliers / product types, recently changed styles and the last committed import.
*/
CREATE OR ALTER PROCEDURE style.usp_Dashboard_Get
    @Top     int = 8,
    @Seasons int = 8
AS
BEGIN
    SET NOCOUNT ON;

    /* 1. Totals and data completeness. */
    SELECT Styles            = (SELECT COUNT(*) FROM style.Style),
           Colorways         = (SELECT COUNT(*) FROM style.Colorway),
           ColorwaysInRange  = (SELECT COUNT(*) FROM style.Colorway WHERE Status = 'INRANGE'),
           ColorwaysWithBom  = (SELECT COUNT(*) FROM style.Colorway k WHERE EXISTS (SELECT 1 FROM style.BomLineColorway x WHERE x.ColorwayId = k.ColorwayId)),
           BomLines          = (SELECT COUNT(*) FROM style.BomLine),
           Materials         = (SELECT COUNT(DISTINCT MaterialId) FROM style.BomLine),
           Suppliers         = (SELECT COUNT(DISTINCT SupplierId) FROM style.BomLine),
           Customers         = (SELECT COUNT(DISTINCT CustomerId) FROM style.Style),
           Seasons           = (SELECT COUNT(DISTINCT SeasonCode) FROM style.Style),
           Families          = (SELECT COUNT(DISTINCT SourceStyleId) FROM style.StyleHistory),
           ReusedStyles      = (SELECT COUNT(*) FROM style.StyleHistory),
           StylesWithImage   = (SELECT COUNT(*) FROM style.Style WHERE ImageUrl IS NOT NULL OR SketchUrl IS NOT NULL),
           StylesWithBom     = (SELECT COUNT(*) FROM style.Style s WHERE EXISTS (SELECT 1 FROM style.BomLine b WHERE b.StyleId = s.StyleId)),
           StylesWithColorways = (SELECT COUNT(*) FROM style.Style s WHERE EXISTS (SELECT 1 FROM style.Colorway k WHERE k.StyleId = s.StyleId));

    /* 2. The latest seasons, oldest first. */
    WITH latest AS
    (
        SELECT TOP (@Seasons) se.SeasonCode, se.SeasonYear, TermOrder = t.SortOrder
        FROM ref.Season se
        JOIN ref.SeasonTerm t ON t.Term = se.Term
        WHERE EXISTS (SELECT 1 FROM style.Style s WHERE s.SeasonCode = se.SeasonCode)
        ORDER BY se.SeasonYear DESC, t.SortOrder DESC
    )
    SELECT l.SeasonCode,
           Styles    = (SELECT COUNT(*) FROM style.Style s WHERE s.SeasonCode = l.SeasonCode),
           Colorways = (SELECT COUNT(*) FROM style.Colorway k JOIN style.Style s ON s.StyleId = k.StyleId WHERE s.SeasonCode = l.SeasonCode),
           BomLines  = (SELECT COUNT(*) FROM style.BomLine b JOIN style.Style s ON s.StyleId = b.StyleId WHERE s.SeasonCode = l.SeasonCode)
    FROM latest l
    ORDER BY l.SeasonYear, l.TermOrder;

    /* 3. Customers by styles. */
    SELECT TOP (@Top) Code = c.PartnerCode, c.Name, Styles = COUNT(*), Colorways = SUM(ISNULL(k.Colorways, 0))
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN (SELECT StyleId, Colorways = COUNT(*) FROM style.Colorway GROUP BY StyleId) k ON k.StyleId = s.StyleId
    GROUP BY c.PartnerCode, c.Name
    ORDER BY Styles DESC, c.PartnerCode;

    /* 4. Material types by BOM lines. */
    SELECT TOP (@Top) Code = ISNULL(b.MaterialTypeCode, N''), Name = ISNULL(mt.Name, N'Not set'),
           BomLines = COUNT(*), Styles = COUNT(DISTINCT b.StyleId)
    FROM style.BomLine b
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = b.MaterialTypeCode
    GROUP BY b.MaterialTypeCode, mt.Name
    ORDER BY BomLines DESC;

    /* 5. Most used materials (styles whose BOM uses them). */
    SELECT m.MaterialCode, m.Description, MaterialTypeName = mt.Name, u.Styles, u.BomLines
    FROM (SELECT TOP (@Top) MaterialId, Styles = COUNT(DISTINCT StyleId), BomLines = COUNT(*)
          FROM style.BomLine GROUP BY MaterialId ORDER BY Styles DESC, BomLines DESC, MaterialId) u
    JOIN mat.Material m ON m.MaterialId = u.MaterialId
    LEFT JOIN ref.MaterialType mt ON mt.MaterialTypeCode = m.MaterialTypeCode
    ORDER BY u.Styles DESC, u.BomLines DESC, m.MaterialCode;

    /* 6. Suppliers by styles supplied. */
    SELECT TOP (@Top) Code = p.PartnerCode, p.Name, Styles = COUNT(DISTINCT b.StyleId), BomLines = COUNT(*)
    FROM style.BomLine b
    JOIN partner.Partner p ON p.PartnerId = b.SupplierId
    GROUP BY p.PartnerCode, p.Name
    ORDER BY Styles DESC, BomLines DESC, p.PartnerCode;

    /* 7. Product types by styles. */
    SELECT TOP (@Top) Code = ISNULL(s.ProductTypeCode, N''), Name = ISNULL(pt.Name, N'Not set'), Styles = COUNT(*)
    FROM style.Style s
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    GROUP BY s.ProductTypeCode, pt.Name
    ORDER BY Styles DESC;

    /* 8. Recently changed styles. */
    SELECT TOP (@Top) s.StyleId, s.StyleNo, s.ModelName, CustomerCode = c.PartnerCode, s.SeasonCode, s.ImageUrl, s.SketchUrl,
           ColorwayCount = (SELECT COUNT(*) FROM style.Colorway k WHERE k.StyleId = s.StyleId),
           BomLineCount  = (SELECT COUNT(*) FROM style.BomLine b WHERE b.StyleId = s.StyleId),
           LastChangedBy = ISNULL(s.UpdatedBy, s.CreatedBy), LastChangedUtc = ISNULL(s.UpdatedUtc, s.CreatedUtc)
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    ORDER BY ISNULL(s.UpdatedUtc, s.CreatedUtc) DESC, s.StyleId DESC;

    /* 9. The last committed import (none: no row). */
    SELECT TOP (1) BatchId, FileName, FinishedBy, FinishedUtc
    FROM staging.ImportBatch
    WHERE Status = 'Committed'
    ORDER BY FinishedUtc DESC, BatchId DESC;
END
GO

/* ---------- Shared helpers for writes ---------- */

/* Adds a season typed as 2028-FW if its term is known; THROWs otherwise. */
CREATE OR ALTER PROCEDURE style.usp_EnsureSeason
    @SeasonCode nvarchar(16)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM ref.Season WHERE SeasonCode = @SeasonCode) RETURN;
    IF @SeasonCode IS NULL OR NOT REGEXP_LIKE(@SeasonCode, N'^[0-9]{4}-[A-Z]{2,4}$')
       OR NOT EXISTS (SELECT 1 FROM ref.SeasonTerm WHERE Term = SUBSTRING(@SeasonCode, 6, 8))
    BEGIN
        DECLARE @msg nvarchar(400) = CONCAT(N'Season "', @SeasonCode, N'" must look like 2027-SS (year, dash, ',
            (SELECT STRING_AGG(Term, N'/') WITHIN GROUP (ORDER BY SortOrder) FROM ref.SeasonTerm), N').');
        THROW 50400, @msg, 1;
    END
    INSERT ref.Season (SeasonCode, SeasonYear, Term) VALUES (@SeasonCode, CAST(LEFT(@SeasonCode, 4) AS smallint), SUBSTRING(@SeasonCode, 6, 8));
END
GO

/* Suffix of a style number relative to its base ('S2508MR1212_SS27' -> 'SS27'). */
CREATE OR ALTER FUNCTION style.fn_Suffix (@StyleNo nvarchar(40))
RETURNS nvarchar(40)
AS
BEGIN
    RETURN IIF(CHARINDEX(N'_', @StyleNo) > 1, SUBSTRING(@StyleNo, CHARINDEX(N'_', @StyleNo) + 1, 40), NULL);
END
GO

/*
    Links versions named by adding letters to another style's number ('S2808MR0000A', 'S2608WR0201IN' from
    'S2808MR0000', 'S2608WR0201'), whatever their model: Variant in the same season, CarryOver in a later one.
    The root is the longest number of the same customer that the version extends by 1-3 letters, in the same or an
    earlier season. Only styles without any link are linked (LinkSource 'Auto'), so import and hand-set links win.
    Runs over the whole library (fast: a self-join on the customer's numbers). @LinksAdded returns how many were added.
*/
CREATE OR ALTER PROCEDURE style.usp_StyleHistory_LinkByNumber
    @ChangedBy  nvarchar(64),
    @LinksAdded int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    WITH s AS (
        SELECT st.StyleId, st.CustomerId, st.StyleNo, st.SeasonCode, OrderKey = se.SeasonYear * 100 + t.SortOrder
        FROM style.Style st
        JOIN ref.Season se ON se.SeasonCode = st.SeasonCode
        JOIN ref.SeasonTerm t ON t.Term = se.Term
    ),
    candidates AS (
        SELECT v.StyleId, RootId = r.StyleId,
               Relation = IIF(r.SeasonCode = v.SeasonCode, 'Variant', 'CarryOver'),
               Suffix = SUBSTRING(v.StyleNo, LEN(r.StyleNo) + 1, 40),
               Pick = ROW_NUMBER() OVER (PARTITION BY v.StyleId ORDER BY LEN(r.StyleNo) DESC, r.OrderKey DESC, r.StyleId)
        FROM s v
        JOIN s r ON r.CustomerId = v.CustomerId AND r.StyleId <> v.StyleId
                AND LEN(r.StyleNo) BETWEEN LEN(v.StyleNo) - 3 AND LEN(v.StyleNo) - 1
                AND LEFT(v.StyleNo, LEN(r.StyleNo)) = r.StyleNo
                AND SUBSTRING(v.StyleNo, LEN(r.StyleNo) + 1, 40) NOT LIKE N'%[^A-Za-z]%'
                AND r.OrderKey <= v.OrderKey
        WHERE NOT EXISTS (SELECT 1 FROM style.StyleHistory h WHERE h.StyleId = v.StyleId)
    )
    INSERT style.StyleHistory (StyleId, SourceStyleId, Relation, Suffix, LinkSource, CreatedBy)
    SELECT StyleId, RootId, Relation, Suffix, 'Auto', @ChangedBy
    FROM candidates WHERE Pick = 1;

    SET @LinksAdded = @@ROWCOUNT;
END
GO

/* ---------- Styles ---------- */

/* Creates (@StyleId NULL) or updates a style header. Returns StyleId. */
CREATE OR ALTER PROCEDURE style.usp_Style_Save
    @StyleId             int           = NULL,
    @RowVer              binary(8)     = NULL,
    @CustomerCode        nvarchar(32),
    @SeasonCode          nvarchar(16),
    @StyleNo             nvarchar(40),
    @Description         nvarchar(400) = NULL,
    @ModelCode           nvarchar(60)  = NULL,
    @ModelName           nvarchar(100) = NULL,
    @WeaveTypeCode       nvarchar(8)   = NULL,
    @ProductTypeCode     nvarchar(40)  = NULL,
    @Gender              nvarchar(16)  = NULL,
    @GarmentLeadTimeDays smallint      = NULL,
    @BusinessUnitCode    nvarchar(16)  = NULL,
    @SketchUrl           nvarchar(500) = NULL,
    @ImageUrl            nvarchar(500) = NULL,
    @IsActive            bit           = 1,
    @ChangedBy           nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF @StyleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM style.Style WITH (UPDLOCK) WHERE StyleId = @StyleId)
        THROW 50404, 'This style no longer exists.', 1;
    IF @StyleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM style.Style WHERE StyleId = @StyleId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this style after you opened it. Reload and try again.', 1;
    IF @WeaveTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.WeaveType WHERE WeaveTypeCode = @WeaveTypeCode)
        THROW 50400, 'Choose a weave type from the list.', 1;

    EXEC style.usp_EnsureSeason @SeasonCode;
    IF NOT EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode)
        INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy) VALUES ('Customer', @CustomerCode, @CustomerCode, @ChangedBy);
    IF @ProductTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ProductType WHERE ProductTypeCode = @ProductTypeCode)
        INSERT ref.ProductType (ProductTypeCode, Name) VALUES (@ProductTypeCode, @ProductTypeCode);
    IF @BusinessUnitCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.BusinessUnit WHERE BusinessUnitCode = @BusinessUnitCode)
        INSERT ref.BusinessUnit (BusinessUnitCode, Name) VALUES (@BusinessUnitCode, @BusinessUnitCode);

    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);

    IF EXISTS (SELECT 1 FROM style.Style WHERE CustomerId = @customerId AND SeasonCode = @SeasonCode AND StyleNo = @StyleNo
               AND (@StyleId IS NULL OR StyleId <> @StyleId))
        THROW 50409, 'A style with this number already exists for this customer and season.', 1;

    IF @StyleId IS NULL
    BEGIN
        INSERT style.Style (CustomerId, SeasonCode, StyleNo, Description, ModelCode, ModelName, WeaveTypeCode, ProductTypeCode, Gender,
                            GarmentLeadTimeDays, BusinessUnitCode, SketchUrl, ImageUrl, IsActive, CreatedBy)
        VALUES (@customerId, @SeasonCode, @StyleNo, @Description, @ModelCode, @ModelName, @WeaveTypeCode, @ProductTypeCode, @Gender,
                @GarmentLeadTimeDays, @BusinessUnitCode, @SketchUrl, @ImageUrl, ISNULL(@IsActive, 1), @ChangedBy);
        SET @StyleId = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
        UPDATE style.Style SET
            CustomerId = @customerId, SeasonCode = @SeasonCode, StyleNo = @StyleNo, Description = @Description,
            ModelCode = @ModelCode, ModelName = @ModelName, WeaveTypeCode = @WeaveTypeCode, ProductTypeCode = @ProductTypeCode,
            Gender = @Gender, GarmentLeadTimeDays = @GarmentLeadTimeDays, BusinessUnitCode = @BusinessUnitCode,
            SketchUrl = @SketchUrl, ImageUrl = @ImageUrl, IsActive = ISNULL(@IsActive, 1), UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        WHERE StyleId = @StyleId;

    -- A number like 'S2808MR0000A' is a version of 'S2808MR0000' (and the reverse when the root is added later).
    EXEC style.usp_StyleHistory_LinkByNumber @ChangedBy;

    COMMIT TRANSACTION;
    SELECT StyleId = @StyleId;
END
GO

/*
    Deletes a style with its colorways and BOM. Styles that were reused from it are re-linked to the style it came
    from (so a family's history stays connected), or unlinked when it had none.
*/
CREATE OR ALTER PROCEDURE style.usp_Style_Delete
    @StyleId   int,
    @RowVer    binary(8),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM style.Style WITH (UPDLOCK) WHERE StyleId = @StyleId)
        THROW 50404, 'This style no longer exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM style.Style WHERE StyleId = @StyleId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this style after you opened it. Reload and try again.', 1;

    DECLARE @source int = (SELECT SourceStyleId FROM style.StyleHistory WHERE StyleId = @StyleId);
    IF @source IS NULL
        DELETE style.StyleHistory WHERE SourceStyleId = @StyleId;
    ELSE
        UPDATE style.StyleHistory SET SourceStyleId = @source, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        WHERE SourceStyleId = @StyleId;
    DELETE style.StyleHistory WHERE StyleId = @StyleId;

    DELETE blc FROM style.BomLineColorway blc JOIN style.BomLine bl ON bl.BomLineId = blc.BomLineId WHERE bl.StyleId = @StyleId;
    DELETE style.BomLine WHERE StyleId = @StyleId;
    DELETE style.Colorway WHERE StyleId = @StyleId;
    DELETE style.Style WHERE StyleId = @StyleId;
    COMMIT TRANSACTION;
END
GO

/*
    Reuses a style in another season (or as a variant in the same season): copies the header, colorways (with images),
    BOM lines and their colorway colours to a new style number, and links it to the original (LinkSource Manual).
    Returns the new StyleId.
*/
CREATE OR ALTER PROCEDURE style.usp_Style_Copy
    @StyleId    int,
    @SeasonCode nvarchar(16),
    @StyleNo    nvarchar(40),
    @ChangedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM style.Style WHERE StyleId = @StyleId)
        THROW 50404, 'This style no longer exists.', 1;
    EXEC style.usp_EnsureSeason @SeasonCode;
    IF EXISTS (SELECT 1 FROM style.Style o JOIN style.Style n ON n.CustomerId = o.CustomerId
               WHERE o.StyleId = @StyleId AND n.SeasonCode = @SeasonCode AND n.StyleNo = @StyleNo)
        THROW 50409, 'A style with this number already exists for this customer and season.', 1;

    INSERT style.Style (CustomerId, SeasonCode, StyleNo, Description, ModelCode, ModelName, WeaveTypeCode, ProductTypeCode, Gender,
                        GarmentLeadTimeDays, BusinessUnitCode, SketchUrl, ImageUrl, CreatedBy)
    SELECT CustomerId, @SeasonCode, @StyleNo, Description, ModelCode, ModelName, WeaveTypeCode, ProductTypeCode, Gender,
           GarmentLeadTimeDays, BusinessUnitCode, SketchUrl, ImageUrl, @ChangedBy
    FROM style.Style WHERE StyleId = @StyleId;
    DECLARE @newId int = CAST(SCOPE_IDENTITY() AS int);

    INSERT style.Colorway (StyleId, SortOrder, ColorwayCode, ColorwayName, Status, ImageUrl, CreatedBy)
    SELECT @newId, SortOrder, ColorwayCode, ColorwayName, Status, ImageUrl, @ChangedBy FROM style.Colorway WHERE StyleId = @StyleId;

    INSERT style.BomLine (StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
                          NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ImageUrl, CreatedBy)
    SELECT @newId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
           NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ImageUrl, @ChangedBy
    FROM style.BomLine WHERE StyleId = @StyleId;

    INSERT style.BomLineColorway (BomLineId, ColorwayId, MaterialColorCode, MaterialColorDescription)
    SELECT nl.BomLineId, nc.ColorwayId, blc.MaterialColorCode, blc.MaterialColorDescription
    FROM style.BomLineColorway blc
    JOIN style.BomLine ol ON ol.BomLineId = blc.BomLineId AND ol.StyleId = @StyleId
    JOIN style.Colorway oc ON oc.ColorwayId = blc.ColorwayId
    JOIN style.BomLine nl ON nl.StyleId = @newId AND nl.LineSeq = ol.LineSeq
    JOIN style.Colorway nc ON nc.StyleId = @newId AND nc.ColorwayCode = oc.ColorwayCode;

    INSERT style.StyleHistory (StyleId, SourceStyleId, Relation, Suffix, LinkSource, CreatedBy)
    SELECT @newId, @StyleId, IIF(SeasonCode = @SeasonCode, 'Variant', 'CarryOver'), style.fn_Suffix(@StyleNo), 'Manual', @ChangedBy
    FROM style.Style WHERE StyleId = @StyleId;

    COMMIT TRANSACTION;
    SELECT StyleId = @newId;
END
GO

/* Marks the style as changed (who / when) when its colorways or BOM change. */
CREATE OR ALTER PROCEDURE style.usp_Style_Touch
    @StyleId   int,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE style.Style SET UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME() WHERE StyleId = @StyleId;
END
GO

/* ---------- Colorways ---------- */

/* Creates (@ColorwayId NULL) or updates a colorway. Returns ColorwayId. */
CREATE OR ALTER PROCEDURE style.usp_Colorway_Save
    @ColorwayId   int           = NULL,
    @StyleId      int,
    @RowVer       binary(8)     = NULL,
    @ColorwayCode nvarchar(20),
    @ColorwayName nvarchar(150) = NULL,
    @Status       varchar(10)   = 'INRANGE',
    @SortOrder    smallint      = NULL,
    @ImageUrl     nvarchar(500) = NULL,
    @ChangedBy    nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM style.Style WITH (UPDLOCK) WHERE StyleId = @StyleId)
        THROW 50404, 'This style no longer exists.', 1;
    IF @ColorwayId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM style.Colorway WHERE ColorwayId = @ColorwayId AND StyleId = @StyleId)
        THROW 50404, 'This colorway no longer exists.', 1;
    IF @ColorwayId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM style.Colorway WHERE ColorwayId = @ColorwayId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this colorway after you opened it. Reload and try again.', 1;
    IF EXISTS (SELECT 1 FROM style.Colorway WHERE StyleId = @StyleId AND ColorwayCode = @ColorwayCode AND (@ColorwayId IS NULL OR ColorwayId <> @ColorwayId))
        THROW 50409, 'This style already has a colorway with this code.', 1;

    SET @SortOrder = ISNULL(@SortOrder, (SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM style.Colorway WHERE StyleId = @StyleId));

    IF @ColorwayId IS NULL
    BEGIN
        INSERT style.Colorway (StyleId, SortOrder, ColorwayCode, ColorwayName, Status, ImageUrl, CreatedBy)
        VALUES (@StyleId, @SortOrder, @ColorwayCode, @ColorwayName, @Status, @ImageUrl, @ChangedBy);
        SET @ColorwayId = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
        UPDATE style.Colorway SET ColorwayCode = @ColorwayCode, ColorwayName = @ColorwayName, Status = @Status, SortOrder = @SortOrder,
                                  ImageUrl = @ImageUrl, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        WHERE ColorwayId = @ColorwayId;

    EXEC style.usp_Style_Touch @StyleId, @ChangedBy;
    COMMIT TRANSACTION;
    SELECT ColorwayId = @ColorwayId;
END
GO

/* Deletes a colorway; BOM lines stay, without its colour. */
CREATE OR ALTER PROCEDURE style.usp_Colorway_Delete
    @ColorwayId int,
    @RowVer     binary(8),
    @ChangedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @styleId int = (SELECT StyleId FROM style.Colorway WITH (UPDLOCK) WHERE ColorwayId = @ColorwayId);
    IF @styleId IS NULL
        THROW 50404, 'This colorway no longer exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM style.Colorway WHERE ColorwayId = @ColorwayId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this colorway after you opened it. Reload and try again.', 1;
    DELETE style.BomLineColorway WHERE ColorwayId = @ColorwayId;
    DELETE style.Colorway WHERE ColorwayId = @ColorwayId;
    EXEC style.usp_Style_Touch @styleId, @ChangedBy;
    COMMIT TRANSACTION;
END
GO

/* ---------- BOM lines ---------- */

/*
    Creates (@BomLineId NULL) or updates a BOM line. @Colorways is the full list of colorways using the line, as
    json: [{"colorwayId": 1, "materialColorCode": "...", "materialColorDescription": "..."}]. Returns BomLineId.
*/
CREATE OR ALTER PROCEDURE style.usp_BomLine_Save
    @BomLineId             int            = NULL,
    @StyleId               int,
    @RowVer                binary(8)      = NULL,
    @PartNo                int            = NULL,
    @MaterialCode          nvarchar(64),
    @MaterialDescription   nvarchar(4000) = NULL,
    @MaterialTypeCode      nvarchar(16)   = NULL,
    @ContentClassCode      nvarchar(8)    = NULL,
    @NominatedSupplierCode nvarchar(32)   = NULL,
    @NominatedSupplierName nvarchar(100)  = NULL,
    @SupplierCode          nvarchar(32)   = NULL,
    @SupplierName          nvarchar(150)  = NULL,
    @LcoConsumption        decimal(18, 6) = NULL,
    @BrandConsumption      decimal(18, 6) = NULL,
    @UomCode               nvarchar(8)    = NULL,
    @ImageUrl              nvarchar(500)  = NULL,
    @Colorways             nvarchar(max)  = N'[]',
    @ChangedBy             nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM style.Style WITH (UPDLOCK) WHERE StyleId = @StyleId)
        THROW 50404, 'This style no longer exists.', 1;
    IF @BomLineId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM style.BomLine WHERE BomLineId = @BomLineId AND StyleId = @StyleId)
        THROW 50404, 'This BOM line no longer exists.', 1;
    IF @BomLineId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM style.BomLine WHERE BomLineId = @BomLineId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this BOM line after you opened it. Reload and try again.', 1;
    IF @ContentClassCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.ContentClass WHERE ContentClassCode = @ContentClassCode)
        THROW 50400, 'Choose a content class from the list.', 1;
    IF ISJSON(@Colorways) = 0
        THROW 50400, 'The colorways must be a JSON array.', 1;

    SELECT ColorwayId, MaterialColorCode = NULLIF(TRIM(MaterialColorCode), N''), MaterialColorDescription = NULLIF(TRIM(MaterialColorDescription), N'')
    INTO #cw
    FROM OPENJSON(@Colorways) WITH (ColorwayId int '$.colorwayId', MaterialColorCode nvarchar(80) '$.materialColorCode',
                                    MaterialColorDescription nvarchar(150) '$.materialColorDescription');
    IF EXISTS (SELECT 1 FROM #cw c WHERE NOT EXISTS (SELECT 1 FROM style.Colorway k WHERE k.ColorwayId = c.ColorwayId AND k.StyleId = @StyleId))
        THROW 50400, 'A colorway on this line does not belong to the style. Reload and try again.', 1;
    IF EXISTS (SELECT 1 FROM #cw GROUP BY ColorwayId HAVING COUNT(*) > 1)
        THROW 50400, 'A colorway is listed twice on this line.', 1;

    SET @UomCode = LOWER(@UomCode);
    IF @UomCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.Uom WHERE UomCode = @UomCode)
        INSERT ref.Uom (UomCode, Name) VALUES (@UomCode, @UomCode);
    IF @MaterialTypeCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ref.MaterialType WHERE MaterialTypeCode = @MaterialTypeCode)
        INSERT ref.MaterialType (MaterialTypeCode, Name) VALUES (@MaterialTypeCode, @MaterialTypeCode);
    IF NOT EXISTS (SELECT 1 FROM mat.Material WHERE MaterialCode = @MaterialCode)
        INSERT mat.Material (MaterialCode, Description, MaterialTypeCode, ContentClassCode, CreatedBy)
        VALUES (@MaterialCode, @MaterialDescription, @MaterialTypeCode, @ContentClassCode, @ChangedBy);
    IF @SupplierCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = @SupplierCode)
        INSERT partner.Partner (PartnerType, PartnerCode, Name, CreatedBy) VALUES ('Supplier', @SupplierCode, ISNULL(@SupplierName, @SupplierCode), @ChangedBy);

    DECLARE @materialId int = (SELECT MaterialId FROM mat.Material WHERE MaterialCode = @MaterialCode);
    DECLARE @supplierId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = @SupplierCode);

    IF @BomLineId IS NULL
    BEGIN
        INSERT style.BomLine (StyleId, LineSeq, PartNo, MaterialId, MaterialDescription, MaterialTypeCode, ContentClassCode, NominatedSupplierCode,
                              NominatedSupplierName, SupplierId, LcoConsumption, BrandConsumption, UomCode, ImageUrl, CreatedBy)
        VALUES (@StyleId, (SELECT ISNULL(MAX(LineSeq), 0) + 1 FROM style.BomLine WHERE StyleId = @StyleId), @PartNo, @materialId,
                @MaterialDescription, @MaterialTypeCode, @ContentClassCode, @NominatedSupplierCode, @NominatedSupplierName, @supplierId,
                @LcoConsumption, @BrandConsumption, @UomCode, @ImageUrl, @ChangedBy);
        SET @BomLineId = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
        UPDATE style.BomLine SET
            PartNo = @PartNo, MaterialId = @materialId, MaterialDescription = @MaterialDescription, MaterialTypeCode = @MaterialTypeCode,
            ContentClassCode = @ContentClassCode, NominatedSupplierCode = @NominatedSupplierCode, NominatedSupplierName = @NominatedSupplierName,
            SupplierId = @supplierId, LcoConsumption = @LcoConsumption, BrandConsumption = @BrandConsumption, UomCode = @UomCode,
            ImageUrl = @ImageUrl, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        WHERE BomLineId = @BomLineId;

    DELETE style.BomLineColorway WHERE BomLineId = @BomLineId;
    INSERT style.BomLineColorway (BomLineId, ColorwayId, MaterialColorCode, MaterialColorDescription)
    SELECT @BomLineId, ColorwayId, MaterialColorCode, MaterialColorDescription FROM #cw;

    EXEC style.usp_Style_Touch @StyleId, @ChangedBy;
    COMMIT TRANSACTION;
    SELECT BomLineId = @BomLineId;
END
GO

CREATE OR ALTER PROCEDURE style.usp_BomLine_Delete
    @BomLineId int,
    @RowVer    binary(8),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @styleId int = (SELECT StyleId FROM style.BomLine WITH (UPDLOCK) WHERE BomLineId = @BomLineId);
    IF @styleId IS NULL
        THROW 50404, 'This BOM line no longer exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM style.BomLine WHERE BomLineId = @BomLineId AND RowVer = @RowVer)
        THROW 50409, 'Someone else changed this BOM line after you opened it. Reload and try again.', 1;
    DELETE style.BomLineColorway WHERE BomLineId = @BomLineId;
    DELETE style.BomLine WHERE BomLineId = @BomLineId;
    EXEC style.usp_Style_Touch @styleId, @ChangedBy;
    COMMIT TRANSACTION;
END
GO

/* ---------- Style history (set by hand) ---------- */

/*
    Records that @StyleId was reused from @SourceStyleId (replacing any link it had). Refuses links that would make
    a loop. Links set here are never replaced by an import.
*/
CREATE OR ALTER PROCEDURE style.usp_StyleHistory_Set
    @StyleId       int,
    @SourceStyleId int,
    @Relation      varchar(10),
    @Note          nvarchar(400) = NULL,
    @ChangedBy     nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @StyleId = @SourceStyleId
        THROW 50400, 'A style cannot be reused from itself.', 1;
    IF @Relation NOT IN ('CarryOver', 'Variant')
        THROW 50400, 'Choose Carry-over or Variant.', 1;

    BEGIN TRANSACTION;
    IF (SELECT COUNT(*) FROM style.Style WITH (UPDLOCK) WHERE StyleId IN (@StyleId, @SourceStyleId)) < 2
        THROW 50404, 'One of the styles no longer exists.', 1;

    /* Walk back from the source: reaching @StyleId would make a loop. */
    WITH chain AS (
        SELECT StyleId = @SourceStyleId, Depth = 0
        UNION ALL
        SELECT h.SourceStyleId, c.Depth + 1 FROM style.StyleHistory h JOIN chain c ON c.StyleId = h.StyleId WHERE c.Depth < 100
    )
    SELECT 1 AS x INTO #loop FROM chain WHERE StyleId = @StyleId;
    IF EXISTS (SELECT 1 FROM #loop)
        THROW 50400, 'That link would make a loop: the other style already comes from this one.', 1;

    DELETE style.StyleHistory WHERE StyleId = @StyleId;
    INSERT style.StyleHistory (StyleId, SourceStyleId, Relation, Suffix, LinkSource, Note, CreatedBy)
    SELECT @StyleId, @SourceStyleId, @Relation, style.fn_Suffix(StyleNo), 'Manual', NULLIF(TRIM(@Note), N''), @ChangedBy
    FROM style.Style WHERE StyleId = @StyleId;

    EXEC style.usp_Style_Touch @StyleId, @ChangedBy;
    COMMIT TRANSACTION;
END
GO

/* Removes the link from a style to the one it was reused from. (A later import may link styles of the same family again.) */
CREATE OR ALTER PROCEDURE style.usp_StyleHistory_Remove
    @StyleId   int,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DELETE style.StyleHistory WHERE StyleId = @StyleId;
    IF @@ROWCOUNT > 0 EXEC style.usp_Style_Touch @StyleId, @ChangedBy;
    COMMIT TRANSACTION;
END
GO

PRINT 'Style procedures ready.';
GO
