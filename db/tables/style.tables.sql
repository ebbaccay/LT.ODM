/*
    LT ODM Style Library - styles, colorways (articles), BOMs and their reference data
    Target: SQL Server 2025, database compatibility level 170

      ref.*      code lists (seasons, business units, product types, weave types, material types, content classes, UOMs)
                 and the concept pick lists (target markets, trend tags, product categories)
      partner.*  customers and suppliers
      mat.*      material master (one row per material code)
      style.*    Style --< Colorway
                   |--< StyleHistory >-- Style   (carry-over / variant: which earlier style it was reused from)
                   \--< BomLine >-- mat.Material
                          \--< BomLineColorway >-- Colorway   (which colorways use the line, and the material colour in each)
      staging.*  Style Library workbook uploads, checked before they are committed (see style.procedures.sql)

    A BOM line is one distinct part + material + consumption + UOM + supplier on a style; the workbook repeats it once per
    colorway, which becomes BomLineColorway rows (this replaces the iPLEX Article1..Article60 columns).
    Colorway = one style in one colour (adidas calls it an article; ColorwayCode holds the customer's number for it).
    Audit columns (CreatedBy/CreatedUtc/UpdatedBy/UpdatedUtc) hold the LT ODM user name; RowVer guards concurrent edits.
    Image columns hold the app-relative URL of an uploaded file (e.g. /api/v1/style-library/images/<name>), or NULL.

    No foreign key cascades: styles are replaced or removed by the procedures, children first.
    Idempotent: safe to run more than once.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) < 170
    THROW 50000, 'Compatibility level 170 (SQL Server 2025) is required. Run: ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;', 1;
GO

IF SCHEMA_ID(N'ref')     IS NULL EXEC (N'CREATE SCHEMA ref AUTHORIZATION dbo;');
IF SCHEMA_ID(N'partner') IS NULL EXEC (N'CREATE SCHEMA partner AUTHORIZATION dbo;');
IF SCHEMA_ID(N'mat')     IS NULL EXEC (N'CREATE SCHEMA mat AUTHORIZATION dbo;');
IF SCHEMA_ID(N'style')   IS NULL EXEC (N'CREATE SCHEMA style AUTHORIZATION dbo;');
IF SCHEMA_ID(N'staging') IS NULL EXEC (N'CREATE SCHEMA staging AUTHORIZATION dbo;');
GO

/* ================================ ref ================================ */

/* Season terms in calendar order within a year, so seasons can be sorted ('2026-FW' comes before '2027-SS'). */
IF OBJECT_ID(N'ref.SeasonTerm', N'U') IS NULL
CREATE TABLE ref.SeasonTerm
(
    Term      nvarchar(8)  NOT NULL,
    Name      nvarchar(32) NOT NULL,
    SortOrder smallint     NOT NULL,
    CONSTRAINT PK_ref_SeasonTerm PRIMARY KEY CLUSTERED (Term),
    CONSTRAINT UQ_ref_SeasonTerm_SortOrder UNIQUE (SortOrder)
);
GO

/* '2027-SS': year + term. Imported seasons may have other names ('26FW', 'FA26', '26Q2', '999'): they are kept as
   written until normalised, with SeasonYear / Term filled when the name shows them (ref.fn_SeasonParts), else NULL. */
IF OBJECT_ID(N'ref.Season', N'U') IS NULL
CREATE TABLE ref.Season
(
    SeasonCode   nvarchar(16)  NOT NULL,
    SeasonYear   smallint      NULL,
    Term         nvarchar(8)   NULL,
    Description  nvarchar(64)  NULL,
    IsActive     bit           NOT NULL CONSTRAINT DF_ref_Season_IsActive DEFAULT (1),

    CONSTRAINT PK_ref_Season PRIMARY KEY CLUSTERED (SeasonCode),
    CONSTRAINT FK_ref_Season_Term FOREIGN KEY (Term) REFERENCES ref.SeasonTerm (Term),
    CONSTRAINT CK_ref_Season_Code CHECK (LEN(SeasonCode) > 0 AND CHARINDEX(N'|', SeasonCode) = 0)
);
GO
/* Changed 2026-10-14: seasons keep the name they were imported with; year and term are optional. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ref_Season_Code' AND definition LIKE N'%REGEXP_LIKE%')
BEGIN
    ALTER TABLE ref.Season DROP CONSTRAINT CK_ref_Season_Code;
    ALTER TABLE ref.Season ADD CONSTRAINT CK_ref_Season_Code CHECK (LEN(SeasonCode) > 0 AND CHARINDEX(N'|', SeasonCode) = 0);
END
IF COLUMNPROPERTY(OBJECT_ID(N'ref.Season'), N'Term', 'AllowsNull') = 0
BEGIN
    ALTER TABLE ref.Season DROP CONSTRAINT FK_ref_Season_Term;
    ALTER TABLE ref.Season ALTER COLUMN SeasonYear smallint NULL;
    ALTER TABLE ref.Season ALTER COLUMN Term nvarchar(8) NULL;
    ALTER TABLE ref.Season ADD CONSTRAINT FK_ref_Season_Term FOREIGN KEY (Term) REFERENCES ref.SeasonTerm (Term);
END
GO

/*
    Year and term read from a season name, for sorting seasons that are not normalised yet:
      '2027-SS', '26FW', 'FA26' -> year + term (the term only if it is on ref.SeasonTerm); '26Q2', 'Q227' -> year only;
      anything else ('999', 'CR') -> NULL.
*/
CREATE OR ALTER FUNCTION ref.fn_SeasonParts (@SeasonCode nvarchar(16))
RETURNS TABLE
AS RETURN
SELECT p.SeasonYear,
       Term = (SELECT t.Term FROM ref.SeasonTerm t WHERE t.Term = p.Term)
FROM (SELECT c = UPPER(TRIM(@SeasonCode))) x
CROSS APPLY (SELECT
    SeasonYear = CASE
        WHEN REGEXP_LIKE(x.c, N'^[0-9]{4}-[A-Z]{2,4}$')         THEN TRY_CONVERT(smallint, LEFT(x.c, 4))
        WHEN REGEXP_LIKE(x.c, N'^[0-9]{2}([A-Z]{2,4}|Q[1-4])$') THEN 2000 + TRY_CONVERT(smallint, LEFT(x.c, 2))
        WHEN REGEXP_LIKE(x.c, N'^([A-Z]{2,4}|Q[1-4])[0-9]{2}$') THEN 2000 + TRY_CONVERT(smallint, RIGHT(x.c, 2))
    END,
    Term = CASE
        WHEN REGEXP_LIKE(x.c, N'^[0-9]{4}-[A-Z]{2,4}$') THEN SUBSTRING(x.c, 6, 4)
        WHEN REGEXP_LIKE(x.c, N'^[0-9]{2}[A-Z]{2,4}$')  THEN SUBSTRING(x.c, 3, 4)
        WHEN REGEXP_LIKE(x.c, N'^[A-Z]{2,4}[0-9]{2}$')  THEN LEFT(x.c, LEN(x.c) - 2)
    END) p;
GO

IF OBJECT_ID(N'ref.BusinessUnit', N'U') IS NULL
CREATE TABLE ref.BusinessUnit
(
    BusinessUnitCode nvarchar(16)  NOT NULL,
    Name             nvarchar(100) NOT NULL,
    IsActive         bit           NOT NULL CONSTRAINT DF_ref_BusinessUnit_IsActive DEFAULT (1),
    CONSTRAINT PK_ref_BusinessUnit PRIMARY KEY CLUSTERED (BusinessUnitCode)
);
GO

IF OBJECT_ID(N'ref.ProductType', N'U') IS NULL
CREATE TABLE ref.ProductType
(
    ProductTypeCode nvarchar(40)  NOT NULL,
    Name            nvarchar(100) NOT NULL,
    IsActive        bit           NOT NULL CONSTRAINT DF_ref_ProductType_IsActive DEFAULT (1),
    CONSTRAINT PK_ref_ProductType PRIMARY KEY CLUSTERED (ProductTypeCode)
);
GO

IF OBJECT_ID(N'ref.WeaveType', N'U') IS NULL
CREATE TABLE ref.WeaveType
(
    WeaveTypeCode nvarchar(8)  NOT NULL,
    Name          nvarchar(50) NOT NULL,
    CONSTRAINT PK_ref_WeaveType PRIMARY KEY CLUSTERED (WeaveTypeCode)
);
GO

