/*
    LT ODM Style Library - Fill the gaps and Similar styles (/api/v1/style-gaps, /api/v1/styles/{id}/similar)

    Fill the gaps: suggestions for what the library is missing, reviewed and accepted by a person. Nothing here
    changes a style until someone accepts.
      gender       rules: words in the description or model ("Women's", "Men's", "kids"), the style number
                   (Skechers P226M177 / 26FA_M_PT08: M or W), other seasons of the same style, the customer account's
                   name ("TJ Skechers Women Domestic"), women-only garments (bra, dress, skirt); then AI for the rest.
      productType  rules: other seasons of the same style, then the garment the description names (fn_GarmentCategory)
                   turned into the product type the customer (or its brand) already uses for that garment; then AI.
      seasons      free-form season names (FA26, SS27, 27SS) merged into year-term seasons (2026-FA, 2027-SS); the
                   merge is remembered as an import alias so the next upload uses the new name.
      LCO          empty LCO consumption filled from the brand consumption (optionally plus the allowance), else the
                   median LCO of other styles using the same material; filled values are marked LcoIsEstimate.
    Similar styles: other styles sharing the style's materials (rare materials and fabrics count most) and its garment,
    weave and gender. Same brand first; other brands are listed apart (know-how only, never pitched).

    Confidence: 3 = High, 2 = Medium, 1 = Check (rules disagree). Rule errors THROW 50400 / 50404 / 50409.
    Requires style.tables.sql and style.procedures.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.gaps.procedures.sql
*/
SET NOCOUNT ON;
GO

/* ---------- Garment categories ---------- */

/*
    The garment a text names: a product type name ("LONG PANTS", "T-SHIRT (SHORT SLEEVE)") or a style description
    ("Men's 92%POLYESTER 8%SPANDEX WOVEN 9in SHORTS", "WOVENDOWNJACKET1"). Words are often run together, so most checks
    look inside the text with letters and digits only; sleeve lengths are removed first (a short sleeve tee is a tee).
    The order matters: a hooded jacket is a jacket, short tights are tights, short pants are shorts.
    NULL when no garment word is found.
*/
CREATE OR ALTER FUNCTION style.fn_GarmentCategory (@Text nvarchar(800))
RETURNS nvarchar(20)
WITH INLINE = OFF   -- inlined, the letters-only text would be worked out again for every check below
AS
BEGIN
    DECLARE @t nvarchar(800) = UPPER(@Text);
    -- letters and digits only: punctuation to spaces, then spaces removed (TRANSLATE is much faster than REGEXP_REPLACE)
    DECLARE @c nvarchar(800) = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(TRANSLATE(@t, N'-/()''.,%+&:;!?"#*_[]{}|\<>=~@$^`', N'                                '), N' ', N''),
        N'SHORTSLEEVE', N''), N'LONGSLEEVE', N''), N'SHORTSLV', N''), N'LONGSLV', N''), N'SHORTSL', N''), N'LONGSL', N'');
    RETURN CASE
        WHEN @c IS NULL OR @c = N'' THEN NULL
        WHEN REGEXP_LIKE(@t, N'\bSETS?\b|[0-9] ?PCS?\b|\+') THEN N'SET'
        WHEN @c LIKE N'%PYJAMA%' OR @c LIKE N'%PAJAMA%' OR @c LIKE N'%SLEEP%' OR @c LIKE N'%NIGHT%' OR @c LIKE N'%ROBE%'
          OR @c LIKE N'%LOUNGE%' OR REGEXP_LIKE(@t, N'\bPJS?\b') THEN N'SLEEPWEAR'
        WHEN @c LIKE N'%DRESS%' THEN N'DRESS'
        WHEN @c LIKE N'%SKIRT%' OR @c LIKE N'%CULOTTE%' OR @c LIKE N'%SKORT%' THEN N'SKIRT'
        WHEN REGEXP_LIKE(@t, N'\bBRAS?\b') OR @c LIKE N'%SPORTSBRA%' OR @c LIKE N'%BRALETTE%' THEN N'BRA'
        WHEN @c LIKE N'%TIGHT%' OR @c LIKE N'%LEGGING%' OR @c LIKE N'%LEOTARD%' THEN N'TIGHTS'
        WHEN @c LIKE N'%JACKET%' OR @c LIKE N'%COAT%' OR @c LIKE N'%PARKA%' OR @c LIKE N'%ANORAK%' OR @c LIKE N'%WINDBREAKER%'
          OR @c LIKE N'%3IN1%' OR @c LIKE N'%TRACKTOP%' OR @c LIKE N'%BLAZER%' OR @c LIKE N'%BOMBER%' THEN N'JACKET'
        WHEN @c LIKE N'%VEST%' OR @c LIKE N'%GILET%' THEN N'VEST'
        WHEN @c LIKE N'%HOODIE%' OR @c LIKE N'%HOODY%' OR @c LIKE N'%HOODEDSWEAT%' THEN N'HOODIE'
        WHEN @c LIKE N'%PULLOVER%' OR @c LIKE N'%SWEATSHIRT%' OR @c LIKE N'%SWEATSHT%' OR @c LIKE N'%SWEATER%' OR @c LIKE N'%CREWNECK%'
          OR @c LIKE N'%JUMPER%' OR @c LIKE N'%HALFZIP%' OR @c LIKE N'%QUARTERZIP%' OR @c LIKE N'%POLARFLEECE%' THEN N'PULLOVER'
        WHEN @c LIKE N'%POLO%' THEN N'POLO'
        WHEN @c LIKE N'%TANK%' OR @c LIKE N'%SINGLET%' OR @c LIKE N'%CAMI%' THEN N'TANK'
        WHEN REGEXP_LIKE(@t, N'\bT-?SHIRTS?|TEES?\b|TEE[^A-Z]') OR @c LIKE N'%TSHIRT%' AND @c NOT LIKE N'%PLACKETSHIRT%'
          OR @c LIKE N'%JERSEY%' THEN N'TEE'
        WHEN @c LIKE N'%SHIRT%' OR @c LIKE N'%BLOUSE%' OR @c LIKE N'%PLACKET%' THEN N'SHIRT'
        WHEN @c LIKE N'%BOXER%' OR @c LIKE N'%BRIEF%' OR @c LIKE N'%UNDERWEAR%' THEN N'UNDERWEAR'
        WHEN @c LIKE N'%SHORT%' THEN N'SHORTS'
        WHEN @c LIKE N'%PANT%' OR @c LIKE N'%TROUSER%' OR @c LIKE N'%JOGGER%' OR @c LIKE N'%CHINO%' OR @c LIKE N'%JEAN%'
          OR @c LIKE N'%CAPRI%' THEN N'PANTS'
        WHEN REGEXP_LIKE(@t, N'\bTOPS?\b') OR @c LIKE N'%CROP%' THEN N'TOP'
        WHEN @c LIKE N'%HEADBAND%' OR REGEXP_LIKE(@t, N'\b(CAPS?|HATS?|BAGS?|SOCKS?)\b') THEN N'ACCESSORY'
    END;
