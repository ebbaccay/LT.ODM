/*
    LT ODM Library assistant (AI Studio > Library assistant, /api/v1/ai/library-assistant): read-only procedures for its
    tools that no other screen has. Every other tool calls the procedures of the Styles, Materials and BOM check screens.

    Reads: usp_Style_Counts

    Requires style.tables.sql and style.procedures.sql (style.fn_SearchPlaces). Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.assistant.procedures.sql
*/
SET NOCOUNT ON;
GO

/*
    Styles counted per season, customer, product type, business unit, gender or weave, for the styles matching the
    same filters as style.usp_Style_List (keep the two WHERE clauses in step). Seasons newest first, other groups
    largest first. 50400 for an unknown @GroupBy.
*/
CREATE OR ALTER PROCEDURE style.usp_Style_Counts
    @GroupBy          varchar(16),            -- season | customer | productType | businessUnit | gender | weaveType
    @Search           nvarchar(100) = NULL,
    @CustomerCode     nvarchar(32)  = NULL,
    @SeasonCode       nvarchar(200) = NULL,   -- one code, or several separated by commas
    @BusinessUnitCode nvarchar(200) = NULL,   -- one code, or several separated by commas
    @ProductTypeCode  nvarchar(400) = NULL,   -- one code, or several separated by commas
    @WeaveTypeCode    nvarchar(8)   = NULL,
    @Gender           nvarchar(16)  = NULL,
    @Material         nvarchar(100) = NULL,   -- word(s) in a BOM line's material code or description; all on the same line
    @Looks            nvarchar(400) = NULL,   -- picture tags 'attribute:value' separated by commas; a style needs all
    @IsActive         bit           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @GroupBy NOT IN ('season', 'customer', 'productType', 'businessUnit', 'gender', 'weaveType')
        THROW 50400, N'Count styles by season, customer, product type, business unit, gender or weave.', 1;

    DECLARE @found TABLE (StyleId int NOT NULL PRIMARY KEY);
    IF @Search IS NOT NULL INSERT @found SELECT DISTINCT StyleId FROM style.fn_SearchPlaces(@Search);
    DECLARE @materialWords TABLE (Pattern nvarchar(110) NOT NULL PRIMARY KEY);
    INSERT @materialWords
    SELECT DISTINCT UPPER(N'%' + REPLACE(REPLACE(REPLACE(TRIM(value), N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%')
    FROM STRING_SPLIT(@Material, N' ') WHERE TRIM(value) <> N'';
    DECLARE @lookTags TABLE (Attribute varchar(20) NOT NULL, Value varchar(30) NOT NULL, PRIMARY KEY (Attribute, Value));
    INSERT @lookTags
    SELECT DISTINCT LEFT(TRIM(LEFT(value, CHARINDEX(':', value) - 1)), 20), LEFT(TRIM(SUBSTRING(value, CHARINDEX(':', value) + 1, 60)), 30)
    FROM STRING_SPLIT(@Looks, N',') WHERE CHARINDEX(':', value) > 1;
    DECLARE @businessUnits TABLE (Code nvarchar(16) NOT NULL PRIMARY KEY);
    INSERT @businessUnits SELECT DISTINCT LEFT(TRIM(value), 16) FROM STRING_SPLIT(@BusinessUnitCode, N',') WHERE TRIM(value) <> N'';
    DECLARE @seasons TABLE (Code nvarchar(16) NOT NULL PRIMARY KEY);
    INSERT @seasons SELECT DISTINCT LEFT(TRIM(value), 16) FROM STRING_SPLIT(@SeasonCode, N',') WHERE TRIM(value) <> N'';
    DECLARE @productTypes TABLE (Code nvarchar(40) NOT NULL PRIMARY KEY);
    INSERT @productTypes SELECT DISTINCT LEFT(TRIM(value), 40) FROM STRING_SPLIT(@ProductTypeCode, N',') WHERE TRIM(value) <> N'';
    DECLARE @customerId int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Customer' AND PartnerCode = @CustomerCode);
    IF @CustomerCode IS NOT NULL AND @customerId IS NULL SET @customerId = -1;

    WITH m AS
    (
        SELECT s.StyleId, s.SeasonCode, se.SeasonYear, TermOrder = t.SortOrder, CustomerCode = c.PartnerCode, CustomerName = c.Name,
               s.ProductTypeCode, s.BusinessUnitCode, s.Gender, s.WeaveTypeCode
        FROM style.Style s
        JOIN ref.Season se ON se.SeasonCode = s.SeasonCode
        LEFT JOIN ref.SeasonTerm t ON t.Term = se.Term
        JOIN partner.Partner c ON c.PartnerId = s.CustomerId
        WHERE (@customerId IS NULL OR s.CustomerId = @customerId)
          AND (@SeasonCode IS NULL OR s.SeasonCode IN (SELECT Code FROM @seasons))
          AND (@BusinessUnitCode IS NULL OR s.BusinessUnitCode IN (SELECT Code FROM @businessUnits))
          AND (@ProductTypeCode IS NULL OR s.ProductTypeCode IN (SELECT Code FROM @productTypes))
          AND (@WeaveTypeCode IS NULL OR s.WeaveTypeCode = @WeaveTypeCode)
          AND (@Gender IS NULL OR s.Gender = @Gender)
          AND (@Looks IS NULL OR NOT EXISTS (SELECT 1 FROM @lookTags l WHERE NOT EXISTS (
                   SELECT 1 FROM style.fn_StyleTags() t WHERE t.StyleId = s.StyleId AND t.Attribute = l.Attribute AND t.Value = l.Value)))
          AND (@IsActive IS NULL OR s.IsActive = @IsActive)
          AND (@Material IS NULL OR EXISTS (
                   SELECT 1 FROM style.BomLine b JOIN mat.Material mt ON mt.MaterialId = b.MaterialId
                   WHERE b.StyleId = s.StyleId
                     AND NOT EXISTS (SELECT 1 FROM @materialWords w
                                     WHERE UPPER(b.MaterialDescription) COLLATE Latin1_General_100_BIN2 NOT LIKE w.Pattern
                                       AND UPPER(mt.MaterialCode) COLLATE Latin1_General_100_BIN2 NOT LIKE w.Pattern)))
          AND (@Search IS NULL OR s.StyleId IN (SELECT StyleId FROM @found))
    ),
    g AS
    (
        SELECT [Key] = CASE @GroupBy WHEN 'season' THEN m.SeasonCode WHEN 'customer' THEN m.CustomerCode WHEN 'productType' THEN m.ProductTypeCode
                                     WHEN 'businessUnit' THEN m.BusinessUnitCode WHEN 'gender' THEN m.Gender ELSE m.WeaveTypeCode END,
               SortYear = IIF(@GroupBy = 'season', m.SeasonYear, 0), SortTerm = IIF(@GroupBy = 'season', m.TermOrder, 0),
               CustomerName = IIF(@GroupBy = 'customer', m.CustomerName, NULL)
        FROM m
    )
    SELECT g.[Key],
           Name = CASE @GroupBy WHEN 'customer' THEN MAX(g.CustomerName) WHEN 'productType' THEN MAX(pt.Name)
                                WHEN 'businessUnit' THEN MAX(bu.Name) WHEN 'weaveType' THEN MAX(wv.Name) ELSE g.[Key] END,
           Styles = COUNT(*)
    FROM g
    LEFT JOIN ref.ProductType pt ON @GroupBy = 'productType' AND pt.ProductTypeCode = g.[Key]
    LEFT JOIN ref.BusinessUnit bu ON @GroupBy = 'businessUnit' AND bu.BusinessUnitCode = g.[Key]
    LEFT JOIN ref.WeaveType wv ON @GroupBy = 'weaveType' AND wv.WeaveTypeCode = g.[Key]
    GROUP BY g.[Key], g.SortYear, g.SortTerm
    ORDER BY MAX(g.SortYear) DESC, MAX(g.SortTerm) DESC, COUNT(*) DESC, g.[Key]
    OPTION (RECOMPILE);
END
GO

PRINT 'Library assistant procedures ready.';
GO
