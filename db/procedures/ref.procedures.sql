/*
    LT ODM Style Library - reference list maintenance (Settings > Reference lists, /api/v1/admin/ref-lists/{list})

    One set of procedures for every pick list on the Styles screens. @List picks the table (static SQL per list):

      @List           table                          code                 name           extra            used by
      contentClasses  ref.ContentClass               upper-case           Name           SortOrder        materials, BOM lines
      materialTypes   ref.MaterialType               upper-case           Name                            materials, BOM lines
      uoms            ref.Uom                        lower-case           Name                            BOM lines
      weaveTypes      ref.WeaveType                  upper-case           Name                            styles
      productTypes    ref.ProductType                as typed             Name           IsActive         styles
      businessUnits   ref.BusinessUnit               as typed             Name           IsActive         styles
      seasons         ref.Season                     '2027-SS'            Description    IsActive         styles
      seasonTerms     ref.SeasonTerm                 2-4 letters          Name           SortOrder        seasons
      customers       partner.Partner (Customer)     as typed             Name           IsActive         styles
      suppliers       partner.Partner (Supplier)     as typed             Name           IsActive         BOM lines

    Codes are the keys styles, BOM lines and the workbook import use, so they cannot change once created (the API
    applies the case rules above). A row still in use cannot be deleted: make it inactive instead where the list
    has IsActive (inactive rows are left out of the pick lists, existing styles keep them).
    Rule errors are THROWn as 50000+ with a message for the user. Requires style.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\ref.procedures.sql
*/
SET NOCOUNT ON;
GO

-- Replaced by the ref.usp_RefList_* procedures below.
DROP PROCEDURE IF EXISTS ref.usp_ContentClass_List;
DROP PROCEDURE IF EXISTS ref.usp_ContentClass_Save;
DROP PROCEDURE IF EXISTS ref.usp_ContentClass_Delete;
GO

/* Every row of one list: Code, Name, SortOrder (or NULL), IsActive (or NULL), UsageCount. */
CREATE OR ALTER PROCEDURE ref.usp_RefList_Get
    @List varchar(32)