END
GO

/* The garment categories AI may answer with (fn_GarmentCategory's values). */
CREATE OR ALTER FUNCTION style.fn_GarmentCategories()
RETURNS TABLE
AS RETURN
SELECT Category FROM (VALUES (N'JACKET'), (N'VEST'), (N'HOODIE'), (N'PULLOVER'), (N'POLO'), (N'TEE'), (N'SHIRT'), (N'TANK'), (N'TOP'),
    (N'PANTS'), (N'SHORTS'), (N'TIGHTS'), (N'SKIRT'), (N'DRESS'), (N'BRA'), (N'UNDERWEAR'), (N'SLEEPWEAR'), (N'SET'), (N'ACCESSORY')) v (Category);
GO

/* ---------- Suggestions ---------- */

/*
    Fills the caller's #sug with the current suggestion for every style missing @Field, leaving out values someone
    dismissed. The caller creates:
        CREATE TABLE #sug (StyleId int PRIMARY KEY, Value nvarchar(40) NOT NULL, ValueName nvarchar(100) NULL,
                           Category nvarchar(20) NULL, Confidence tinyint NOT NULL, Reason nvarchar(400) NULL, Source varchar(8) NOT NULL);
    gender: the first rule that answers (description words, style number, other seasons of the style: High; customer
            account name, women-only garment: Medium), else the AI's answer (Medium). Check (1) when rules disagree.
    productType: another season of the style (High), else the garment the description names, or the AI's category
            (Medium), as the product type the customer uses most for it, else its brand, then one whose name matches
            the weave (KNIT / WOVEN), then the most used anywhere.
    Each step is worked out once into a temporary table (about 0.1 s for 2,500 styles).
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_Suggest
    @Field varchar(16)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Field = 'gender'
    BEGIN
        SELECT st.StyleId, st.CustomerId, st.BaseStyleNo, st.StyleNo,
               Txt = REPLACE(REPLACE(REPLACE(CONCAT(N' ', st.Description, N' ', st.ModelName, N' '), NCHAR(8217), N''''), NCHAR(8216), N''''), N'`', N''''),
               Account = COALESCE(st.CustomerName, c.Name), st.ProductTypeCode, Garment = CAST(NULL AS nvarchar(20))
        INTO #s
        FROM style.Style st
        JOIN partner.Partner c ON c.PartnerId = st.CustomerId
        WHERE st.Gender IS NULL;
        -- the garment: from the product type (worked out once per type), else the description
        SELECT pt.ProductTypeCode, Category = style.fn_GarmentCategory(pt.Name) INTO #ptc
        FROM ref.ProductType pt WHERE pt.ProductTypeCode IN (SELECT ProductTypeCode FROM #s);
        UPDATE s SET Garment = p.Category FROM #s s JOIN #ptc p ON p.ProductTypeCode = s.ProductTypeCode;
        UPDATE #s SET Garment = style.fn_GarmentCategory(Txt) WHERE ProductTypeCode IS NULL;

        SELECT s.StyleId, s.Account,
               KidsWord   = REGEXP_SUBSTR(s.Txt, N'\b(kids?|youth|junior|boys?|girls?|bab(y|ies)|infants?|toddlers?|child(ren)?)\b', 1, 1, 'i'),
               UnisexWord = REGEXP_SUBSTR(s.Txt, N'\bunisex\b', 1, 1, 'i'),
               WomenWord  = REGEXP_SUBSTR(s.Txt, N'\b(wom[ae]n''?s?|ladies|lady|female|wmns)\b', 1, 1, 'i'),
               MenWord    = REGEXP_SUBSTR(s.Txt, N'\b(m[ae]n''?s?|male)\b', 1, 1, 'i'),
               NumberLetter = COALESCE(REGEXP_SUBSTR(s.StyleNo, N'^[A-Z]{1,2}[0-9]{3}([MW])[0-9]', 1, 1, 'c', 1),
                                       REGEXP_SUBSTR(s.StyleNo, N'_([MW])_', 1, 1, 'c', 1)),
               Family = CAST(NULL AS nvarchar(16)),
               AccountWomen = IIF(REGEXP_LIKE(s.Account, N'\b(wom[ae]n''?s?|ladies)\b', 'i'), 1, 0),
               AccountMen   = IIF(REGEXP_LIKE(s.Account, N'\bm[ae]n''?s?\b', 'i'), 1, 0),
               AccountKids  = IIF(REGEXP_LIKE(s.Account, N'\b(kids?|youth|boys?|girls?|baby)\b', 'i'), 1, 0),
               Garment = IIF(s.Garment IN (N'BRA', N'DRESS', N'SKIRT'), s.Garment, NULL)
        INTO #r
        FROM #s s
        OPTION (MAXDOP 1);   -- a parallel plan works the patterns out several times over
        -- other seasons of the same style, when they agree on one gender (a separate step keeps the plan above simple)
        UPDATE r SET Family = f.Gender
        FROM #r r JOIN #s s ON s.StyleId = r.StyleId
        CROSS APPLY (SELECT Gender = IIF(MIN(f.Gender) = MAX(f.Gender), MIN(f.Gender), NULL) FROM style.Style f
                     WHERE f.CustomerId = s.CustomerId AND f.BaseStyleNo = s.BaseStyleNo AND f.Gender IS NOT NULL) f
        WHERE f.Gender IS NOT NULL;

        SELECT r.StyleId,
               Word = CASE WHEN r.KidsWord IS NOT NULL THEN N'KIDS' WHEN r.UnisexWord IS NOT NULL THEN N'UNISEX'
                           WHEN r.WomenWord IS NOT NULL THEN N'FEMALE' WHEN r.MenWord IS NOT NULL THEN N'MALE' END,
               WordText = COALESCE(r.KidsWord, r.UnisexWord, r.WomenWord, r.MenWord),
               Number = CASE r.NumberLetter WHEN N'M' THEN N'MALE' WHEN N'W' THEN N'FEMALE' END, r.NumberLetter,
               r.Family,
               Account = CASE WHEN r.AccountWomen + r.AccountMen + r.AccountKids <> 1 THEN NULL
                              WHEN r.AccountWomen = 1 THEN N'FEMALE' WHEN r.AccountMen = 1 THEN N'MALE' ELSE N'KIDS' END,
               AccountName = r.Account,
               GarmentValue = IIF(r.Garment IS NULL, NULL, N'FEMALE'), r.Garment
        INTO #v
        FROM #r r;

        INSERT #sug (StyleId, Value, ValueName, Category, Confidence, Reason, Source)
        SELECT p.StyleId, p.Value, p.Value, NULL,
               IIF(p.Disagree = 1, 1, p.Base),
               IIF(p.Disagree = 1, CONCAT(p.Reason, N'; other clues disagree'), p.Reason), 'Rule'
        FROM (
            SELECT v.StyleId,
                   Value = COALESCE(v.Word, v.Number, v.Family, v.Account, v.GarmentValue),
                   Base  = IIF(COALESCE(v.Word, v.Number, v.Family) IS NOT NULL, 3, 2),
                   Reason = CASE
                       WHEN v.Word IS NOT NULL   THEN CONCAT(N'Description says "', TRIM(v.WordText), N'"')
                       WHEN v.Number IS NOT NULL THEN CONCAT(N'Style number has ', v.NumberLetter, IIF(v.NumberLetter = N'M', N' (men''s numbering)', N' (women''s numbering)'))
                       WHEN v.Family IS NOT NULL THEN N'Other seasons of this style'
                       WHEN v.Account IS NOT NULL THEN CONCAT(N'Customer account "', v.AccountName, N'"')
                       ELSE CONCAT(N'Garment is a ', LOWER(v.Garment)) END,
                   Disagree = IIF(EXISTS (SELECT 1 FROM (VALUES (v.Word), (v.Number), (v.Family), (v.Account), (v.GarmentValue)) o (g)
                                          WHERE o.g IS NOT NULL AND o.g <> COALESCE(v.Word, v.Number, v.Family, v.Account, v.GarmentValue)), 1, 0)
            FROM #v v
        ) p
        WHERE p.Value IS NOT NULL;

        INSERT #sug (StyleId, Value, ValueName, Category, Confidence, Reason, Source)
        SELECT g.StyleId, g.Value, g.Value, NULL, 2, g.Reason, 'AI'
        FROM style.GapSuggestion g
        JOIN #s s ON s.StyleId = g.StyleId
        WHERE g.Field = 'gender' AND g.Value <> N'-' AND NOT EXISTS (SELECT 1 FROM #sug x WHERE x.StyleId = g.StyleId);
    END
    ELSE IF @Field = 'productType'
    BEGIN
        SELECT st.StyleId, st.CustomerId, BrandCode = c.BrandCode, st.WeaveTypeCode,
               Family = (SELECT IIF(MIN(f.ProductTypeCode) = MAX(f.ProductTypeCode), MIN(f.ProductTypeCode), NULL) FROM style.Style f
                         WHERE f.CustomerId = st.CustomerId AND f.BaseStyleNo = st.BaseStyleNo AND f.ProductTypeCode IS NOT NULL),
               RuleCategory = style.fn_GarmentCategory(CONCAT(st.Description, N' ', st.ModelName)),
               AiCategory = NULLIF(a.Value, N'-'), AiReason = a.Reason
        INTO #p
        FROM style.Style st
        JOIN partner.Partner c ON c.PartnerId = st.CustomerId
        LEFT JOIN style.GapSuggestion a ON a.StyleId = st.StyleId AND a.Field = 'productType'
        WHERE st.ProductTypeCode IS NULL;

        -- product types by garment, and how much each customer, brand and the library use them
        SELECT pt.ProductTypeCode, pt.Name, Category = style.fn_GarmentCategory(pt.Name),
               WeaveWord = CASE WHEN pt.Name LIKE N'%KNIT%' THEN N'KNT' WHEN pt.Name LIKE N'%WOVEN%' THEN N'WVN' END,
               Anywhere = (SELECT COUNT(*) FROM style.Style s WHERE s.ProductTypeCode = pt.ProductTypeCode)
        INTO #pt
        FROM ref.ProductType pt
        WHERE pt.IsActive = 1;
        DELETE #pt WHERE Category IS NULL;
        SELECT s.CustomerId, s.ProductTypeCode, Styles = COUNT(*) INTO #own FROM style.Style s WHERE s.ProductTypeCode IS NOT NULL GROUP BY s.CustomerId, s.ProductTypeCode;
        SELECT c.BrandCode, s.ProductTypeCode, Styles = COUNT(*) INTO #brand
        FROM style.Style s JOIN partner.Partner c ON c.PartnerId = s.CustomerId
        WHERE s.ProductTypeCode IS NOT NULL AND c.BrandCode IS NOT NULL GROUP BY c.BrandCode, s.ProductTypeCode;

        INSERT #sug (StyleId, Value, ValueName, Category, Confidence, Reason, Source)
        SELECT p.StyleId, p.Family, pt.Name, NULL, 3, N'Other seasons of this style', 'Rule'
        FROM #p p JOIN ref.ProductType pt ON pt.ProductTypeCode = p.Family
        WHERE p.Family IS NOT NULL;

        INSERT #sug (StyleId, Value, ValueName, Category, Confidence, Reason, Source)
        SELECT p.StyleId, t.ProductTypeCode, t.Name, COALESCE(p.RuleCategory, p.AiCategory), 2,
               IIF(p.RuleCategory IS NOT NULL, CONCAT(N'Description reads as ', LOWER(p.RuleCategory)), p.AiReason),
               IIF(p.RuleCategory IS NOT NULL, 'Rule', 'AI')
        FROM #p p
        CROSS APPLY (SELECT TOP (1) x.ProductTypeCode, x.Name
                     FROM #pt x
                     LEFT JOIN #own o ON o.CustomerId = p.CustomerId AND o.ProductTypeCode = x.ProductTypeCode
                     LEFT JOIN #brand b ON b.BrandCode = p.BrandCode AND b.ProductTypeCode = x.ProductTypeCode
                     WHERE x.Category = COALESCE(p.RuleCategory, p.AiCategory)
                     ORDER BY ISNULL(o.Styles, 0) DESC, ISNULL(b.Styles, 0) DESC,
                              CASE WHEN x.WeaveWord = p.WeaveTypeCode THEN 0 WHEN x.WeaveWord IS NULL THEN 1 ELSE 2 END,
                              x.Anywhere DESC, LEN(x.Name), x.ProductTypeCode) t
        WHERE p.Family IS NULL AND COALESCE(p.RuleCategory, p.AiCategory) IS NOT NULL;
    END
    ELSE THROW 50400, 'Choose gender or productType.', 1;

    DELETE g FROM #sug g WHERE EXISTS (SELECT 1 FROM style.GapDismissed d WHERE d.StyleId = g.StyleId AND d.Field = @Field AND d.Value = g.Value);
END
GO

/* ---------- Summary (Fill the gaps page and the home dashboard) ---------- */