IF OBJECT_ID(N'ref.MaterialType', N'U') IS NULL
CREATE TABLE ref.MaterialType
(
    MaterialTypeCode nvarchar(16)  NOT NULL,
    Name             nvarchar(100) NOT NULL,
    CONSTRAINT PK_ref_MaterialType PRIMARY KEY CLUSTERED (MaterialTypeCode)
);
GO

/* BOM sections: FAB fabric, TRI trims, ACC accessories, LNP labels & packaging, ART artwork. */
IF OBJECT_ID(N'ref.ContentClass', N'U') IS NULL
CREATE TABLE ref.ContentClass
(
    ContentClassCode nvarchar(8)  NOT NULL,
    Name             nvarchar(50) NOT NULL,
    SortOrder        smallint     NOT NULL,
    CONSTRAINT PK_ref_ContentClass PRIMARY KEY CLUSTERED (ContentClassCode)
);
GO

/* Units are stored lower-case ('pc', 'm', 'yd'); the import folds case. */
IF OBJECT_ID(N'ref.Uom', N'U') IS NULL
CREATE TABLE ref.Uom
(
    UomCode nvarchar(8)  NOT NULL,
    Name    nvarchar(50) NOT NULL,
    CONSTRAINT PK_ref_Uom PRIMARY KEY CLUSTERED (UomCode)
);
GO

/*
    Pick lists for concepts (added 2026-10-28, roadmap 5.6; Settings > Reference lists). Concepts only: styles do not use
    them. A first set is added once when a list is empty; edit them in Settings after that.
      ref.TargetMarket    : who the concept is for (Gen Z urban, premium outdoor, ...).
      ref.TrendTag        : trends a concept follows (athleisure, sustainable, ...).
      ref.ProductCategory : the kind of garment, with the same codes as the garment groups of What to offer and the hit
                            rate (style.fn_GarmentCategory), so concepts and offers count garments alike.
*/
IF OBJECT_ID(N'ref.TargetMarket', N'U') IS NULL
CREATE TABLE ref.TargetMarket
(
    TargetMarketCode nvarchar(16)  NOT NULL,
    Name             nvarchar(100) NOT NULL,
    IsActive         bit           NOT NULL CONSTRAINT DF_ref_TargetMarket_IsActive DEFAULT (1),
    CONSTRAINT PK_ref_TargetMarket PRIMARY KEY CLUSTERED (TargetMarketCode)
);
GO
IF NOT EXISTS (SELECT 1 FROM ref.TargetMarket)
    INSERT ref.TargetMarket (TargetMarketCode, Name) VALUES
        (N'GENZ', N'Gen Z urban'), (N'MILLENNIAL', N'Millennial premium'), (N'MASS', N'Mass market'),
        (N'OUTDOOR', N'Premium outdoor'), (N'PERFORMANCE', N'Performance sport'), (N'FAST', N'Fast fashion'), (N'KIDS', N'Kids and teens');
GO

IF OBJECT_ID(N'ref.TrendTag', N'U') IS NULL
CREATE TABLE ref.TrendTag
(
    TrendTagCode nvarchar(40)  NOT NULL,
    Name         nvarchar(100) NOT NULL,
    IsActive     bit           NOT NULL CONSTRAINT DF_ref_TrendTag_IsActive DEFAULT (1),
    CONSTRAINT PK_ref_TrendTag PRIMARY KEY CLUSTERED (TrendTagCode)
);
GO
IF NOT EXISTS (SELECT 1 FROM ref.TrendTag)
    INSERT ref.TrendTag (TrendTagCode, Name) VALUES
        (N'ATHLEISURE', N'Athleisure'), (N'SUSTAINABLE', N'Sustainable'), (N'RECYCLED', N'Recycled materials'),
        (N'URBAN-OUTDOOR', N'Urban outdoor'), (N'GORPCORE', N'Gorpcore'), (N'STREETWEAR', N'Streetwear'), (N'RETRO', N'Retro sport'),
        (N'MINIMALIST', N'Minimalist'), (N'TECHNICAL', N'Technical performance'), (N'COMFORT', N'Comfort / lounge');
GO

IF OBJECT_ID(N'ref.ProductCategory', N'U') IS NULL
CREATE TABLE ref.ProductCategory
(
    ProductCategoryCode nvarchar(20)  NOT NULL,
    Name                nvarchar(100) NOT NULL,
    IsActive            bit           NOT NULL CONSTRAINT DF_ref_ProductCategory_IsActive DEFAULT (1),
    CONSTRAINT PK_ref_ProductCategory PRIMARY KEY CLUSTERED (ProductCategoryCode)
);
GO
IF NOT EXISTS (SELECT 1 FROM ref.ProductCategory)
    INSERT ref.ProductCategory (ProductCategoryCode, Name) VALUES
        (N'JACKET', N'Jackets and coats'), (N'VEST', N'Vests'), (N'HOODIE', N'Hoodies'), (N'PULLOVER', N'Pullovers and sweatshirts'),
        (N'POLO', N'Polos'), (N'TEE', N'T-shirts'), (N'SHIRT', N'Shirts'), (N'TANK', N'Tanks'), (N'TOP', N'Tops'), (N'PANTS', N'Pants'),
        (N'SHORTS', N'Shorts'), (N'TIGHTS', N'Tights and leggings'), (N'SKIRT', N'Skirts'), (N'DRESS', N'Dresses'), (N'BRA', N'Bras'),
        (N'UNDERWEAR', N'Underwear'), (N'SLEEPWEAR', N'Sleepwear and lounge'), (N'SET', N'Sets'), (N'ACCESSORY', N'Accessories');
GO

/* ================================ partner ================================ */

/* Customers (ADI, SKE, TMS, ...) and suppliers (SAP vendor codes). Code is unique per type. */
IF OBJECT_ID(N'partner.Partner', N'U') IS NULL
CREATE TABLE partner.Partner
(
    PartnerId    int IDENTITY(1, 1) NOT NULL,
    PartnerType  varchar(16)        NOT NULL,
    PartnerCode  nvarchar(32)       NOT NULL,
    Name         nvarchar(150)      NOT NULL,
    IsActive     bit                NOT NULL CONSTRAINT DF_partner_Partner_IsActive DEFAULT (1),
    CreatedBy    nvarchar(64)       NOT NULL,
    CreatedUtc   datetime2(3)       NOT NULL CONSTRAINT DF_partner_Partner_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy    nvarchar(64)       NULL,
    UpdatedUtc   datetime2(3)       NULL,
    RowVer       rowversion         NOT NULL,

    CONSTRAINT PK_partner_Partner PRIMARY KEY CLUSTERED (PartnerId),
    CONSTRAINT UQ_partner_Partner_Code UNIQUE (PartnerType, PartnerCode),
    CONSTRAINT CK_partner_Partner_Type CHECK (PartnerType IN ('Customer', 'Supplier', 'Factory'))
);
GO

/*
    Brands (added 2026-10-15): customer accounts that stay separate customers but belong to one brand
    (11 POLO accounts -> brand POLO; the Skechers accounts -> SKE). Set from the workbook (Customer_Brand) or the
    import's Questions panel.
*/
IF OBJECT_ID(N'ref.Brand', N'U') IS NULL
CREATE TABLE ref.Brand
(
    BrandCode  nvarchar(32)   NOT NULL,
    Name       nvarchar(100)  NOT NULL,
    IsActive   bit            NOT NULL CONSTRAINT DF_ref_Brand_IsActive DEFAULT (1),
    CONSTRAINT PK_ref_Brand PRIMARY KEY CLUSTERED (BrandCode),
    CONSTRAINT CK_ref_Brand_Code CHECK (LEN(BrandCode) > 0 AND CHARINDEX(N'|', BrandCode) = 0)
);
GO
IF COL_LENGTH(N'partner.Partner', N'BrandCode') IS NULL
    ALTER TABLE partner.Partner ADD BrandCode nvarchar(32) NULL CONSTRAINT FK_partner_Partner_Brand REFERENCES ref.Brand (BrandCode);
GO
IF INDEXPROPERTY(OBJECT_ID(N'partner.Partner'), N'IX_partner_Partner_Brand', 'IndexId') IS NULL
    CREATE INDEX IX_partner_Partner_Brand ON partner.Partner (BrandCode) WHERE BrandCode IS NOT NULL;
GO

/* ================================ mat ================================ */