AS
BEGIN
    SET NOCOUNT ON;

    IF @List = 'contentClasses'
        SELECT Code = x.ContentClassCode, x.Name, SortOrder = CAST(x.SortOrder AS int), IsActive = CAST(NULL AS bit),
               UsageCount = (SELECT COUNT(*) FROM mat.Material m WHERE m.ContentClassCode = x.ContentClassCode)
                          + (SELECT COUNT(*) FROM style.BomLine b WHERE b.ContentClassCode = x.ContentClassCode)
        FROM ref.ContentClass x ORDER BY x.SortOrder, x.ContentClassCode;
    ELSE IF @List = 'materialTypes'
        SELECT Code = x.MaterialTypeCode, x.Name, SortOrder = CAST(NULL AS int), IsActive = CAST(NULL AS bit),
               UsageCount = (SELECT COUNT(*) FROM mat.Material m WHERE m.MaterialTypeCode = x.MaterialTypeCode)
                          + (SELECT COUNT(*) FROM style.BomLine b WHERE b.MaterialTypeCode = x.MaterialTypeCode)
        FROM ref.MaterialType x ORDER BY x.MaterialTypeCode;
    ELSE IF @List = 'uoms'
        SELECT Code = x.UomCode, x.Name, SortOrder = CAST(NULL AS int), IsActive = CAST(NULL AS bit),
               UsageCount = (SELECT COUNT(*) FROM style.BomLine b WHERE b.UomCode = x.UomCode)
        FROM ref.Uom x ORDER BY x.UomCode;
    ELSE IF @List = 'weaveTypes'
        SELECT Code = x.WeaveTypeCode, x.Name, SortOrder = CAST(NULL AS int), IsActive = CAST(NULL AS bit),
               UsageCount = (SELECT COUNT(*) FROM style.Style s WHERE s.WeaveTypeCode = x.WeaveTypeCode)
        FROM ref.WeaveType x ORDER BY x.WeaveTypeCode;
    ELSE IF @List = 'productTypes'
        SELECT Code = x.ProductTypeCode, x.Name, SortOrder = CAST(NULL AS int), x.IsActive,
               UsageCount = (SELECT COUNT(*) FROM style.Style s WHERE s.ProductTypeCode = x.ProductTypeCode)
        FROM ref.ProductType x ORDER BY x.ProductTypeCode;
    ELSE IF @List = 'businessUnits'
        SELECT Code = x.BusinessUnitCode, x.Name, SortOrder = CAST(NULL AS int), x.IsActive,
               UsageCount = (SELECT COUNT(*) FROM style.Style s WHERE s.BusinessUnitCode = x.BusinessUnitCode)
        FROM ref.BusinessUnit x ORDER BY x.BusinessUnitCode;
    ELSE IF @List = 'seasons'
        SELECT Code = x.SeasonCode, Name = x.Description, SortOrder = CAST(NULL AS int), x.IsActive,
               UsageCount = (SELECT COUNT(*) FROM style.Style s WHERE s.SeasonCode = x.SeasonCode)
        FROM ref.Season x JOIN ref.SeasonTerm t ON t.Term = x.Term
        ORDER BY x.SeasonYear DESC, t.SortOrder DESC;
    ELSE IF @List = 'seasonTerms'
        SELECT Code = x.Term, x.Name, SortOrder = CAST(x.SortOrder AS int), IsActive = CAST(NULL AS bit),
               UsageCount = (SELECT COUNT(*) FROM ref.Season s WHERE s.Term = x.Term)
        FROM ref.SeasonTerm x ORDER BY x.SortOrder;
    ELSE IF @List = 'customers'
        SELECT Code = x.PartnerCode, x.Name, SortOrder = CAST(NULL AS int), x.IsActive,
               UsageCount = (SELECT COUNT(*) FROM style.Style s WHERE s.CustomerId = x.PartnerId)
        FROM partner.Partner x WHERE x.PartnerType = 'Customer' ORDER BY x.PartnerCode;
    ELSE IF @List = 'suppliers'
        SELECT Code = x.PartnerCode, x.Name, SortOrder = CAST(NULL AS int), x.IsActive,
               UsageCount = (SELECT COUNT(*) FROM style.BomLine b WHERE b.SupplierId = x.PartnerId)
        FROM partner.Partner x WHERE x.PartnerType = 'Supplier' ORDER BY x.PartnerCode;
    ELSE
        THROW 50070, 'Unknown reference list.', 1;
END
GO