/*
    What the library is missing. @WithSuggestions = 1 also counts the current suggestions (about 1.5 s on 2,500 styles):
    the Fill the gaps page; the home dashboard leaves them out (NULL).
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_Summary
    @WithSuggestions bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @genderSuggested int, @genderHigh int, @ptSuggested int, @ptHigh int;
    IF @WithSuggestions = 1
    BEGIN
        CREATE TABLE #sug (StyleId int PRIMARY KEY, Value nvarchar(40) NOT NULL, ValueName nvarchar(100) NULL, Category nvarchar(20) NULL,
                           Confidence tinyint NOT NULL, Reason nvarchar(400) NULL, Source varchar(8) NOT NULL);
        EXEC style.usp_Gaps_Suggest 'gender';
        SELECT @genderSuggested = COUNT(*), @genderHigh = SUM(IIF(Confidence = 3, 1, 0)) FROM #sug;
        TRUNCATE TABLE #sug;
        EXEC style.usp_Gaps_Suggest 'productType';
        SELECT @ptSuggested = COUNT(*), @ptHigh = SUM(IIF(Confidence = 3, 1, 0)) FROM #sug;
    END

    SELECT Styles               = (SELECT COUNT(*) FROM style.Style),
           GenderMissing        = (SELECT COUNT(*) FROM style.Style WHERE Gender IS NULL),
           GenderSuggested      = @genderSuggested,
           GenderHigh           = @genderHigh,
           ProductTypeMissing   = (SELECT COUNT(*) FROM style.Style WHERE ProductTypeCode IS NULL),
           ProductTypeSuggested = @ptSuggested,
           ProductTypeHigh      = @ptHigh,
           SeasonsToMerge       = (SELECT COUNT(*) FROM ref.Season se WHERE NOT REGEXP_LIKE(se.SeasonCode, N'^[0-9]{4}-[A-Z]{2,4}$')
                                     AND EXISTS (SELECT 1 FROM style.Style s WHERE s.SeasonCode = se.SeasonCode)),
           StylesInSeasonsToMerge = (SELECT COUNT(*) FROM style.Style s WHERE NOT REGEXP_LIKE(s.SeasonCode, N'^[0-9]{4}-[A-Z]{2,4}$')),
           LeadTimeMissing      = (SELECT COUNT(*) FROM style.Style WHERE ISNULL(GarmentLeadTimeDays, 0) = 0),
           LcoMissing           = (SELECT COUNT(*) FROM style.BomLine WHERE LcoConsumption IS NULL),
           LcoFromBrand         = (SELECT COUNT(*) FROM style.BomLine WHERE LcoConsumption IS NULL AND BrandConsumption > 0),
           LcoEstimates         = (SELECT COUNT(*) FROM style.BomLine WHERE LcoIsEstimate = 1),
           SupplierMissing      = (SELECT COUNT(*) FROM style.BomLine WHERE SupplierId IS NULL AND ContentClassCode IN (N'FAB', N'TRI', N'ACC'));
END
GO

/* ---------- Gender and product type ---------- */

