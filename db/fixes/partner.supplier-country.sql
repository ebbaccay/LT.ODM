/*
    One-time fill (2026-11-05): a first country of origin for SAP suppliers that have none (partner.Partner.CountryCode,
    edited in Settings > Reference lists > SAP suppliers), guessed from text already in the library:

      1. the supplier's own name ("... (CAMBODIA) TRADING CO., LTD", "... VIETNAM COMPANY LIMITED"), when it names
         exactly one country (it wins: a nominated name may point to a trading office, e.g. Hong Kong).
      2. else the nominated supplier names on the supplier's BOM lines, which often end in a country ("SUE'S (VIETNAM)",
         "HUAFENG (VNM)"): the country on at least 90% of the lines that show one.

    Suppliers with no hint, or mixed hints, stay empty. Never changes a country already set. One statement per rule, no
    open transaction. Safe to run again (e.g. after an import adds suppliers). Prints what it filled.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i fixes\partner.supplier-country.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

/* Country words: in brackets (nominated names), and anywhere in a supplier name (own names). ASCII only (sqlcmd reads ANSI). */
DECLARE @w TABLE (Word nvarchar(20) NOT NULL, CountryCode char(2) NOT NULL);
INSERT @w VALUES
    (N'VIETNAM', 'VN'), (N'VIET NAM', 'VN'), (N'VNM', 'VN'), (N'CAMBODIA', 'KH'), (N'KHM', 'KH'), (N'CHINA', 'CN'), (N'CHN', 'CN'),
    (N'TAIWAN', 'TW'), (N'TWN', 'TW'), (N'KOREA', 'KR'), (N'KOR', 'KR'), (N'JAPAN', 'JP'), (N'JPN', 'JP'), (N'THAILAND', 'TH'), (N'THA', 'TH'),
    (N'INDONESIA', 'ID'), (N'IDN', 'ID'), (N'MALAYSIA', 'MY'), (N'MYS', 'MY'), (N'INDIA', 'IN'), (N'IND', 'IN'), (N'BANGLADESH', 'BD'),
    (N'BGD', 'BD'), (N'SRI LANKA', 'LK'), (N'LKA', 'LK'), (N'PHILIPPINES', 'PH'), (N'PHL', 'PH'), (N'TURKEY', 'TR'), (N'TUR', 'TR'),
    (N'ITALY', 'IT'), (N'ITA', 'IT'), (N'MYANMAR', 'MM'), (N'MMR', 'MM'), (N'PAKISTAN', 'PK'), (N'PAK', 'PK'), (N'HONG KONG', 'HK'),
    (N'HKG', 'HK'), (N'SINGAPORE', 'SG'), (N'SGP', 'SG'), (N'USA', 'US'), (N'GERMANY', 'DE'), (N'DEU', 'DE');

DECLARE @filled TABLE (PartnerId int NOT NULL PRIMARY KEY, CountryCode char(2) NOT NULL, RuleNo tinyint NOT NULL);

-- 1. The supplier's own name, when it names exactly one country (whole words: "VN" alone is not used, too short).
;WITH hits AS (
    SELECT p.PartnerId, w.CountryCode
    FROM partner.Partner p
    JOIN @w w ON LEN(w.Word) >= 4
             AND (N' ' + UPPER(TRANSLATE(p.Name, N'(),.-', N'     ')) + N' ') LIKE N'% ' + w.Word + N' %'
    WHERE p.PartnerType = 'Supplier' AND p.CountryCode IS NULL
    GROUP BY p.PartnerId, w.CountryCode),
single AS (SELECT PartnerId, CountryCode = MIN(CountryCode) FROM hits GROUP BY PartnerId HAVING COUNT(*) = 1)
UPDATE p SET CountryCode = single.CountryCode
OUTPUT inserted.PartnerId, inserted.CountryCode, 1 INTO @filled
FROM partner.Partner p JOIN single ON single.PartnerId = p.PartnerId
WHERE p.CountryCode IS NULL;

-- 2. Nominated supplier names on the supplier's BOM lines: "(VIETNAM)", "(VNM)".
;WITH hits AS (
    SELECT b.SupplierId, w.CountryCode, n = COUNT(*)
    FROM style.BomLine b
    JOIN @w w ON UPPER(b.NominatedSupplierName) LIKE N'%(' + w.Word + N')%'
    WHERE b.SupplierId IS NOT NULL
    GROUP BY b.SupplierId, w.CountryCode),
best AS (
    SELECT SupplierId, CountryCode
    FROM (SELECT *, total = SUM(n) OVER (PARTITION BY SupplierId), rk = ROW_NUMBER() OVER (PARTITION BY SupplierId ORDER BY n DESC) FROM hits) h
    WHERE rk = 1 AND n * 1.0 / total >= 0.9)
UPDATE p SET CountryCode = best.CountryCode
OUTPUT inserted.PartnerId, inserted.CountryCode, 2 INTO @filled
FROM partner.Partner p JOIN best ON best.SupplierId = p.PartnerId
WHERE p.PartnerType = 'Supplier' AND p.CountryCode IS NULL;

SELECT RuleNo, Basis = IIF(RuleNo = 1, 'its own name', 'nominated supplier names on its BOM lines'), Suppliers = COUNT(*)
FROM @filled GROUP BY RuleNo ORDER BY RuleNo;
SELECT f.CountryCode, Suppliers = COUNT(*) FROM @filled f GROUP BY f.CountryCode ORDER BY COUNT(*) DESC;
SELECT SuppliersWithoutCountry = COUNT(*) FROM partner.Partner WHERE PartnerType = 'Supplier' AND CountryCode IS NULL;
GO
