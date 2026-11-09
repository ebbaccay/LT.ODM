/*
    One-time fill (2026-11-05): BOM lines imported with a nominated supplier (the brand's, Ad_Supplier_ID) but no SAP
    supplier get the SAP supplier the library already pairs with that nominated supplier:

      1. same material: the supplier most often used on the material's other lines with the same nominated supplier code
         (only when one supplier is clearly first; a tie is left empty). Same rule as the BOM line form's fill-in.
      2. else, any material: the nominated supplier code goes to one supplier on at least 95% of its lines (at least 3).

    Lines whose nominated supplier goes to several suppliers, or was never seen with one, stay empty (pick them in the
    BOM line form). Audit columns are left as they are (a data fill, not a user change). One statement, no open
    transaction. Idempotent: only lines still without a supplier are touched, so running it again after an import fills
    the new lines the same way. Prints what it filled per rule.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i fixes\style.supplier-from-nominated.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @filled TABLE (BomLineId int NOT NULL PRIMARY KEY, SupplierId int NOT NULL, RuleNo tinyint NOT NULL);

;WITH pairs AS (
    SELECT MaterialId, NominatedSupplierCode, SupplierId, n = COUNT(*)
    FROM style.BomLine
    WHERE SupplierId IS NOT NULL AND NominatedSupplierCode IS NOT NULL
    GROUP BY MaterialId, NominatedSupplierCode, SupplierId),
sameMaterial AS (
    SELECT MaterialId, NominatedSupplierCode, SupplierId
    FROM (SELECT *, rk = RANK() OVER (PARTITION BY MaterialId, NominatedSupplierCode ORDER BY n DESC),
                 ties = COUNT(*) OVER (PARTITION BY MaterialId, NominatedSupplierCode, n)
          FROM pairs) r
    WHERE rk = 1 AND ties = 1),
byCode AS (
    SELECT NominatedSupplierCode, SupplierId
    FROM (SELECT NominatedSupplierCode, SupplierId, n = SUM(n),
                 total = SUM(SUM(n)) OVER (PARTITION BY NominatedSupplierCode),
                 rk = ROW_NUMBER() OVER (PARTITION BY NominatedSupplierCode ORDER BY SUM(n) DESC)
          FROM pairs GROUP BY NominatedSupplierCode, SupplierId) c
    WHERE rk = 1 AND total >= 3 AND n * 1.0 / total >= 0.95)
UPDATE b
SET SupplierId = COALESCE(m.SupplierId, c.SupplierId)
OUTPUT inserted.BomLineId, inserted.SupplierId, IIF(m.SupplierId IS NOT NULL, 1, 2) INTO @filled
FROM style.BomLine b
LEFT JOIN sameMaterial m ON m.MaterialId = b.MaterialId AND m.NominatedSupplierCode = b.NominatedSupplierCode
LEFT JOIN byCode c ON c.NominatedSupplierCode = b.NominatedSupplierCode
WHERE b.SupplierId IS NULL AND b.NominatedSupplierCode IS NOT NULL
  AND COALESCE(m.SupplierId, c.SupplierId) IS NOT NULL;

SELECT RuleNo, Basis = IIF(RuleNo = 1, 'same material, same nominated supplier', 'nominated supplier -> one supplier (95%+)'), Lines = COUNT(*)
FROM @filled GROUP BY RuleNo ORDER BY RuleNo;

SELECT StillWithoutSupplier = COUNT(*) FROM style.BomLine WHERE SupplierId IS NULL AND NominatedSupplierCode IS NOT NULL;
GO