/*
    Styles without a gender (@Field 'gender') or product type ('productType'), with the suggestion when there is one.
    @Show: suggested (default) | none (no suggestion: fill by hand or ask AI) | all. @MinConfidence 1-3.
    Result sets: 1. the page (TotalCount on each row); 2. counts in the scope by confidence (0 = no suggestion).
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_List
    @Field         varchar(16),
    @CustomerCode  nvarchar(32) = NULL,
    @SeasonCode    nvarchar(16) = NULL,
    @Show          varchar(10)  = 'suggested',
    @MinConfidence tinyint      = 1,
    @Skip          int          = 0,
    @Take          int          = 50
AS
BEGIN
    SET NOCOUNT ON;
    CREATE TABLE #sug (StyleId int PRIMARY KEY, Value nvarchar(40) NOT NULL, ValueName nvarchar(100) NULL, Category nvarchar(20) NULL,
                       Confidence tinyint NOT NULL, Reason nvarchar(400) NULL, Source varchar(8) NOT NULL);
    EXEC style.usp_Gaps_Suggest @Field;

    SELECT s.StyleId, CustomerCode = c.PartnerCode, Account = COALESCE(s.CustomerName, c.Name), s.SeasonCode, s.StyleNo, s.Description, s.ModelName,
           s.WeaveTypeCode, s.Gender, ProductTypeName = pt.Name, s.ImageUrl, s.SketchUrl,
           g.Value, g.ValueName, g.Category, Confidence = ISNULL(g.Confidence, 0), g.Reason, g.Source
    INTO #scope
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    LEFT JOIN #sug g ON g.StyleId = s.StyleId
    WHERE (@Field = 'gender' AND s.Gender IS NULL OR @Field = 'productType' AND s.ProductTypeCode IS NULL)
      AND (@CustomerCode IS NULL OR c.PartnerCode = @CustomerCode)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode);

    SELECT x.*, TotalCount = COUNT(*) OVER ()
    FROM #scope x
    WHERE (@Show = 'all' OR @Show = 'none' AND x.Value IS NULL OR @Show = 'suggested' AND x.Value IS NOT NULL AND x.Confidence >= @MinConfidence)
    ORDER BY x.Confidence DESC, x.CustomerCode, x.SeasonCode, x.StyleNo
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

    SELECT Confidence, Styles = COUNT(*) FROM #scope GROUP BY Confidence;
END
GO

/*
    Accepts suggestions. @Items = [{"styleId":1,"value":"FEMALE"}, ...] (values as suggested, or chosen by hand), or
    NULL to accept every current suggestion in the scope with at least @MinConfidence. Only styles still missing the
    field change, and only to a valid value. Returns Applied.
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_Accept
    @Field         varchar(16),
    @Items         nvarchar(max) = NULL,
    @CustomerCode  nvarchar(32)  = NULL,
    @SeasonCode    nvarchar(16)  = NULL,
    @MinConfidence tinyint       = 3,
    @ChangedBy     nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Field NOT IN ('gender', 'productType') THROW 50400, 'Choose gender or productType.', 1;
    IF @Items IS NOT NULL AND ISJSON(@Items) = 0 THROW 50400, 'The items must be a JSON array.', 1;

    CREATE TABLE #pick (StyleId int PRIMARY KEY, Value nvarchar(40) NOT NULL);
    IF @Items IS NOT NULL
        INSERT #pick
        SELECT StyleId, MAX(TRIM(Value)) FROM OPENJSON(@Items) WITH (StyleId int '$.styleId', Value nvarchar(40) '$.value')
        WHERE StyleId IS NOT NULL AND NULLIF(TRIM(Value), N'') IS NOT NULL
        GROUP BY StyleId;
    ELSE
    BEGIN
        CREATE TABLE #sug (StyleId int PRIMARY KEY, Value nvarchar(40) NOT NULL, ValueName nvarchar(100) NULL, Category nvarchar(20) NULL,
                           Confidence tinyint NOT NULL, Reason nvarchar(400) NULL, Source varchar(8) NOT NULL);
        EXEC style.usp_Gaps_Suggest @Field;
        INSERT #pick
        SELECT g.StyleId, g.Value FROM #sug g
        JOIN style.Style s ON s.StyleId = g.StyleId JOIN partner.Partner c ON c.PartnerId = s.CustomerId
        WHERE g.Confidence >= @MinConfidence AND (@CustomerCode IS NULL OR c.PartnerCode = @CustomerCode) AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode);
    END

    IF @Field = 'gender'
    BEGIN
        UPDATE #pick SET Value = UPPER(Value);
        IF EXISTS (SELECT 1 FROM #pick WHERE Value NOT IN (N'MALE', N'FEMALE', N'UNISEX', N'KIDS'))
            THROW 50400, 'Gender must be MALE, FEMALE, UNISEX or KIDS.', 1;
    END
    ELSE IF EXISTS (SELECT 1 FROM #pick p WHERE NOT EXISTS (SELECT 1 FROM ref.ProductType pt WHERE pt.ProductTypeCode = p.Value))
        THROW 50400, 'Choose a product type from the list.', 1;

    DECLARE @applied int;
    BEGIN TRANSACTION;
    IF @Field = 'gender'
        UPDATE s SET Gender = p.Value, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        FROM style.Style s JOIN #pick p ON p.StyleId = s.StyleId WHERE s.Gender IS NULL;
    ELSE
        UPDATE s SET ProductTypeCode = pt.ProductTypeCode, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        FROM style.Style s JOIN #pick p ON p.StyleId = s.StyleId JOIN ref.ProductType pt ON pt.ProductTypeCode = p.Value
        WHERE s.ProductTypeCode IS NULL;
    SET @applied = @@ROWCOUNT;
    DELETE g FROM style.GapSuggestion g JOIN #pick p ON p.StyleId = g.StyleId WHERE g.Field = @Field;
    DELETE d FROM style.GapDismissed d JOIN #pick p ON p.StyleId = d.StyleId WHERE d.Field = @Field;
    COMMIT TRANSACTION;
    SELECT Applied = @applied;
END
GO

/* Turns suggestions down: @Items = [{"styleId":1,"value":"FEMALE"}]. The same value is not offered again for that style. */
CREATE OR ALTER PROCEDURE style.usp_Gaps_Dismiss
    @Field     varchar(16),
    @Items     nvarchar(max),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Field NOT IN ('gender', 'productType') THROW 50400, 'Choose gender or productType.', 1;
    IF ISJSON(@Items) = 0 THROW 50400, 'The items must be a JSON array.', 1;
    INSERT style.GapDismissed (StyleId, Field, Value, DismissedBy)
    SELECT DISTINCT i.StyleId, @Field, TRIM(i.Value), @ChangedBy
    FROM OPENJSON(@Items) WITH (StyleId int '$.styleId', Value nvarchar(40) '$.value') i
    WHERE EXISTS (SELECT 1 FROM style.Style s WHERE s.StyleId = i.StyleId) AND NULLIF(TRIM(i.Value), N'') IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM style.GapDismissed d WHERE d.StyleId = i.StyleId AND d.Field = @Field AND d.Value = TRIM(i.Value));
    SELECT Dismissed = @@ROWCOUNT;
