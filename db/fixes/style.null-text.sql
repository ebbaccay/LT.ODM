/*
    One-time repair: the source export writes database nulls as the text "NULL", and imports before the fix in
    StyleWorkbookReader.AsText stored that text as real values (a material type, UOM and supplier coded 'NULL',
    descriptions reading "NULL"). This turns them back into NULL and removes the 'NULL' reference rows.
    Audit columns are left as they are (a data repair, not a user change). Staging rows keep the workbook text.
    Idempotent: does nothing once clean.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i fixes\style.null-text.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @nullSupplier int = (SELECT PartnerId FROM partner.Partner WHERE PartnerType = 'Supplier' AND PartnerCode = N'NULL');

UPDATE style.BomLine SET SupplierId = NULL WHERE SupplierId = @nullSupplier;
UPDATE style.BomLine SET MaterialTypeCode = NULL WHERE MaterialTypeCode = N'NULL';
UPDATE style.BomLine SET UomCode = NULL WHERE UomCode = N'NULL';
UPDATE style.BomLine SET MaterialDescription = NULL WHERE MaterialDescription = N'NULL';
UPDATE style.BomLine SET NominatedSupplierCode = NULL WHERE NominatedSupplierCode = N'NULL';
UPDATE style.BomLine SET NominatedSupplierName = NULL WHERE NominatedSupplierName = N'NULL';
UPDATE style.BomLineColorway SET MaterialColorCode = NULL WHERE MaterialColorCode = N'NULL';
UPDATE style.BomLineColorway SET MaterialColorDescription = NULL WHERE MaterialColorDescription = N'NULL';
UPDATE style.Colorway SET ColorwayName = NULL WHERE ColorwayName = N'NULL';
UPDATE mat.Material SET MaterialTypeCode = NULL WHERE MaterialTypeCode = N'NULL';
UPDATE mat.Material SET Description = NULL WHERE Description = N'NULL';

DELETE partner.Partner WHERE PartnerId = @nullSupplier;
DELETE ref.MaterialType WHERE MaterialTypeCode = N'NULL';
DELETE ref.Uom WHERE UomCode = N'NULL';

COMMIT;
PRINT 'NULL text repaired.';
GO
