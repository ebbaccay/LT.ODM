/*
    LT ODM Style Library - styles, colorways (articles), BOMs and their reference data
    Target: SQL Server 2025, database compatibility level 170

      ref.*      code lists (seasons, business units, product types, weave types, material types, content classes, UOMs)
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

/* '2027-SS': year + term. */
IF OBJECT_ID(N'ref.Season', N'U') IS NULL
CREATE TABLE ref.Season
(
    SeasonCode   nvarchar(16)  NOT NULL,
    SeasonYear   smallint      NOT NULL,
    Term         nvarchar(8)   NOT NULL,
    Description  nvarchar(64)  NULL,
    IsActive     bit           NOT NULL CONSTRAINT DF_ref_Season_IsActive DEFAULT (1),

    CONSTRAINT PK_ref_Season PRIMARY KEY CLUSTERED (SeasonCode),
    CONSTRAINT FK_ref_Season_Term FOREIGN KEY (Term) REFERENCES ref.SeasonTerm (Term),
    CONSTRAINT CK_ref_Season_Code CHECK (REGEXP_LIKE(SeasonCode, N'^[0-9]{4}-[A-Z]{2,4}$'))
);
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
    StyleNo             nvarchar(40)       NOT NULL,
    -- Style number without the reuse suffix ('S2508MR1212_SS27' -> 'S2508MR1212'): groups a style family.
    BaseStyleNo         AS (CAST(IIF(CHARINDEX(N'_', StyleNo) > 1, LEFT(StyleNo, CHARINDEX(N'_', StyleNo) - 1), StyleNo) AS nvarchar(40))) PERSISTED,
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
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_Season', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_Season ON style.Style (SeasonCode, CustomerId) INCLUDE (StyleNo, ModelName);
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_StyleNo', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_StyleNo ON style.Style (StyleNo);
/* Styles list (style.usp_Style_List): every filter column, so finding a page never reads the wide rows. */
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_List', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_List ON style.Style (SeasonCode, StyleNo)
        INCLUDE (CustomerId, BusinessUnitCode, ProductTypeCode, WeaveTypeCode, Gender, ModelCode, ModelName);
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_Family', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_Family ON style.Style (CustomerId, BaseStyleNo, ModelCode) INCLUDE (SeasonCode, StyleNo);
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
    Suffix         nvarchar(40)   NULL,       -- what was added to the base style number, e.g. 'SS27', 'B_GRADE_FW26'
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
    ColorwayCode        nvarchar(20)       NOT NULL,
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
    ColorwayCode  nvarchar(20)       NULL,
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
    CONSTRAINT CK_staging_ImportBatch_Status CHECK (Status IN ('Staged', 'Committed', 'Cancelled')),
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
    StyleId        nvarchar(40)   NULL,
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
    StyleId     nvarchar(40)   NULL,
    SeqNo       nvarchar(8)    NULL,
    ArticleId   nvarchar(20)   NULL,
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
    StyleId         nvarchar(40)    NULL,
    PartNo          nvarchar(16)    NULL,
    AdMatId         nvarchar(64)    NULL,
    MaterialDesc    nvarchar(4000)  NULL,
    MatTypeId       nvarchar(16)    NULL,
    MatTypeDesc     nvarchar(100)   NULL,
    ContentClass    nvarchar(8)     NULL,
    AdSupplierId    nvarchar(32)    NULL,
    AdSupplierName  nvarchar(100)   NULL,
    LcoConsump      nvarchar(32)    NULL,
    AdConsump       nvarchar(32)    NULL,
    Uom             nvarchar(8)     NULL,
    SapSupplierId   nvarchar(32)    NULL,
    SapSupplierName nvarchar(150)   NULL,
    ArticleSeq      nvarchar(16)    NULL,
    ArticleId       nvarchar(20)    NULL,
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
    StyleKey       nvarchar(100)  NOT NULL,   -- Cust|Season|Style
    CustomerCode   nvarchar(32)   NOT NULL,
    SeasonCode     nvarchar(16)   NOT NULL,
    StyleNo        nvarchar(40)   NOT NULL,
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
    StyleKey  nvarchar(100)      NULL,    -- Cust|Season|Style, so the preview can group issues by style
    Message   nvarchar(400)      NOT NULL,
    CONSTRAINT PK_staging_ImportIssue PRIMARY KEY CLUSTERED (IssueId),
    CONSTRAINT FK_staging_ImportIssue_Batch FOREIGN KEY (BatchId) REFERENCES staging.ImportBatch (BatchId) ON DELETE CASCADE,
    CONSTRAINT CK_staging_ImportIssue_Severity CHECK (Severity IN ('Error', 'Warning'))
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'staging.ImportIssue'), N'IX_staging_ImportIssue_Batch', 'IndexId') IS NULL
    CREATE INDEX IX_staging_ImportIssue_Batch ON staging.ImportIssue (BatchId, Sheet, RowNo);
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