END
GO

/*
    The next styles to ask the AI about: missing the field, no rule suggestion, not asked before, nothing dismissed,
    and some description or model name to read. Remaining = how many such styles are left in the scope (including these).
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_ToAsk
    @Field        varchar(16),
    @CustomerCode nvarchar(32) = NULL,
    @SeasonCode   nvarchar(16) = NULL,
    @Take         int          = 25
AS
BEGIN
    SET NOCOUNT ON;
    CREATE TABLE #sug (StyleId int PRIMARY KEY, Value nvarchar(40) NOT NULL, ValueName nvarchar(100) NULL, Category nvarchar(20) NULL,
                       Confidence tinyint NOT NULL, Reason nvarchar(400) NULL, Source varchar(8) NOT NULL);
    EXEC style.usp_Gaps_Suggest @Field;

    SELECT s.StyleId, s.StyleNo, s.Description, s.ModelName, s.WeaveTypeCode, s.Gender, ProductTypeName = pt.Name
    INTO #ask
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE (@Field = 'gender' AND s.Gender IS NULL OR @Field = 'productType' AND s.ProductTypeCode IS NULL)
      AND (@CustomerCode IS NULL OR c.PartnerCode = @CustomerCode)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode)
      AND NOT EXISTS (SELECT 1 FROM #sug g WHERE g.StyleId = s.StyleId AND g.Source = 'Rule')
      AND NOT EXISTS (SELECT 1 FROM style.GapSuggestion g WHERE g.StyleId = s.StyleId AND g.Field = @Field)
      AND NOT EXISTS (SELECT 1 FROM style.GapDismissed d WHERE d.StyleId = s.StyleId AND d.Field = @Field)
      AND (NULLIF(TRIM(s.Description), N'') IS NOT NULL OR NULLIF(TRIM(s.ModelName), N'') IS NOT NULL);

    SELECT TOP (@Take) a.*, Remaining = (SELECT COUNT(*) FROM #ask)
    FROM #ask a ORDER BY a.StyleId;
END
GO

/*
    Stores the AI's answers: @Items = [{"styleId":1,"value":"FEMALE","reason":"..."}]; value '-' = the AI could not
    tell (the style is not asked again). Values are checked by the caller and again here.
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_SaveAi
    @Field     varchar(16),
    @Items     nvarchar(max),
    @Provider  nvarchar(100),
    @Model     nvarchar(200),
    @CreatedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Field NOT IN ('gender', 'productType') THROW 50400, 'Choose gender or productType.', 1;
    IF ISJSON(@Items) = 0 THROW 50400, 'The items must be a JSON array.', 1;

    MERGE style.GapSuggestion AS t
    USING (
        SELECT DISTINCT i.StyleId, Value = UPPER(TRIM(i.Value)), Reason = LEFT(i.Reason, 300)
        FROM OPENJSON(@Items) WITH (StyleId int '$.styleId', Value nvarchar(40) '$.value', Reason nvarchar(400) '$.reason') i
        WHERE EXISTS (SELECT 1 FROM style.Style s WHERE s.StyleId = i.StyleId)
          AND (UPPER(TRIM(i.Value)) = N'-'
               OR @Field = 'gender' AND UPPER(TRIM(i.Value)) IN (N'MALE', N'FEMALE', N'UNISEX', N'KIDS')
               OR @Field = 'productType' AND UPPER(TRIM(i.Value)) IN (SELECT Category FROM style.fn_GarmentCategories()))
    ) AS x ON t.StyleId = x.StyleId AND t.Field = @Field
    WHEN MATCHED THEN UPDATE SET Value = x.Value, Reason = x.Reason, Provider = @Provider, Model = @Model, CreatedBy = @CreatedBy, CreatedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (StyleId, Field, Value, Reason, Provider, Model, CreatedBy) VALUES (x.StyleId, @Field, x.Value, x.Reason, @Provider, @Model, @CreatedBy);
    SELECT Saved = @@ROWCOUNT;
END
GO

/* ---------- Seasons ---------- */