/*
    Insert (@IsNew = 1) or update one row. The code itself never changes; @SortOrder and @IsActive are ignored by
    lists without them. Single-statement writes: the primary and unique keys still refuse a duplicate that slips
    past the checks.
*/
CREATE OR ALTER PROCEDURE ref.usp_RefList_Save
    @List       varchar(32),
    @IsNew      bit,
    @Code       nvarchar(40),
    @Name       nvarchar(150),
    @SortOrder  int = NULL,
    @IsActive   bit = NULL,
    @ChangedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @exists bit, @partnerType varchar(16) = CASE @List WHEN 'customers' THEN 'Customer' WHEN 'suppliers' THEN 'Supplier' END;
    SET @IsActive = ISNULL(@IsActive, 1);

    SET @exists = CASE
        WHEN @List = 'contentClasses' THEN IIF(EXISTS (SELECT 1 FROM ref.ContentClass WHERE ContentClassCode = @Code), 1, 0)
        WHEN @List = 'materialTypes'  THEN IIF(EXISTS (SELECT 1 FROM ref.MaterialType WHERE MaterialTypeCode = @Code), 1, 0)
        WHEN @List = 'uoms'           THEN IIF(EXISTS (SELECT 1 FROM ref.Uom WHERE UomCode = @Code), 1, 0)
        WHEN @List = 'weaveTypes'     THEN IIF(EXISTS (SELECT 1 FROM ref.WeaveType WHERE WeaveTypeCode = @Code), 1, 0)
        WHEN @List = 'productTypes'   THEN IIF(EXISTS (SELECT 1 FROM ref.ProductType WHERE ProductTypeCode = @Code), 1, 0)
        WHEN @List = 'businessUnits'  THEN IIF(EXISTS (SELECT 1 FROM ref.BusinessUnit WHERE BusinessUnitCode = @Code), 1, 0)
        WHEN @List = 'seasons'        THEN IIF(EXISTS (SELECT 1 FROM ref.Season WHERE SeasonCode = @Code), 1, 0)
        WHEN @List = 'seasonTerms'    THEN IIF(EXISTS (SELECT 1 FROM ref.SeasonTerm WHERE Term = @Code), 1, 0)
        WHEN @partnerType IS NOT NULL THEN IIF(EXISTS (SELECT 1 FROM partner.Partner WHERE PartnerType = @partnerType AND PartnerCode = @Code), 1, 0)
    END;
    IF @exists IS NULL THROW 50070, 'Unknown reference list.', 1;
    IF @IsNew = 1 AND @exists = 1 THROW 50071, 'This code already exists in the list.', 1;
    IF @IsNew = 0 AND @exists = 0 THROW 50072, 'This code was not found. It may have been deleted; reload the list.', 1;

    IF @List = 'seasons' AND @IsNew = 1
       AND NOT EXISTS (SELECT 1 FROM ref.SeasonTerm WHERE Term = SUBSTRING(@Code, 6, 8))
        THROW 50073, 'The season term after the year is not in the Season terms list. Add the term first.', 1;
    IF @List = 'seasonTerms'
       AND EXISTS (SELECT 1 FROM ref.SeasonTerm WHERE SortOrder = @SortOrder AND Term <> @Code)
        THROW 50074, 'Another season term already has this sort order.', 1;

    IF @List = 'contentClasses'
    BEGIN
        IF @IsNew = 1 INSERT ref.ContentClass (ContentClassCode, Name, SortOrder) VALUES (@Code, @Name, @SortOrder);
        ELSE UPDATE ref.ContentClass SET Name = @Name, SortOrder = @SortOrder WHERE ContentClassCode = @Code;
    END
    ELSE IF @List = 'materialTypes'
    BEGIN
        IF @IsNew = 1 INSERT ref.MaterialType (MaterialTypeCode, Name) VALUES (@Code, @Name);
        ELSE UPDATE ref.MaterialType SET Name = @Name WHERE MaterialTypeCode = @Code;
    END
    ELSE IF @List = 'uoms'
    BEGIN
        IF @IsNew = 1 INSERT ref.Uom (UomCode, Name) VALUES (@Code, @Name);
        ELSE UPDATE ref.Uom SET Name = @Name WHERE UomCode = @Code;
    END
    ELSE IF @List = 'weaveTypes'
    BEGIN
        IF @IsNew = 1 INSERT ref.WeaveType (WeaveTypeCode, Name) VALUES (@Code, @Name);
        ELSE UPDATE ref.WeaveType SET Name = @Name WHERE WeaveTypeCode = @Code;
    END
    ELSE IF @List = 'productTypes'
    BEGIN
        IF @IsNew = 1 INSERT ref.ProductType (ProductTypeCode, Name, IsActive) VALUES (@Code, @Name, @IsActive);
        ELSE UPDATE ref.ProductType SET Name = @Name, IsActive = @IsActive WHERE ProductTypeCode = @Code;
    END
    ELSE IF @List = 'businessUnits'
    BEGIN
        IF @IsNew = 1 INSERT ref.BusinessUnit (BusinessUnitCode, Name, IsActive) VALUES (@Code, @Name, @IsActive);
        ELSE UPDATE ref.BusinessUnit SET Name = @Name, IsActive = @IsActive WHERE BusinessUnitCode = @Code;
    END
    ELSE IF @List = 'seasons'
    BEGIN
        IF @IsNew = 1
            INSERT ref.Season (SeasonCode, SeasonYear, Term, Description, IsActive)
            VALUES (@Code, CAST(LEFT(@Code, 4) AS smallint), SUBSTRING(@Code, 6, 8), NULLIF(@Name, N''), @IsActive);
        ELSE UPDATE ref.Season SET Description = NULLIF(@Name, N''), IsActive = @IsActive WHERE SeasonCode = @Code;
    END
    ELSE IF @List = 'seasonTerms'
    BEGIN
        IF @IsNew = 1 INSERT ref.SeasonTerm (Term, Name, SortOrder) VALUES (@Code, @Name, @SortOrder);
        ELSE UPDATE ref.SeasonTerm SET Name = @Name, SortOrder = @SortOrder WHERE Term = @Code;
    END
    ELSE
    BEGIN
        IF @IsNew = 1
            INSERT partner.Partner (PartnerType, PartnerCode, Name, IsActive, CreatedBy) VALUES (@partnerType, @Code, @Name, @IsActive, @ChangedBy);
        ELSE
            UPDATE partner.Partner SET Name = @Name, IsActive = @IsActive, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
            WHERE PartnerType = @partnerType AND PartnerCode = @Code;
    END