IF OBJECT_ID(N'mat.Material', N'U') IS NULL
CREATE TABLE mat.Material
(
    MaterialId       int IDENTITY(1, 1) NOT NULL,
    MaterialCode     nvarchar(64)       NOT NULL,
    Description      nvarchar(4000)     NULL,
    MaterialTypeCode nvarchar(16)       NULL,
    ContentClassCode nvarchar(8)        NULL,
    CreatedBy        nvarchar(64)       NOT NULL,
    CreatedUtc       datetime2(3)       NOT NULL CONSTRAINT DF_mat_Material_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy        nvarchar(64)       NULL,
    UpdatedUtc       datetime2(3)       NULL,
    RowVer           rowversion         NOT NULL,

    CONSTRAINT PK_mat_Material PRIMARY KEY CLUSTERED (MaterialId),
    CONSTRAINT UQ_mat_Material_Code UNIQUE (MaterialCode),
    CONSTRAINT FK_mat_Material_Type FOREIGN KEY (MaterialTypeCode) REFERENCES ref.MaterialType (MaterialTypeCode),
    CONSTRAINT FK_mat_Material_Class FOREIGN KEY (ContentClassCode) REFERENCES ref.ContentClass (ContentClassCode)
);
GO

/* ================================ style ================================ */

IF OBJECT_ID(N'style.Style', N'U') IS NULL
CREATE TABLE style.Style
(
    StyleId             int IDENTITY(1, 1) NOT NULL,
    CustomerId          int                NOT NULL,
    SeasonCode          nvarchar(16)       NOT NULL,
    StyleNo             nvarchar(100)      NOT NULL,
    -- Style number without the reuse suffix ('S2508MR1212_SS27' -> 'S2508MR1212'): groups a style family.
    BaseStyleNo         AS (CAST(IIF(CHARINDEX(N'_', StyleNo) > 1, LEFT(StyleNo, CHARINDEX(N'_', StyleNo) - 1), StyleNo) AS nvarchar(100))) PERSISTED,
    Description         nvarchar(400)      NULL,
    ModelCode           nvarchar(60)       NULL,
    ModelName           nvarchar(100)      NULL,
    WeaveTypeCode       nvarchar(8)        NULL,
    ProductTypeCode     nvarchar(40)       NULL,
    Gender              nvarchar(16)       NULL,
    GarmentLeadTimeDays smallint           NULL,
    BusinessUnitCode    nvarchar(16)       NULL,
    SketchUrl           nvarchar(500)      NULL,
    ImageUrl            nvarchar(500)      NULL,
    CustomerName        nvarchar(150)      NULL,   -- Cust_Name from the source system
    IsActive            bit                NOT NULL CONSTRAINT DF_style_Style_IsActive DEFAULT (1),   -- 0 = kept for history, not in use
    SourceCreatedUtc    datetime2(3)       NULL,   -- Create_dt from the source system
    ImportBatchId       int                NULL,   -- last import that wrote this style
    CreatedBy           nvarchar(64)       NOT NULL,
    CreatedUtc          datetime2(3)       NOT NULL CONSTRAINT DF_style_Style_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy           nvarchar(64)       NULL,
    UpdatedUtc          datetime2(3)       NULL,
    RowVer              rowversion         NOT NULL,

    CONSTRAINT PK_style_Style PRIMARY KEY CLUSTERED (StyleId),
    CONSTRAINT UQ_style_Style_Key UNIQUE (CustomerId, SeasonCode, StyleNo),
    CONSTRAINT FK_style_Style_Customer FOREIGN KEY (CustomerId) REFERENCES partner.Partner (PartnerId),
    CONSTRAINT FK_style_Style_Season FOREIGN KEY (SeasonCode) REFERENCES ref.Season (SeasonCode),
    CONSTRAINT FK_style_Style_Weave FOREIGN KEY (WeaveTypeCode) REFERENCES ref.WeaveType (WeaveTypeCode),
    CONSTRAINT FK_style_Style_ProductType FOREIGN KEY (ProductTypeCode) REFERENCES ref.ProductType (ProductTypeCode),
    CONSTRAINT FK_style_Style_BusinessUnit FOREIGN KEY (BusinessUnitCode) REFERENCES ref.BusinessUnit (BusinessUnitCode),
    CONSTRAINT CK_style_Style_Gender CHECK (Gender IN (N'MALE', N'FEMALE', N'UNISEX', N'KIDS')),
    CONSTRAINT CK_style_Style_LeadTime CHECK (GarmentLeadTimeDays BETWEEN 0 AND 730)
);
GO
/* Added 2026-10-07: active / inactive tag (existing styles become active). */
IF COL_LENGTH(N'style.Style', N'IsActive') IS NULL
    ALTER TABLE style.Style ADD IsActive bit NOT NULL CONSTRAINT DF_style_Style_IsActive DEFAULT (1);
/* Added 2026-10-14: the customer account's name as the source system writes it (Cust_Name), kept per style because the
   customer code may later be mapped to a broader customer (several Skechers accounts -> SKE). */
IF COL_LENGTH(N'style.Style', N'CustomerName') IS NULL
    ALTER TABLE style.Style ADD CustomerName nvarchar(150) NULL;
GO
/* Widened 2026-10-14 (GO export: style numbers up to 49 characters, room for more): StyleNo 40 -> 100. Indexes and the
   unique key on StyleNo widen in place; only the computed BaseStyleNo is rebuilt, so its index is dropped here and
   re-created below. */
IF COL_LENGTH(N'style.Style', N'StyleNo') < 200
BEGIN
    IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_Family', 'IndexId') IS NOT NULL
        DROP INDEX IX_style_Style_Family ON style.Style;
    ALTER TABLE style.Style DROP COLUMN BaseStyleNo;
    ALTER TABLE style.Style ALTER COLUMN StyleNo nvarchar(100) NOT NULL;
    ALTER TABLE style.Style ADD BaseStyleNo AS (CAST(IIF(CHARINDEX(N'_', StyleNo) > 1, LEFT(StyleNo, CHARINDEX(N'_', StyleNo) - 1), StyleNo) AS nvarchar(100))) PERSISTED;
END
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_Season', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_Season ON style.Style (SeasonCode, CustomerId) INCLUDE (StyleNo, ModelName);
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_StyleNo', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_StyleNo ON style.Style (StyleNo);
/* Styles list (style.usp_Style_List): every filter column, so finding a page never reads the wide rows.
   Rebuilt in place when an older copy lacks IsActive. */
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_List', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_List ON style.Style (SeasonCode, StyleNo)
        INCLUDE (CustomerId, BusinessUnitCode, ProductTypeCode, WeaveTypeCode, Gender, ModelCode, ModelName, IsActive);
ELSE IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                    WHERE i.object_id = OBJECT_ID(N'style.Style') AND i.name = N'IX_style_Style_List'
                      AND ic.column_id = COLUMNPROPERTY(i.object_id, N'IsActive', 'ColumnId'))
    CREATE INDEX IX_style_Style_List ON style.Style (SeasonCode, StyleNo)
        INCLUDE (CustomerId, BusinessUnitCode, ProductTypeCode, WeaveTypeCode, Gender, ModelCode, ModelName, IsActive)
        WITH (DROP_EXISTING = ON);
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_Family', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_Family ON style.Style (CustomerId, BaseStyleNo, ModelCode) INCLUDE (SeasonCode, StyleNo);
GO
/* Added 2026-10-28 (Style Packages, roadmap 5.12): a copy made for another customer (another brand's style offered as a new
   style) points at exactly the style it was copied from, a suffixed version too. Separate from StyleHistory, which links
   reuse within the same customer. Set by pack.usp_PackageItem_AddCopy. */
IF COL_LENGTH(N'style.Style', N'OriginStyleId') IS NULL
    ALTER TABLE style.Style ADD OriginStyleId int NULL CONSTRAINT FK_style_Style_Origin REFERENCES style.Style (StyleId);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_Origin', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_Origin ON style.Style (OriginStyleId) WHERE OriginStyleId IS NOT NULL;
GO