/*
    Seasons not named year-term (FA26, 27SS, 26Q2, 999) that styles use, with the year-term season their name shows
    (Suggested, NULL for quarters and other names) and how many styles could not move there because the customer
    already has the same style number in it (Conflicts).
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_Seasons
AS
BEGIN
    SET NOCOUNT ON;
    SELECT se.SeasonCode, SeasonYear = CAST(se.SeasonYear AS int), se.Term, x.Suggested,
           Styles    = (SELECT COUNT(*) FROM style.Style s WHERE s.SeasonCode = se.SeasonCode),
           Conflicts = IIF(x.Suggested IS NULL, 0,
                           (SELECT COUNT(*) FROM style.Style s
                            WHERE s.SeasonCode = se.SeasonCode
                              AND EXISTS (SELECT 1 FROM style.Style o WHERE o.CustomerId = s.CustomerId AND o.StyleNo = s.StyleNo AND o.SeasonCode = x.Suggested))),
           SuggestedExists = CAST(IIF(EXISTS (SELECT 1 FROM ref.Season t WHERE t.SeasonCode = x.Suggested), 1, 0) AS bit)
    FROM ref.Season se
    CROSS APPLY (SELECT Suggested = IIF(se.SeasonYear IS NOT NULL AND se.Term IS NOT NULL, CONCAT(se.SeasonYear, N'-', se.Term), NULL)) x
    WHERE NOT REGEXP_LIKE(se.SeasonCode, N'^[0-9]{4}-[A-Z]{2,4}$')
      AND EXISTS (SELECT 1 FROM style.Style s WHERE s.SeasonCode = se.SeasonCode)
    ORDER BY ISNULL(se.SeasonYear, 9999), IIF(x.Suggested IS NULL, 1, 0), se.SeasonCode;
END
GO

/*
    Moves the styles of season @From to the year-term season @To (created when its term is known), except styles whose
    customer already has the same number in @To. Remembers @From -> @To as an import alias (next uploads use @To) and
    removes @From when no style is left in it. Returns Moved, Conflicts.
*/
CREATE OR ALTER PROCEDURE style.usp_Gaps_MergeSeason
    @From      nvarchar(16),
    @To        nvarchar(16),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @From = TRIM(@From);
    SET @To = UPPER(TRIM(@To));
    IF NOT EXISTS (SELECT 1 FROM ref.Season WHERE SeasonCode = @From) THROW 50404, 'This season no longer exists.', 1;
    IF @From = @To THROW 50400, 'Choose another season to merge into.', 1;
    -- checked before the transaction, so a refused merge never leaves one open
    IF NOT EXISTS (SELECT 1 FROM ref.Season WHERE SeasonCode = @To)
       AND (NOT REGEXP_LIKE(@To, N'^[0-9]{4}-[A-Z]{2,4}$') OR NOT EXISTS (SELECT 1 FROM ref.SeasonTerm WHERE Term = SUBSTRING(@To, 6, 8)))
    BEGIN
        DECLARE @msg nvarchar(400) = CONCAT(N'Season "', @To, N'" must look like 2027-SS (year, dash, ',
            (SELECT STRING_AGG(Term, N'/') WITHIN GROUP (ORDER BY SortOrder) FROM ref.SeasonTerm), N').');
        THROW 50400, @msg, 1;
    END
    IF NOT REGEXP_LIKE(@To, N'^[0-9]{4}-[A-Z]{2,4}$') THROW 50400, 'Merge into a season named year-term, such as 2027-SS.', 1;

    BEGIN TRANSACTION;
    EXEC style.usp_EnsureSeason @To;

    UPDATE s SET SeasonCode = @To, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    FROM style.Style s
    WHERE s.SeasonCode = @From
      AND NOT EXISTS (SELECT 1 FROM style.Style o WHERE o.CustomerId = s.CustomerId AND o.StyleNo = s.StyleNo AND o.SeasonCode = @To);
    DECLARE @moved int = @@ROWCOUNT;
    DECLARE @conflicts int = (SELECT COUNT(*) FROM style.Style WHERE SeasonCode = @From);

    MERGE staging.ImportCodeAlias AS t
    USING (SELECT List = 'season', Value = UPPER(@From), Code = @To) AS x ON t.List = x.List AND t.Value = x.Value
    WHEN MATCHED THEN UPDATE SET Code = x.Code, CreatedBy = @ChangedBy, CreatedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (List, Value, Code, CreatedBy) VALUES (x.List, x.Value, x.Code, @ChangedBy);

    IF @conflicts = 0 DELETE ref.Season WHERE SeasonCode = @From;
    COMMIT TRANSACTION;
    SELECT Moved = @moved, Conflicts = @conflicts;
END
GO

/* ---------- LCO consumption ---------- */

/*
    Fills the caller's #lco with a suggested LCO consumption for the BOM lines without one, in the scope (@StyleId,
    @CustomerCode, @SeasonCode; nothing = the whole library): the brand consumption (times 1 + the allowance when
    @WithAllowance = 1), else the median LCO of at least 3 other styles using the same material and unit (same product
    type first). Estimated LCOs are not used as examples. The caller creates:
        CREATE TABLE #lco (BomLineId int PRIMARY KEY, StyleId int NOT NULL, Value decimal(18, 6) NULL, Source varchar(16) NULL, Peers int NULL);
*/
CREATE OR ALTER PROCEDURE style.usp_Lco_Suggest
    @WithAllowance bit          = 0,
    @StyleId       int          = NULL,
    @CustomerCode  nvarchar(32) = NULL,
    @SeasonCode    nvarchar(16) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT #lco (BomLineId, StyleId, Value, Source)
    SELECT b.BomLineId, b.StyleId,
           IIF(b.BrandConsumption > 0, CAST(ROUND(b.BrandConsumption * IIF(@WithAllowance = 1, 1 + ISNULL(b.ConsumptionAllowance, 0), 1), 6) AS decimal(18, 6)), NULL),
           IIF(b.BrandConsumption > 0, IIF(@WithAllowance = 1 AND b.ConsumptionAllowance > 0, 'BrandAllowance', 'Brand'), NULL)
    FROM style.BomLine b
    JOIN style.Style s ON s.StyleId = b.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    WHERE b.LcoConsumption IS NULL
      AND (@StyleId IS NULL OR b.StyleId = @StyleId)
      AND (@CustomerCode IS NULL OR c.PartnerCode = @CustomerCode)
      AND (@SeasonCode IS NULL OR s.SeasonCode = @SeasonCode);

    -- lines without a brand figure (few): the median of other styles using the material, same product type first
    UPDATE l SET Value = CAST(ROUND(m.Median, 6) AS decimal(18, 6)), Source = m.Source, Peers = m.Styles
    FROM #lco l
    JOIN style.BomLine b ON b.BomLineId = l.BomLineId
    JOIN style.Style s ON s.StyleId = b.StyleId
    CROSS APPLY (
        SELECT TOP (1) x.Median, x.Styles, x.Source FROM (
            SELECT * FROM (
                SELECT TOP (1) Median = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY p.LcoConsumption) OVER (), Styles = COUNT(*) OVER (),
                       Source = 'SameProductType', Rnk = 1
                FROM style.BomLine p JOIN style.Style ps ON ps.StyleId = p.StyleId
                WHERE p.MaterialId = b.MaterialId AND p.UomCode = b.UomCode AND p.LcoConsumption > 0 AND p.LcoIsEstimate = 0
                  AND ps.ProductTypeCode = s.ProductTypeCode) a
            UNION ALL
            SELECT * FROM (
                SELECT TOP (1) Median = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY p.LcoConsumption) OVER (), Styles = COUNT(*) OVER (),
                       Source = 'SameMaterial', Rnk = 2
                FROM style.BomLine p
                WHERE p.MaterialId = b.MaterialId AND p.UomCode = b.UomCode AND p.LcoConsumption > 0 AND p.LcoIsEstimate = 0) z
        ) x
        WHERE x.Styles >= 3
        ORDER BY x.Rnk) m
    WHERE l.Value IS NULL;
