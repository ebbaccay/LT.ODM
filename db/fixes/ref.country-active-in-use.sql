/*
    One-time setting (2026-11-05): only the countries actually used stay active in Settings > Reference lists >
    Countries (inactive ones are hidden from the pick lists; turn any back on there). Used = a country of origin on:

      - LT ODM: SAP suppliers (partner.Partner) and material exceptions (mat.MaterialSupplier);
      - iPLEX (old system, when its tables are in this database): style origin (iPLEXSTY_STYLE_PARM.Country_origin_id)
        and material origin (iPLEXMAT_MATERIAL_PARM.Country_origin);
      - TMS SBU products (tms_sbu_products.country_code).

    iPLEX values are mixed (VN, HKG, "INDONESIA", "China"): matched on the 2-letter code, the country name, or a few
    known spellings. Countries used but missing from the list are added. iPLEX partner addresses are not counted (where
    companies have offices, not where goods are made).

    Run once: it sets every country, so running it later would undo changes made in Settings since. No open transaction.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i fixes\ref.country-active-in-use.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

CREATE TABLE #used (Val nvarchar(200) NOT NULL);

INSERT #used SELECT CountryCode FROM partner.Partner WHERE CountryCode IS NOT NULL;
INSERT #used SELECT CountryCode FROM mat.MaterialSupplier WHERE CountryCode IS NOT NULL;
IF OBJECT_ID(N'dbo.iPLEXSTY_STYLE_PARM', N'U') IS NOT NULL
    EXEC (N'INSERT #used SELECT DISTINCT Country_origin_id FROM dbo.iPLEXSTY_STYLE_PARM WHERE NULLIF(TRIM(Country_origin_id), '''') IS NOT NULL;');
IF OBJECT_ID(N'dbo.iPLEXMAT_MATERIAL_PARM', N'U') IS NOT NULL
    EXEC (N'INSERT #used SELECT DISTINCT Country_origin FROM dbo.iPLEXMAT_MATERIAL_PARM WHERE NULLIF(TRIM(Country_origin), '''') IS NOT NULL;');
IF OBJECT_ID(N'dbo.tms_sbu_products', N'U') IS NOT NULL
    EXEC (N'INSERT #used SELECT DISTINCT country_code FROM dbo.tms_sbu_products WHERE NULLIF(TRIM(country_code), '''') IS NOT NULL;');

-- Spellings seen in the old data, and countries used there but missing from the first list.
DECLARE @alias TABLE (Val nvarchar(40) NOT NULL PRIMARY KEY, CountryCode char(2) NOT NULL);
INSERT @alias VALUES (N'HKG', 'HK'), (N'CHN', 'CN'), (N'VNM', 'VN'), (N'KHM', 'KH'), (N'IDN', 'ID'), (N'BGD', 'BD'), (N'IND', 'IN'),
    (N'INDI', 'IN'), (N'TWN', 'TW'), (N'THA', 'TH'), (N'LKA', 'LK'), (N'MYS', 'MY'), (N'PHL', 'PH'), (N'KOR', 'KR'), (N'UK', 'GB');
DECLARE @missing TABLE (CountryCode char(2) NOT NULL PRIMARY KEY, Name nvarchar(100) NOT NULL);
INSERT @missing VALUES ('ME', N'Montenegro'), ('EE', N'Estonia');

INSERT ref.Country (CountryCode, Name)
SELECT m.CountryCode, m.Name FROM @missing m
WHERE EXISTS (SELECT 1 FROM #used u WHERE UPPER(TRIM(u.Val)) = m.CountryCode)
  AND NOT EXISTS (SELECT 1 FROM ref.Country c WHERE c.CountryCode = m.CountryCode);

;WITH used AS (
    SELECT DISTINCT c.CountryCode
    FROM #used u
    LEFT JOIN @alias a ON a.Val = UPPER(TRIM(u.Val))
    JOIN ref.Country c ON c.CountryCode = UPPER(TRIM(u.Val)) OR c.Name = TRIM(u.Val) OR c.CountryCode = a.CountryCode)
UPDATE c SET IsActive = IIF(EXISTS (SELECT 1 FROM used WHERE used.CountryCode = c.CountryCode), 1, 0)
FROM ref.Country c;

SELECT Active = SUM(IIF(IsActive = 1, 1, 0)), Inactive = SUM(IIF(IsActive = 0, 1, 0)) FROM ref.Country;
SELECT ActiveCountries = STRING_AGG(CONCAT(Name, N' (', RTRIM(CountryCode), N')'), N', ') WITHIN GROUP (ORDER BY Name) FROM ref.Country WHERE IsActive = 1;
-- Values in the old data that matched no country (left as they are).
SELECT Unmatched = u.Val, Rows = COUNT(*)
FROM #used u
LEFT JOIN @alias a ON a.Val = UPPER(TRIM(u.Val))
WHERE NOT EXISTS (SELECT 1 FROM ref.Country c WHERE c.CountryCode = UPPER(TRIM(u.Val)) OR c.Name = TRIM(u.Val) OR c.CountryCode = a.CountryCode)
GROUP BY u.Val;
DROP TABLE #used;
GO