/*
    Style history: a style reused from an earlier one. Customers reuse a style by adding a suffix
    (adidas 'S2508MR1212_FW26' -> 'S2508MR1212_SS27'), or keep the number in a new season (Skechers 'P224M053').
      CarryOver = reused in a later season; Variant = another version in the same season ('_B_GRADE', '_H', '_L').
    One row per reused style (StyleId), pointing at the style it came from, so a style's full history is the chain
    of SourceStyleId links. LinkSource says who made the link:
      Import = the import, for styles with the same customer, base style number and model (rebuilt on each import);
      Auto   = by style number: a version that adds letters to another style's number ('S2808MR0000A' from
               'S2808MR0000'), whatever its model (style.usp_StyleHistory_LinkByNumber; never replaces a link);
      Manual = set in the app; never replaced.
*/
IF OBJECT_ID(N'style.StyleHistory', N'U') IS NULL
CREATE TABLE style.StyleHistory
(
    StyleId        int            NOT NULL,
    SourceStyleId  int            NOT NULL,
    Relation       varchar(10)    NOT NULL,
    Suffix         nvarchar(100)  NULL,       -- what was added to the base style number, e.g. 'SS27', 'B_GRADE_FW26'
    LinkSource     varchar(8)     NOT NULL CONSTRAINT DF_style_StyleHistory_LinkSource DEFAULT ('Import'),
    Note           nvarchar(400)  NULL,
    CreatedBy      nvarchar(64)   NOT NULL,
    CreatedUtc     datetime2(3)   NOT NULL CONSTRAINT DF_style_StyleHistory_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy      nvarchar(64)   NULL,
    UpdatedUtc     datetime2(3)   NULL,
    RowVer         rowversion     NOT NULL,

    CONSTRAINT PK_style_StyleHistory PRIMARY KEY CLUSTERED (StyleId),
    CONSTRAINT FK_style_StyleHistory_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId),
    CONSTRAINT FK_style_StyleHistory_Source FOREIGN KEY (SourceStyleId) REFERENCES style.Style (StyleId),
    CONSTRAINT CK_style_StyleHistory_NotSelf CHECK (StyleId <> SourceStyleId),
    CONSTRAINT CK_style_StyleHistory_Relation CHECK (Relation IN ('CarryOver', 'Variant')),
    CONSTRAINT CK_style_StyleHistory_LinkSource CHECK (LinkSource IN ('Import', 'Auto', 'Manual'))
);
GO
-- Databases created before 'Auto' existed.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_style_StyleHistory_LinkSource' AND definition NOT LIKE N'%Auto%')
BEGIN
    ALTER TABLE style.StyleHistory DROP CONSTRAINT CK_style_StyleHistory_LinkSource;
    ALTER TABLE style.StyleHistory ADD CONSTRAINT CK_style_StyleHistory_LinkSource CHECK (LinkSource IN ('Import', 'Auto', 'Manual'));
END
GO
/* Widened 2026-10-14 with StyleNo. */
IF COL_LENGTH(N'style.StyleHistory', N'Suffix') < 200
    ALTER TABLE style.StyleHistory ALTER COLUMN Suffix nvarchar(100) NULL;
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.StyleHistory'), N'IX_style_StyleHistory_Source', 'IndexId') IS NULL
    CREATE INDEX IX_style_StyleHistory_Source ON style.StyleHistory (SourceStyleId);
GO

/* Colorways. ColorwayCode is unique within a style (the same code can appear on other styles, e.g. a carry-over). */
IF OBJECT_ID(N'style.Colorway', N'U') IS NULL
CREATE TABLE style.Colorway
(
    ColorwayId          int IDENTITY(1, 1) NOT NULL,
    StyleId             int                NOT NULL,
    SortOrder           smallint           NOT NULL,
    ColorwayCode        nvarchar(100)      NOT NULL,
    ColorwayName        nvarchar(150)      NULL,
    Status              varchar(10)        NOT NULL CONSTRAINT DF_style_Colorway_Status DEFAULT ('INRANGE'),
    ImageUrl            nvarchar(500)      NULL,
    CreatedBy           nvarchar(64)       NOT NULL,
    CreatedUtc          datetime2(3)       NOT NULL CONSTRAINT DF_style_Colorway_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy           nvarchar(64)       NULL,
    UpdatedUtc          datetime2(3)       NULL,
    RowVer              rowversion         NOT NULL,

    CONSTRAINT PK_style_Colorway PRIMARY KEY CLUSTERED (ColorwayId),
    CONSTRAINT UQ_style_Colorway_Code UNIQUE (StyleId, ColorwayCode),
    CONSTRAINT FK_style_Colorway_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId),
    CONSTRAINT CK_style_Colorway_Status CHECK (Status IN ('INRANGE', 'DROPPED'))
);
GO
/* Widened 2026-10-14 (GO export: article codes up to 30 characters, room for more): ColorwayCode 20 -> 100
   (indexes widen in place). */
IF COL_LENGTH(N'style.Colorway', N'ColorwayCode') < 200
    ALTER TABLE style.Colorway ALTER COLUMN ColorwayCode nvarchar(100) NOT NULL;
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.Colorway'), N'IX_style_Colorway_Code', 'IndexId') IS NULL
    CREATE INDEX IX_style_Colorway_Code ON style.Colorway (ColorwayCode);
GO

IF OBJECT_ID(N'style.BomLine', N'U') IS NULL
CREATE TABLE style.BomLine
(
    BomLineId           int IDENTITY(1, 1) NOT NULL,
    StyleId             int                NOT NULL,
    LineSeq              int                NOT NULL,   -- display order within the style
    PartNo              int                NULL,
    MaterialId          int                NOT NULL,
    MaterialDescription nvarchar(4000)     NULL,       -- as written on this BOM (can differ from the material master)
    MaterialTypeCode    nvarchar(16)       NULL,
    ContentClassCode    nvarchar(8)        NULL,
    NominatedSupplierCode nvarchar(32)     NULL,       -- Ad_Supplier_ID: the brand's nominated supplier
    NominatedSupplierName nvarchar(100)    NULL,
    SupplierId          int                NULL,       -- SAP vendor (partner.Partner, type Supplier)
    LcoConsumption      decimal(18, 6)     NULL,
    BrandConsumption    decimal(18, 6)     NULL,       -- AD_Consump
    UomCode             nvarchar(8)        NULL,
    ConsumptionAllowance decimal(18, 6)    NULL,       -- ASAD_Consump_Allowance
    ImageUrl            nvarchar(500)      NULL,
    CreatedBy           nvarchar(64)       NOT NULL,
    CreatedUtc          datetime2(3)       NOT NULL CONSTRAINT DF_style_BomLine_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy           nvarchar(64)       NULL,
    UpdatedUtc          datetime2(3)       NULL,
    RowVer              rowversion         NOT NULL,

    CONSTRAINT PK_style_BomLine PRIMARY KEY CLUSTERED (BomLineId),
    CONSTRAINT UQ_style_BomLine_LineSeq UNIQUE (StyleId, LineSeq),
    CONSTRAINT FK_style_BomLine_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId),
    CONSTRAINT FK_style_BomLine_Material FOREIGN KEY (MaterialId) REFERENCES mat.Material (MaterialId),
    CONSTRAINT FK_style_BomLine_Type FOREIGN KEY (MaterialTypeCode) REFERENCES ref.MaterialType (MaterialTypeCode),
    CONSTRAINT FK_style_BomLine_Class FOREIGN KEY (ContentClassCode) REFERENCES ref.ContentClass (ContentClassCode),
    CONSTRAINT FK_style_BomLine_Supplier FOREIGN KEY (SupplierId) REFERENCES partner.Partner (PartnerId),
    CONSTRAINT FK_style_BomLine_Uom FOREIGN KEY (UomCode) REFERENCES ref.Uom (UomCode)
);
GO
/* Added 2026-10-14: consumption allowance from the workbook (ASAD_Consump_Allowance). */
IF COL_LENGTH(N'style.BomLine', N'ConsumptionAllowance') IS NULL
    ALTER TABLE style.BomLine ADD ConsumptionAllowance decimal(18, 6) NULL;
GO
/* Added 2026-10-16: 1 = the LCO consumption was filled from a suggestion (Fill the gaps: brand consumption, or other
   styles' figures), not typed. Shown as an estimate until someone saves the line with another LCO. */
IF COL_LENGTH(N'style.BomLine', N'LcoIsEstimate') IS NULL
    ALTER TABLE style.BomLine ADD LcoIsEstimate bit NOT NULL CONSTRAINT DF_style_BomLine_LcoIsEstimate DEFAULT (0);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.BomLine'), N'IX_style_BomLine_Material', 'IndexId') IS NULL
    CREATE INDEX IX_style_BomLine_Material ON style.BomLine (MaterialId) INCLUDE (StyleId);