END
GO

/* One style's BOM lines without LCO and their suggestion (Value NULL: nothing to go by). */
CREATE OR ALTER PROCEDURE style.usp_Lco_ForStyle
    @StyleId       int,
    @WithAllowance bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    CREATE TABLE #lco (BomLineId int PRIMARY KEY, StyleId int NOT NULL, Value decimal(18, 6) NULL, Source varchar(16) NULL, Peers int NULL);
    EXEC style.usp_Lco_Suggest @WithAllowance, @StyleId = @StyleId;
    SELECT b.BomLineId, b.LineSeq, m.MaterialCode, b.MaterialDescription, b.ContentClassCode, b.UomCode, b.BrandConsumption,
           b.ConsumptionAllowance, Suggested = f.Value, f.Source, f.Peers
    FROM #lco f
    JOIN style.BomLine b ON b.BomLineId = f.BomLineId
    JOIN mat.Material m ON m.MaterialId = b.MaterialId
    ORDER BY b.LineSeq;
END
GO

/* Lines without LCO per customer and season, and how many have a suggestion. */
CREATE OR ALTER PROCEDURE style.usp_Lco_Summary
    @WithAllowance bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    CREATE TABLE #lco (BomLineId int PRIMARY KEY, StyleId int NOT NULL, Value decimal(18, 6) NULL, Source varchar(16) NULL, Peers int NULL);
    EXEC style.usp_Lco_Suggest @WithAllowance;
    SELECT CustomerCode = c.PartnerCode, Account = COALESCE(MAX(s.CustomerName), c.Name), s.SeasonCode,
           Lines = COUNT(*), Suggested = SUM(IIF(f.Value IS NOT NULL, 1, 0)), Styles = COUNT(DISTINCT s.StyleId)
    FROM #lco f
    JOIN style.Style s ON s.StyleId = f.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    GROUP BY c.PartnerCode, c.Name, s.SeasonCode
    ORDER BY Lines DESC, c.PartnerCode, s.SeasonCode;
END
GO