END
GO

/* Deletes a row nothing uses. The foreign keys still refuse a delete if a row is added after the check. */
CREATE OR ALTER PROCEDURE ref.usp_RefList_Delete
    @List varchar(32),
    @Code nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @used bit, @partnerType varchar(16) = CASE @List WHEN 'customers' THEN 'Customer' WHEN 'suppliers' THEN 'Supplier' END;
    DECLARE @partnerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = @partnerType AND PartnerCode = @Code);

    SET @used = CASE
        WHEN @List = 'contentClasses' THEN IIF(EXISTS (SELECT 1 FROM mat.Material WHERE ContentClassCode = @Code)
                                               OR EXISTS (SELECT 1 FROM style.BomLine WHERE ContentClassCode = @Code), 1, 0)
        WHEN @List = 'materialTypes'  THEN IIF(EXISTS (SELECT 1 FROM mat.Material WHERE MaterialTypeCode = @Code)
                                               OR EXISTS (SELECT 1 FROM style.BomLine WHERE MaterialTypeCode = @Code), 1, 0)
        WHEN @List = 'uoms'           THEN IIF(EXISTS (SELECT 1 FROM style.BomLine WHERE UomCode = @Code), 1, 0)
        WHEN @List = 'weaveTypes'     THEN IIF(EXISTS (SELECT 1 FROM style.Style WHERE WeaveTypeCode = @Code), 1, 0)
        WHEN @List = 'productTypes'   THEN IIF(EXISTS (SELECT 1 FROM style.Style WHERE ProductTypeCode = @Code), 1, 0)
        WHEN @List = 'businessUnits'  THEN IIF(EXISTS (SELECT 1 FROM style.Style WHERE BusinessUnitCode = @Code), 1, 0)
        WHEN @List = 'seasons'        THEN IIF(EXISTS (SELECT 1 FROM style.Style WHERE SeasonCode = @Code), 1, 0)
        WHEN @List = 'seasonTerms'    THEN IIF(EXISTS (SELECT 1 FROM ref.Season WHERE Term = @Code), 1, 0)
        WHEN @List = 'customers'      THEN IIF(EXISTS (SELECT 1 FROM style.Style WHERE CustomerId = @partnerId), 1, 0)
        WHEN @List = 'suppliers'      THEN IIF(EXISTS (SELECT 1 FROM style.BomLine WHERE SupplierId = @partnerId), 1, 0)
    END;
    IF @used IS NULL THROW 50070, 'Unknown reference list.', 1;
    IF @used = 1
        THROW 50075, 'This code is still in use (see the Used by column). Change what uses it first, or make it inactive where the list allows.', 1;

    IF @List = 'contentClasses'     DELETE ref.ContentClass WHERE ContentClassCode = @Code;
    ELSE IF @List = 'materialTypes' DELETE ref.MaterialType WHERE MaterialTypeCode = @Code;
    ELSE IF @List = 'uoms'          DELETE ref.Uom WHERE UomCode = @Code;
    ELSE IF @List = 'weaveTypes'    DELETE ref.WeaveType WHERE WeaveTypeCode = @Code;
    ELSE IF @List = 'productTypes'  DELETE ref.ProductType WHERE ProductTypeCode = @Code;
    ELSE IF @List = 'businessUnits' DELETE ref.BusinessUnit WHERE BusinessUnitCode = @Code;
    ELSE IF @List = 'seasons'       DELETE ref.Season WHERE SeasonCode = @Code;
    ELSE IF @List = 'seasonTerms'   DELETE ref.SeasonTerm WHERE Term = @Code;
    ELSE                            DELETE partner.Partner WHERE PartnerId = @partnerId;
END
GO