IF INDEXPROPERTY(OBJECT_ID(N'style.BomLine'), N'IX_style_BomLine_Supplier', 'IndexId') IS NULL
    CREATE INDEX IX_style_BomLine_Supplier ON style.BomLine (SupplierId) INCLUDE (StyleId) WHERE SupplierId IS NOT NULL;
GO

/* Which colorways use a BOM line, and the material colour in each (written with its BOM line, so no audit columns). */
IF OBJECT_ID(N'style.BomLineColorway', N'U') IS NULL
CREATE TABLE style.BomLineColorway
(
    BomLineId                int            NOT NULL,
    ColorwayId               int            NOT NULL,
    MaterialColorCode        nvarchar(80)   NULL,
    MaterialColorDescription nvarchar(150)  NULL,

    CONSTRAINT PK_style_BomLineColorway PRIMARY KEY CLUSTERED (BomLineId, ColorwayId),
    CONSTRAINT FK_style_BomLineColorway_Line FOREIGN KEY (BomLineId) REFERENCES style.BomLine (BomLineId),
    CONSTRAINT FK_style_BomLineColorway_Colorway FOREIGN KEY (ColorwayId) REFERENCES style.Colorway (ColorwayId)
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.BomLineColorway'), N'IX_style_BomLineColorway_Colorway', 'IndexId') IS NULL
    CREATE INDEX IX_style_BomLineColorway_Colorway ON style.BomLineColorway (ColorwayId);
GO

/*
    AI Studio renders: product images drawn by an AI model from the style's data (and its sketch). Kept apart from
    Style.ImageUrl so a generated picture is never mistaken for a real photo; a user can still choose one as the photo.
    Provider/Model record which service drew it (cloud or in-house). Deleted with the style; a deleted colorway leaves
    ColorwayId null but keeps ColorwayCode.
*/
IF OBJECT_ID(N'style.AiRender', N'U') IS NULL
CREATE TABLE style.AiRender
(
    RenderId      int IDENTITY(1, 1) NOT NULL,
    StyleId       int                NOT NULL,
    ColorwayId    int                NULL,
    ColorwayCode  nvarchar(100)      NULL,
    ImageUrl      nvarchar(500)      NOT NULL,
    Prompt        nvarchar(4000)     NOT NULL,
    Provider      nvarchar(60)       NOT NULL,
    Model         nvarchar(100)      NOT NULL,
    UsedSketch    bit                NOT NULL,
    CreatedBy     nvarchar(64)       NOT NULL,
    CreatedUtc    datetime2(3)       NOT NULL CONSTRAINT DF_style_AiRender_CreatedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_style_AiRender PRIMARY KEY CLUSTERED (RenderId),
    CONSTRAINT FK_style_AiRender_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId) ON DELETE CASCADE,
    CONSTRAINT FK_style_AiRender_Colorway FOREIGN KEY (ColorwayId) REFERENCES style.Colorway (ColorwayId) ON DELETE SET NULL
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.AiRender'), N'IX_style_AiRender_Style', 'IndexId') IS NULL
    CREATE INDEX IX_style_AiRender_Style ON style.AiRender (StyleId, RenderId DESC);
GO
/* Widened 2026-10-14 with style.Colorway.ColorwayCode. */
IF COL_LENGTH(N'style.AiRender', N'ColorwayCode') < 200
    ALTER TABLE style.AiRender ALTER COLUMN ColorwayCode nvarchar(100) NULL;
GO

/*
    Image search (AI Studio > Image search): one row per picture - a style's photo or sketch, a colorway picture, an AI
    render. ItemId = the colorway or render id (0 for the style's own photo and sketch). The Vision job reads the picture
    into Garment / Colours / Details / Description; SearchText is the text built from them and Embedding is the Embedding
    job's vector of that text (1024 numbers: SQL Server 2025 vector type). A row is current while ImageUrl and both model
    names match the picture and the jobs. A picture that could not be read keeps Error and no Embedding, and is not tried
    again until the picture or a model changes. No foreign key on ItemId: colorways and renders already cascade from the
    style, and rows of removed pictures are cleared by style.usp_ImageIndex_ToIndex.
*/
IF OBJECT_ID(N'style.ImageIndex', N'U') IS NULL
CREATE TABLE style.ImageIndex
(
    ImageIndexId   int IDENTITY(1, 1) NOT NULL,
    StyleId        int                NOT NULL,
    Source         varchar(10)        NOT NULL,
    ItemId         int                NOT NULL CONSTRAINT DF_style_ImageIndex_ItemId DEFAULT (0),
    ImageUrl       nvarchar(500)      NOT NULL,
    Garment        nvarchar(100)      NULL,
    Colours        nvarchar(300)      NULL,
    Details        nvarchar(600)      NULL,
    Description    nvarchar(1000)     NULL,
    SearchText     nvarchar(2000)     NULL,
    Embedding      vector(1024)       NULL,
    Error          nvarchar(300)      NULL,
    VisionModel    nvarchar(200)      NOT NULL,
    EmbeddingModel nvarchar(200)      NOT NULL,
    Provider       nvarchar(100)      NOT NULL,
    IndexedBy      nvarchar(64)       NOT NULL,
    IndexedUtc     datetime2(3)       NOT NULL CONSTRAINT DF_style_ImageIndex_IndexedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_style_ImageIndex PRIMARY KEY CLUSTERED (ImageIndexId),
    CONSTRAINT UQ_style_ImageIndex_Slot UNIQUE (StyleId, Source, ItemId),
    CONSTRAINT CK_style_ImageIndex_Source CHECK (Source IN ('Photo', 'Sketch', 'Colorway', 'Render')),
    CONSTRAINT FK_style_ImageIndex_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId) ON DELETE CASCADE
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.ImageIndex'), N'IX_style_ImageIndex_Model', 'IndexId') IS NULL
    CREATE INDEX IX_style_ImageIndex_Model ON style.ImageIndex (EmbeddingModel) INCLUDE (StyleId, Source, ItemId, ImageUrl);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.ImageIndex'), N'IX_style_ImageIndex_ImageUrl', 'IndexId') IS NULL
    CREATE INDEX IX_style_ImageIndex_ImageUrl ON style.ImageIndex (ImageUrl);
GO

/*
    Image search look-alike vectors: one row per picture (same pictures as style.ImageIndex), made by the image model
    built into the API (ImageSearch:ImageModel, e.g. Marqo-FashionSigLIP, 768 numbers) straight from the pixels: no
    description, no AI server. Used for search by photo and "more like this". A row is current while ImageUrl and Model
    match; a picture that could not be decoded keeps Error and no Vector.
*/
IF OBJECT_ID(N'style.ImageVector', N'U') IS NULL
CREATE TABLE style.ImageVector
(
    ImageVectorId  int IDENTITY(1, 1) NOT NULL,
    StyleId        int                NOT NULL,
    Source         varchar(10)        NOT NULL,
    ItemId         int                NOT NULL CONSTRAINT DF_style_ImageVector_ItemId DEFAULT (0),
    ImageUrl       nvarchar(500)      NOT NULL,
    Model          nvarchar(200)      NOT NULL,
    Vector         vector(768)        NULL,
    Error          nvarchar(300)      NULL,
    IndexedBy      nvarchar(64)       NOT NULL,
    IndexedUtc     datetime2(3)       NOT NULL CONSTRAINT DF_style_ImageVector_IndexedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_style_ImageVector PRIMARY KEY CLUSTERED (ImageVectorId),
    CONSTRAINT UQ_style_ImageVector_Slot UNIQUE (StyleId, Source, ItemId),
    CONSTRAINT CK_style_ImageVector_Source CHECK (Source IN ('Photo', 'Sketch', 'Colorway', 'Render')),
    CONSTRAINT FK_style_ImageVector_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId) ON DELETE CASCADE
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.ImageVector'), N'IX_style_ImageVector_ImageUrl', 'IndexId') IS NULL
    CREATE INDEX IX_style_ImageVector_ImageUrl ON style.ImageVector (ImageUrl);
GO

/* Picture tags (TagVersion = the tag vocabulary that tagged this vector; NULL = to tag, e.g. after the picture changed). */
IF COL_LENGTH(N'style.ImageVector', N'TagVersion') IS NULL
    ALTER TABLE style.ImageVector ADD TagVersion varchar(40) NULL, TaggedUtc datetime2(3) NULL;
GO

/*
    Picture tags: what a style picture looks like (hood, closure, pattern, colours), read by the built-in image model
    (Marqo-FashionSigLIP) by comparing the picture's look-alike vector with short descriptions of each tag - no AI server.
    Only confident tags are stored (Score = closeness to the tag, Margin = lead over the next value of the same attribute).
    A tag counts while its Version equals the vector's TagVersion and the vector's picture is still the style's.
    Searched as "attribute:value" (hood:hooded, colour:navy) by Styles, Smart search and the Library assistant.
*/
IF OBJECT_ID(N'style.ImageTag', N'U') IS NULL
CREATE TABLE style.ImageTag
(
    ImageTagId     int IDENTITY(1, 1) NOT NULL,
    ImageVectorId  int                NOT NULL,
    Attribute      varchar(20)        NOT NULL,
    Value          varchar(30)        NOT NULL,
    Score          decimal(9, 4)      NOT NULL,
    Margin         decimal(9, 4)      NOT NULL,
    Version        varchar(40)        NOT NULL,

    CONSTRAINT PK_style_ImageTag PRIMARY KEY CLUSTERED (ImageTagId),
    CONSTRAINT UQ_style_ImageTag UNIQUE (ImageVectorId, Attribute, Value),
    CONSTRAINT FK_style_ImageTag_Vector FOREIGN KEY (ImageVectorId) REFERENCES style.ImageVector (ImageVectorId) ON DELETE CASCADE
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.ImageTag'), N'IX_style_ImageTag_Value', 'IndexId') IS NULL
    CREATE INDEX IX_style_ImageTag_Value ON style.ImageTag (Attribute, Value) INCLUDE (ImageVectorId, Version);
GO

/*
    A style's current picture tags: tags of the vector's current tag version, for a picture the style still has (photo,
    sketch, colorway picture or render with the same address). Used by the look filters of style.usp_Style_List / _Counts.
*/
CREATE OR ALTER FUNCTION style.fn_StyleTags()
RETURNS TABLE
AS
RETURN
    SELECT DISTINCT v.StyleId, t.Attribute, t.Value
    FROM style.ImageTag t
    JOIN style.ImageVector v ON v.ImageVectorId = t.ImageVectorId AND v.TagVersion = t.Version
    WHERE (v.Source = 'Photo' AND EXISTS (SELECT 1 FROM style.Style s WHERE s.StyleId = v.StyleId AND s.ImageUrl = v.ImageUrl))
       OR (v.Source = 'Sketch' AND EXISTS (SELECT 1 FROM style.Style s WHERE s.StyleId = v.StyleId AND s.SketchUrl = v.ImageUrl))
       OR (v.Source = 'Colorway' AND EXISTS (SELECT 1 FROM style.Colorway c WHERE c.ColorwayId = v.ItemId AND c.StyleId = v.StyleId AND c.ImageUrl = v.ImageUrl))
       OR (v.Source = 'Render' AND EXISTS (SELECT 1 FROM style.AiRender r WHERE r.RenderId = v.ItemId AND r.StyleId = v.StyleId AND r.ImageUrl = v.ImageUrl));
GO

/*
    Fill the gaps (Style Library > Fill the gaps, style.gaps.procedures.sql). Rule suggestions are worked out when asked
    for and never stored; these two tables keep what is not a rule:
      GapSuggestion: the AI's answer for a style the rules could not read (Field gender: MALE/FEMALE/UNISEX/KIDS;
                     productType: a garment category such as PANTS, turned into the customer's product type when shown).
      GapDismissed:  a suggestion someone turned down, so the same value is not offered again for that style.
    Nothing here changes a style: accepting a suggestion writes the style and removes its rows.
*/
IF OBJECT_ID(N'style.GapSuggestion', N'U') IS NULL
CREATE TABLE style.GapSuggestion
(
    StyleId     int            NOT NULL,
    Field       varchar(16)    NOT NULL,
    Value       nvarchar(40)   NOT NULL,
    Reason      nvarchar(300)  NULL,
    Provider    nvarchar(100)  NOT NULL,
    Model       nvarchar(200)  NOT NULL,
    CreatedBy   nvarchar(64)   NOT NULL,
    CreatedUtc  datetime2(3)   NOT NULL CONSTRAINT DF_style_GapSuggestion_CreatedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_style_GapSuggestion PRIMARY KEY CLUSTERED (StyleId, Field),
    CONSTRAINT FK_style_GapSuggestion_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId) ON DELETE CASCADE,
    CONSTRAINT CK_style_GapSuggestion_Field CHECK (Field IN ('gender', 'productType'))
);
GO
IF OBJECT_ID(N'style.GapDismissed', N'U') IS NULL
CREATE TABLE style.GapDismissed
(
    StyleId      int            NOT NULL,
    Field        varchar(16)    NOT NULL,
    Value        nvarchar(40)   NOT NULL,
    DismissedBy  nvarchar(64)   NOT NULL,
    DismissedUtc datetime2(3)   NOT NULL CONSTRAINT DF_style_GapDismissed_DismissedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_style_GapDismissed PRIMARY KEY CLUSTERED (StyleId, Field, Value),
    CONSTRAINT FK_style_GapDismissed_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId) ON DELETE CASCADE,
    CONSTRAINT CK_style_GapDismissed_Field CHECK (Field IN ('gender', 'productType'))
);
GO