/*
    Fills LCO from the suggestions and marks it as an estimate: chosen lines (@BomLineIds = [1,2,...]), one style, or a
    customer and/or season; @All = 1 with nothing else = every line in the library. Lines that got an LCO in the
    meantime are left alone. Returns Updated, Styles.
*/
CREATE OR ALTER PROCEDURE style.usp_Lco_Apply
    @BomLineIds    nvarchar(max) = NULL,
    @StyleId       int           = NULL,
    @CustomerCode  nvarchar(32)  = NULL,
    @SeasonCode    nvarchar(16)  = NULL,
    @All           bit           = 0,
    @WithAllowance bit           = 0,
    @ChangedBy     nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @BomLineIds IS NULL AND @StyleId IS NULL AND @CustomerCode IS NULL AND @SeasonCode IS NULL AND @All = 0
        THROW 50400, 'Choose lines, a style, a customer or a season.', 1;
    IF @BomLineIds IS NOT NULL AND ISJSON(@BomLineIds) = 0 THROW 50400, 'The lines must be a JSON array.', 1;

    CREATE TABLE #lco (BomLineId int PRIMARY KEY, StyleId int NOT NULL, Value decimal(18, 6) NULL, Source varchar(16) NULL, Peers int NULL);
    EXEC style.usp_Lco_Suggest @WithAllowance, @StyleId, @CustomerCode, @SeasonCode;
    SELECT f.BomLineId, f.StyleId, f.Value
    INTO #fill
    FROM #lco f
    WHERE f.Value IS NOT NULL
      AND (@BomLineIds IS NULL OR f.BomLineId IN (SELECT CAST([value] AS int) FROM OPENJSON(@BomLineIds)));

    BEGIN TRANSACTION;
    UPDATE b SET LcoConsumption = f.Value, LcoIsEstimate = 1, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    FROM style.BomLine b JOIN #fill f ON f.BomLineId = b.BomLineId
    WHERE b.LcoConsumption IS NULL;
    DECLARE @updated int = @@ROWCOUNT;
    UPDATE s SET UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    FROM style.Style s WHERE s.StyleId IN (SELECT StyleId FROM #fill);
    DECLARE @styles int = @@ROWCOUNT;
    COMMIT TRANSACTION;
    SELECT Updated = @updated, Styles = @styles;
END
GO

/* ---------- Similar styles ---------- */

/*
    Styles like @StyleId, for "have we made this before, and what did we use?" Score 0-100:
      65% materials: shared materials weighted by section (fabric 3, artwork 1.5, trims and accessories 1, labels and
          packaging 0.2) and rarity (a material on few styles says more than a sewing thread on all), as weighted
          overlap of the two BOMs;
      20% same garment (fn_GarmentCategory of the product type; the description for a style without one), 10% same weave,
      5% same gender.
    The style's own family (same customer and base number, or linked by history) is left out: it is on the History tab.
    SameBrand = same customer or a customer of the same brand. Up to @Top of each (other brands: know-how only).
*/
CREATE OR ALTER PROCEDURE style.usp_Style_Similar
    @StyleId int,
    @Top     int = 8
AS
BEGIN
    SET NOCOUNT ON;

    -- garment of each product type, worked out once (styles without a product type are read from their description)
    SELECT pt.ProductTypeCode, Category = style.fn_GarmentCategory(pt.Name)
    INTO #ptc
    FROM ref.ProductType pt;
    ALTER TABLE #ptc ADD PRIMARY KEY (ProductTypeCode);

    DECLARE @customerId int, @brand nvarchar(32), @base nvarchar(100), @weave nvarchar(8), @gender nvarchar(16), @category nvarchar(20);
    SELECT @customerId = s.CustomerId, @brand = c.BrandCode, @base = s.BaseStyleNo, @weave = s.WeaveTypeCode, @gender = s.Gender,
           @category = COALESCE(p.Category, IIF(s.ProductTypeCode IS NULL, style.fn_GarmentCategory(CONCAT(s.Description, N' ', s.ModelName)), NULL))
    FROM style.Style s
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN #ptc p ON p.ProductTypeCode = s.ProductTypeCode
    WHERE s.StyleId = @StyleId;
    IF @customerId IS NULL THROW 50404, 'This style no longer exists.', 1;

    DECLARE @n float = (SELECT COUNT(*) FROM style.Style);

    -- the family, left out
    CREATE TABLE #family (StyleId int PRIMARY KEY);
    INSERT #family SELECT StyleId FROM style.Style WHERE CustomerId = @customerId AND BaseStyleNo = @base;
    INSERT #family SELECT DISTINCT x.Id FROM style.StyleHistory h CROSS APPLY (VALUES (h.StyleId), (h.SourceStyleId)) x (Id)
    WHERE (h.StyleId IN (SELECT StyleId FROM #family) OR h.SourceStyleId IN (SELECT StyleId FROM #family)) AND x.Id NOT IN (SELECT StyleId FROM #family);

    -- the style's materials and their weight
    CREATE TABLE #t (MaterialId int PRIMARY KEY, W float NOT NULL, IsFabric bit NOT NULL);
    INSERT #t
    SELECT b.MaterialId,
           MAX(CASE b.ContentClassCode WHEN N'FAB' THEN 3.0 WHEN N'ART' THEN 1.5 WHEN N'TRI' THEN 1.0 WHEN N'ACC' THEN 1.0 WHEN N'LNP' THEN 0.2 ELSE 0.5 END)
             * LOG(1 + @n / (SELECT COUNT(DISTINCT x.StyleId) FROM style.BomLine x WHERE x.MaterialId = b.MaterialId)),
           MAX(IIF(b.ContentClassCode = N'FAB', 1, 0))
    FROM style.BomLine b WHERE b.StyleId = @StyleId GROUP BY b.MaterialId;
    DECLARE @total float = ISNULL((SELECT SUM(W) FROM #t), 0);

    -- styles sharing materials (the best 300)
    CREATE TABLE #c (StyleId int PRIMARY KEY, Shared float NOT NULL, SharedMaterials int NOT NULL, SharedFabrics int NOT NULL, Total float NULL);
    INSERT #c (StyleId, Shared, SharedMaterials, SharedFabrics)
    SELECT TOP (300) u.StyleId, SUM(t.W), COUNT(*), SUM(CAST(t.IsFabric AS int))
    FROM (SELECT DISTINCT b.StyleId, b.MaterialId FROM style.BomLine b JOIN #t t ON t.MaterialId = b.MaterialId) u
    JOIN #t t ON t.MaterialId = u.MaterialId
    WHERE u.StyleId <> @StyleId AND u.StyleId NOT IN (SELECT StyleId FROM #family)
    GROUP BY u.StyleId
    ORDER BY SUM(t.W) DESC;

    -- their whole BOM's weight, for the overlap
    SELECT b.StyleId, b.MaterialId, ClassWeight = MAX(CASE b.ContentClassCode WHEN N'FAB' THEN 3.0 WHEN N'ART' THEN 1.5 WHEN N'TRI' THEN 1.0 WHEN N'ACC' THEN 1.0 WHEN N'LNP' THEN 0.2 ELSE 0.5 END)
    INTO #cm
    FROM style.BomLine b JOIN #c c ON c.StyleId = b.StyleId
    GROUP BY b.StyleId, b.MaterialId;
    SELECT x.MaterialId, Styles = COUNT(DISTINCT x.StyleId)
    INTO #df
    FROM style.BomLine x WHERE x.MaterialId IN (SELECT MaterialId FROM #cm) GROUP BY x.MaterialId;
    UPDATE c SET Total = w.Total
    FROM #c c
    JOIN (SELECT cm.StyleId, Total = SUM(cm.ClassWeight * LOG(1 + @n / df.Styles))
          FROM #cm cm JOIN #df df ON df.MaterialId = cm.MaterialId GROUP BY cm.StyleId) w ON w.StyleId = c.StyleId;

    -- plus styles of the same garment and weave that share no material (other customers mostly)
    IF @category IS NOT NULL
        INSERT #c (StyleId, Shared, SharedMaterials, SharedFabrics, Total)
        SELECT TOP (200) s.StyleId, 0, 0, 0, NULL
        FROM style.Style s
        JOIN #ptc p ON p.ProductTypeCode = s.ProductTypeCode
        WHERE p.Category = @category AND (@weave IS NULL OR s.WeaveTypeCode = @weave)
          AND s.StyleId <> @StyleId AND s.StyleId NOT IN (SELECT StyleId FROM #family) AND s.StyleId NOT IN (SELECT StyleId FROM #c)
        ORDER BY IIF(s.Gender = @gender, 0, 1), IIF(s.IsActive = 1, 0, 1), s.StyleId DESC;

    WITH scored AS
    (
        SELECT c.StyleId, c.SharedMaterials, c.SharedFabrics, s.SeasonCode, s.StyleNo, s.Description, s.ModelName,
               s.ImageUrl, s.SketchUrl, s.IsActive, s.Gender, s.WeaveTypeCode, ProductTypeName = pt.Name,
               CustomerCode = cu.PartnerCode, CustomerName = cu.Name,
               SameBrand = CAST(IIF(s.CustomerId = @customerId OR (@brand IS NOT NULL AND cu.BrandCode = @brand), 1, 0) AS bit),
               SameGarment = CAST(IIF(p.Category = @category, 1, 0) AS bit),
               -- overlap capped at 1: a material can weigh more on this style than on the other one
               Score = 65.0 * IIF(@total + ISNULL(c.Total, 0) - c.Shared > 0,
                                  IIF(c.Shared >= @total + ISNULL(c.Total, 0) - c.Shared, 1, c.Shared / (@total + ISNULL(c.Total, 0) - c.Shared)), 0)
                     + IIF(p.Category = @category, 20, 0)
                     + IIF(s.WeaveTypeCode = @weave, 10, 0)
                     + IIF(s.Gender = @gender, 5, 0)
        FROM #c c
        JOIN style.Style s ON s.StyleId = c.StyleId
        JOIN partner.Partner cu ON cu.PartnerId = s.CustomerId
        LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
        LEFT JOIN #ptc p ON p.ProductTypeCode = s.ProductTypeCode
    ),
    ranked AS
    (
        SELECT *, Rnk = ROW_NUMBER() OVER (PARTITION BY SameBrand ORDER BY Score DESC, IIF(IsActive = 1, 0, 1), StyleId DESC)
        FROM scored
    )
    SELECT r.StyleId, r.CustomerCode, r.CustomerName, r.SeasonCode, r.StyleNo, r.Description, r.ModelName, r.ProductTypeName, r.Gender,
           r.WeaveTypeCode, r.ImageUrl, r.SketchUrl, r.IsActive, r.SameBrand, r.SameGarment, r.SharedMaterials, r.SharedFabrics,
           Score = CAST(ROUND(r.Score, 0) AS int),
           -- what it is made of: the fabric line with the largest consumption
           MainFabric = (SELECT TOP (1) LEFT(COALESCE(b.MaterialDescription, m.Description), 160)
                         FROM style.BomLine b JOIN mat.Material m ON m.MaterialId = b.MaterialId
                         WHERE b.StyleId = r.StyleId AND b.ContentClassCode = N'FAB'
                         ORDER BY COALESCE(b.LcoConsumption, b.BrandConsumption, 0) DESC, b.LineSeq)
    FROM ranked r
    WHERE r.Rnk <= @Top AND r.Score >= 15
    ORDER BY r.SameBrand DESC, r.Score DESC, r.Rnk;
END
GO

PRINT 'Fill the gaps and similar styles procedures ready.';
GO