/* ================================ staging ================================ */

/*
    One row per uploaded workbook. Status: Staged -> Committed | Cancelled.
    Summary holds the checked counts (styles new/changed/unchanged, rows, errors, warnings) as json.
*/
IF OBJECT_ID(N'staging.ImportBatch', N'U') IS NULL
CREATE TABLE staging.ImportBatch
(
    BatchId      int IDENTITY(1, 1) NOT NULL,
    Kind         varchar(32)        NOT NULL,
    FileName     nvarchar(260)      NOT NULL,
    Status       varchar(16)        NOT NULL CONSTRAINT DF_staging_ImportBatch_Status DEFAULT ('Staged'),
    Summary      json               NULL,
    UploadedBy   nvarchar(64)       NOT NULL,
    UploadedUtc  datetime2(3)       NOT NULL CONSTRAINT DF_staging_ImportBatch_UploadedUtc DEFAULT (SYSUTCDATETIME()),
    FinishedBy   nvarchar(64)       NULL,
    FinishedUtc  datetime2(3)       NULL,

    CONSTRAINT PK_staging_ImportBatch PRIMARY KEY CLUSTERED (BatchId),
    CONSTRAINT CK_staging_ImportBatch_Status CHECK (Status IN ('Mapping', 'Staged', 'Committed', 'Cancelled', 'RolledBack')),
    CONSTRAINT CK_staging_ImportBatch_Kind CHECK (Kind IN ('StyleLibrary'))
);
GO

/* Sheet rows exactly as read from the workbook (trimmed text), named after its headings; RowNo = Excel row number.
   The workbook keeps the customer's terms (Article ID, Article Seq); the import maps them to Colorway. */
IF OBJECT_ID(N'staging.StyleRow', N'U') IS NULL
CREATE TABLE staging.StyleRow
(
    BatchId        int            NOT NULL,
    RowNo          int            NOT NULL,
    Cust           nvarchar(32)   NULL,
    SeasonId       nvarchar(16)   NULL,
    StyleId        nvarchar(100)  NULL,
    Description    nvarchar(400)  NULL,
    ModelId        nvarchar(60)   NULL,
    ModelName      nvarchar(100)  NULL,
    WeaveTypeId    nvarchar(8)    NULL,
    WeaveTypeDesc  nvarchar(50)   NULL,
    ProdTypeId     nvarchar(40)   NULL,
    ProdTypeDesc   nvarchar(100)  NULL,
    Gender         nvarchar(16)   NULL,
    LeadTime       nvarchar(16)   NULL,
    BuTeam         nvarchar(16)   NULL,
    BuDesc         nvarchar(100)  NULL,
    CreateDt       datetime2(3)   NULL,
    SketchUrl      nvarchar(500)  NULL,
    ImageUrl       nvarchar(500)  NULL,
    CONSTRAINT PK_staging_StyleRow PRIMARY KEY CLUSTERED (BatchId, RowNo),
    CONSTRAINT FK_staging_StyleRow_Batch FOREIGN KEY (BatchId) REFERENCES staging.ImportBatch (BatchId) ON DELETE CASCADE
);
GO

IF OBJECT_ID(N'staging.ArticleRow', N'U') IS NULL
CREATE TABLE staging.ArticleRow
(
    BatchId     int            NOT NULL,
    RowNo       int            NOT NULL,
    Cust        nvarchar(32)   NULL,
    SeasonId    nvarchar(16)   NULL,
    StyleId     nvarchar(100)  NULL,
    SeqNo       nvarchar(8)    NULL,
    ArticleId   nvarchar(100)  NULL,
    Description nvarchar(150)  NULL,
    Status      nvarchar(16)   NULL,
    ImageUrl    nvarchar(500)  NULL,
    CONSTRAINT PK_staging_ArticleRow PRIMARY KEY CLUSTERED (BatchId, RowNo),
    CONSTRAINT FK_staging_ArticleRow_Batch FOREIGN KEY (BatchId) REFERENCES staging.ImportBatch (BatchId) ON DELETE CASCADE
);
GO

IF OBJECT_ID(N'staging.BomRow', N'U') IS NULL
CREATE TABLE staging.BomRow
(
    BatchId         int             NOT NULL,
    RowNo           int             NOT NULL,
    Cust            nvarchar(32)    NULL,
    SeasonId        nvarchar(16)    NULL,
    StyleId         nvarchar(100)   NULL,
    PartNo          nvarchar(16)    NULL,
    AdMatId         nvarchar(64)    NULL,
    MaterialDesc    nvarchar(4000)  NULL,
    MatTypeId       nvarchar(16)    NULL,
    MatTypeDesc     nvarchar(100)   NULL,
    ContentClass    nvarchar(100)   NULL,   -- wide, so a wrong value reaches the check (New codes) instead of stopping the upload
    AdSupplierId    nvarchar(32)    NULL,
    AdSupplierName  nvarchar(100)   NULL,
    LcoConsump      nvarchar(32)    NULL,
    AdConsump       nvarchar(32)    NULL,
    Uom             nvarchar(8)     NULL,
    SapSupplierId   nvarchar(32)    NULL,
    SapSupplierName nvarchar(150)   NULL,
    ArticleSeq      nvarchar(16)    NULL,
    ArticleId       nvarchar(100)   NULL,
    ArticleDesc     nvarchar(150)   NULL,
    MatColorId      nvarchar(80)    NULL,
    MatColorDesc    nvarchar(150)   NULL,
    ImageUrl        nvarchar(500)   NULL,
    CONSTRAINT PK_staging_BomRow PRIMARY KEY CLUSTERED (BatchId, RowNo),
    CONSTRAINT FK_staging_BomRow_Batch FOREIGN KEY (BatchId) REFERENCES staging.ImportBatch (BatchId) ON DELETE CASCADE
);
GO

/*
    One row per style in a batch, filled by the check: what committing would do to it.
      New = not in the library; Changed = differs from the library (written only if the user ticks it);
      Unchanged = identical; Blocked = has errors (never written).
    Changes lists what differs for a Changed style ('header', 'colorways', 'BOM').
*/
IF OBJECT_ID(N'staging.ImportStyle', N'U') IS NULL
CREATE TABLE staging.ImportStyle
(
    BatchId        int            NOT NULL,
    StyleKey       nvarchar(200)  NOT NULL,   -- Cust|Season|Style
    CustomerCode   nvarchar(32)   NOT NULL,
    SeasonCode     nvarchar(16)   NOT NULL,
    StyleNo        nvarchar(100)  NOT NULL,
    ModelName      nvarchar(100)  NULL,
    Action         varchar(10)    NOT NULL,
    ExistingStyleId int           NULL,
    Changes        nvarchar(100)  NULL,
    ColorwayCount  int            NOT NULL CONSTRAINT DF_staging_ImportStyle_ColorwayCount DEFAULT (0),
    BomLineCount   int            NOT NULL CONSTRAINT DF_staging_ImportStyle_BomLineCount DEFAULT (0),
    ErrorCount     int            NOT NULL CONSTRAINT DF_staging_ImportStyle_ErrorCount DEFAULT (0),
    WarningCount   int            NOT NULL CONSTRAINT DF_staging_ImportStyle_WarningCount DEFAULT (0),
    Committed      bit            NOT NULL CONSTRAINT DF_staging_ImportStyle_Committed DEFAULT (0),
    CONSTRAINT PK_staging_ImportStyle PRIMARY KEY CLUSTERED (BatchId, StyleKey),
    CONSTRAINT FK_staging_ImportStyle_Batch FOREIGN KEY (BatchId) REFERENCES staging.ImportBatch (BatchId) ON DELETE CASCADE,
    CONSTRAINT CK_staging_ImportStyle_Action CHECK (Action IN ('New', 'Changed', 'Unchanged', 'Blocked'))
);
GO

/* Problems found when checking a batch. Errors block the affected style; warnings are shown and imported. */
IF OBJECT_ID(N'staging.ImportIssue', N'U') IS NULL
CREATE TABLE staging.ImportIssue
(
    IssueId   int IDENTITY(1, 1) NOT NULL,
    BatchId   int                NOT NULL,
    Sheet     varchar(16)        NOT NULL,
    RowNo     int                NULL,
    Severity  varchar(8)         NOT NULL,
    StyleKey  nvarchar(200)      NULL,    -- Cust|Season|Style, so the preview can group issues by style
    Message   nvarchar(400)      NOT NULL,
    CONSTRAINT PK_staging_ImportIssue PRIMARY KEY CLUSTERED (IssueId),
    CONSTRAINT FK_staging_ImportIssue_Batch FOREIGN KEY (BatchId) REFERENCES staging.ImportBatch (BatchId) ON DELETE CASCADE,
    CONSTRAINT CK_staging_ImportIssue_Severity CHECK (Severity IN ('Error', 'Warning'))
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'staging.ImportIssue'), N'IX_staging_ImportIssue_Batch', 'IndexId') IS NULL
    CREATE INDEX IX_staging_ImportIssue_Batch ON staging.ImportIssue (BatchId, Sheet, RowNo);
GO
/* Widened 2026-10-14 with StyleNo and ColorwayCode (100); StyleKey (Cust|Season|Style) 100 -> 200, so the ImportStyle
   primary key on it is rebuilt. Content_Class 8 -> 100 so a wrong value is reported by the check, not the upload. */
IF COL_LENGTH(N'staging.StyleRow', N'StyleId') < 200
BEGIN
    ALTER TABLE staging.StyleRow   ALTER COLUMN StyleId      nvarchar(100) NULL;
    ALTER TABLE staging.ArticleRow ALTER COLUMN StyleId      nvarchar(100) NULL;
    ALTER TABLE staging.ArticleRow ALTER COLUMN ArticleId    nvarchar(100) NULL;
    ALTER TABLE staging.BomRow     ALTER COLUMN StyleId      nvarchar(100) NULL;
    ALTER TABLE staging.BomRow     ALTER COLUMN ArticleId    nvarchar(100) NULL;
    ALTER TABLE staging.BomRow     ALTER COLUMN ContentClass nvarchar(100) NULL;
    ALTER TABLE staging.ImportStyle DROP CONSTRAINT PK_staging_ImportStyle;
    ALTER TABLE staging.ImportStyle ALTER COLUMN StyleKey nvarchar(200) NOT NULL;
    ALTER TABLE staging.ImportStyle ALTER COLUMN StyleNo  nvarchar(100) NOT NULL;
    ALTER TABLE staging.ImportStyle ADD CONSTRAINT PK_staging_ImportStyle PRIMARY KEY CLUSTERED (BatchId, StyleKey);
    ALTER TABLE staging.ImportIssue ALTER COLUMN StyleKey nvarchar(200) NULL;
END
GO
/* Added 2026-10-14: customer name (Cust_Name) and consumption allowance (Consump_Allowance) from the workbook. */
IF COL_LENGTH(N'staging.StyleRow', N'CustName') IS NULL
    ALTER TABLE staging.StyleRow ADD CustName nvarchar(150) NULL;
IF COL_LENGTH(N'staging.BomRow', N'ConsumpAllowance') IS NULL
    ALTER TABLE staging.BomRow ADD ConsumpAllowance nvarchar(32) NULL;
GO
/* Added 2026-10-15: the customer account's brand (Customer_Brand); wide, so a wrong value reaches the check. */
IF COL_LENGTH(N'staging.StyleRow', N'CustBrand') IS NULL
    ALTER TABLE staging.StyleRow ADD CustBrand nvarchar(100) NULL;
GO
/* BOM rows without an Article ID apply to all of the style's colorways: their articles are found by style. */
IF INDEXPROPERTY(OBJECT_ID(N'staging.ArticleRow'), N'IX_staging_ArticleRow_Style', 'IndexId') IS NULL
    CREATE INDEX IX_staging_ArticleRow_Style ON staging.ArticleRow (BatchId, StyleId) INCLUDE (Cust, SeasonId, ArticleId);
GO

/*
    Saved column mappings (Settings > Import, Map columns): which workbook sheet and heading feeds each staging column,
    so a workbook with other headings (e.g. the GO style export) loads without editing it.
    Definition: {"sheets":{"Style":{"source":"Style_Parm","columns":{"StyleId":"Style_id",...}},"Article":{...},"Bom":{...}}}
*/
/* Added 2026-10-14: a committed import can be rolled back (Status RolledBack). */
IF COL_LENGTH(N'staging.ImportBatch', N'RolledBackBy') IS NULL
    ALTER TABLE staging.ImportBatch ADD RolledBackBy nvarchar(64) NULL, RolledBackUtc datetime2(3) NULL;
/*
    Added 2026-10-15: the uploaded workbook is kept (Files:Root/style-imports, StoredFile = its file name) with the column
    mapping chosen for it, so an import can be mapped later, mapped again or resumed. Status Mapping = file read, columns
    not mapped yet. The file goes on commit, or 7 days after a cancel or an unfinished mapping.
*/
IF COL_LENGTH(N'staging.ImportBatch', N'StoredFile') IS NULL
    ALTER TABLE staging.ImportBatch ADD StoredFile nvarchar(64) NULL, Mapping nvarchar(max) NULL;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_staging_ImportBatch_Status' AND definition NOT LIKE N'%Mapping%')
BEGIN
    ALTER TABLE staging.ImportBatch DROP CONSTRAINT CK_staging_ImportBatch_Status;
    ALTER TABLE staging.ImportBatch ADD CONSTRAINT CK_staging_ImportBatch_Status CHECK (Status IN ('Mapping', 'Staged', 'Committed', 'Cancelled', 'RolledBack'));
END
GO

/*
    What a commit did, so it can be rolled back (staging.usp_StyleImport_Rollback). Kept for 30 days after the commit.
      ImportUndoStyle    Kind N = style the import added; R = style it replaced, with Snapshot = the style before (json:
                         header, colorways, BOM lines with their colorways)
      ImportUndoMaterial Kind N = material it added; U = material it changed, with the values before
      ImportUndoCode     reference codes it added (season, customer, supplier, ...), removed on rollback when unused
    No foreign keys: the commit fills them with OUTPUT ... INTO.
*/
IF OBJECT_ID(N'staging.ImportUndoStyle', N'U') IS NULL
CREATE TABLE staging.ImportUndoStyle
(
    BatchId   int            NOT NULL,
    StyleId   int            NOT NULL,
    Kind      char(1)        NOT NULL,
    Snapshot  nvarchar(max)  NULL,
    CONSTRAINT PK_staging_ImportUndoStyle PRIMARY KEY CLUSTERED (BatchId, StyleId),
    CONSTRAINT CK_staging_ImportUndoStyle_Kind CHECK (Kind IN ('N', 'R'))
);
GO
IF OBJECT_ID(N'staging.ImportUndoMaterial', N'U') IS NULL
CREATE TABLE staging.ImportUndoMaterial
(
    BatchId           int             NOT NULL,
    MaterialId        int             NOT NULL,
    Kind              char(1)         NOT NULL,
    Description       nvarchar(4000)  NULL,
    MaterialTypeCode  nvarchar(16)    NULL,
    ContentClassCode  nvarchar(8)     NULL,
    CONSTRAINT PK_staging_ImportUndoMaterial PRIMARY KEY CLUSTERED (BatchId, MaterialId)
);
GO
IF OBJECT_ID(N'staging.ImportUndoCode', N'U') IS NULL
CREATE TABLE staging.ImportUndoCode
(
    BatchId  int           NOT NULL,
    Kind     varchar(16)   NOT NULL,
    Code     nvarchar(64)  NOT NULL,
    CONSTRAINT PK_staging_ImportUndoCode PRIMARY KEY CLUSTERED (BatchId, Kind, Code)
);
GO

/*
    Remembered answers from the New codes tab (Settings > Import): a value in a workbook that means an existing code
    (supplier '0001100131' = '1100131', customer '0000001304' = 'SKE'). Every upload applies them before the check,
    so each value is asked about once. ImportAppliedAlias records which ones an import used (shown, can be forgotten).
*/
IF OBJECT_ID(N'staging.ImportCodeAlias', N'U') IS NULL
CREATE TABLE staging.ImportCodeAlias
(
    List        varchar(16)    NOT NULL,
    Value       nvarchar(150)  NOT NULL,
    Code        nvarchar(64)   NOT NULL,
    CreatedBy   nvarchar(64)   NOT NULL,
    CreatedUtc  datetime2(3)   NOT NULL CONSTRAINT DF_staging_ImportCodeAlias_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_staging_ImportCodeAlias PRIMARY KEY CLUSTERED (List, Value)
);
GO
IF OBJECT_ID(N'staging.ImportAppliedAlias', N'U') IS NULL
CREATE TABLE staging.ImportAppliedAlias
(
    BatchId  int            NOT NULL,
    List     varchar(16)    NOT NULL,
    Value    nvarchar(150)  NOT NULL,
    Code     nvarchar(64)   NOT NULL,
    Rows     int            NOT NULL,
    CONSTRAINT PK_staging_ImportAppliedAlias PRIMARY KEY CLUSTERED (BatchId, List, Value),
    CONSTRAINT FK_staging_ImportAppliedAlias_Batch FOREIGN KEY (BatchId) REFERENCES staging.ImportBatch (BatchId) ON DELETE CASCADE
);
GO

IF OBJECT_ID(N'staging.ImportMapping', N'U') IS NULL
CREATE TABLE staging.ImportMapping
(
    MappingId   int IDENTITY(1, 1) NOT NULL,
    Name        nvarchar(100)      NOT NULL,
    Definition  nvarchar(max)      NOT NULL,
    CreatedBy   nvarchar(64)       NOT NULL,
    CreatedUtc  datetime2(3)       NOT NULL CONSTRAINT DF_staging_ImportMapping_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy   nvarchar(64)       NULL,
    UpdatedUtc  datetime2(3)       NULL,
    CONSTRAINT PK_staging_ImportMapping PRIMARY KEY CLUSTERED (MappingId),
    CONSTRAINT UQ_staging_ImportMapping_Name UNIQUE (Name),
    CONSTRAINT CK_staging_ImportMapping_Definition CHECK (ISJSON(Definition) = 1)
);
GO

/* ================================ seed: fixed code lists ================================ */

MERGE ref.ContentClass AS t
USING (VALUES (N'FAB', N'Fabric', 1), (N'TRI', N'Trims', 2), (N'ACC', N'Accessories', 3), (N'LNP', N'Labels & packaging', 4), (N'ART', N'Artwork', 5))
      AS s (ContentClassCode, Name, SortOrder)
ON t.ContentClassCode = s.ContentClassCode
WHEN NOT MATCHED THEN INSERT (ContentClassCode, Name, SortOrder) VALUES (s.ContentClassCode, s.Name, s.SortOrder);
GO

MERGE ref.SeasonTerm AS t
USING (VALUES (N'SP', N'Spring', 1), (N'SS', N'Spring/Summer', 2), (N'SU', N'Summer', 3), (N'FA', N'Fall', 4), (N'FW', N'Fall/Winter', 5), (N'WI', N'Winter', 6))
      AS s (Term, Name, SortOrder)
ON t.Term = s.Term
WHEN NOT MATCHED THEN INSERT (Term, Name, SortOrder) VALUES (s.Term, s.Name, s.SortOrder);
GO

MERGE ref.WeaveType AS t
USING (VALUES (N'KNT', N'Knit'), (N'WVN', N'Woven')) AS s (WeaveTypeCode, Name)
ON t.WeaveTypeCode = s.WeaveTypeCode
WHEN NOT MATCHED THEN INSERT (WeaveTypeCode, Name) VALUES (s.WeaveTypeCode, s.Name);
GO

PRINT 'Style Library tables ready.';
GO
