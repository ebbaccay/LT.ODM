/*
    LT ODM Costing - prices per IO from the ERP (roadmap 3.16). A style has no single price: each IO has its own.

      style.Style --< cost.Io --< cost.IoCosting --< cost.IoCostingLine >-- cost.Category

    cost.Category       : the ERP's cost categories, each with the sign it has in net balance and the group it is shown
                          in (Fabric, Trims, Making, Other).
    cost.Import         : each upload of the ERP costing export.
    cost.Io             : one IO: its style, plant (ref.Plant), order quantity and dates. Destination is not kept: an IO can cover
                          several customer POs, each with its own destination.
    cost.IoCosting      : a costing version of the IO, the ERP's own number: PCOSTING (pre-costing) or FCOSTING (final
                          costing). FOB, net balance and percentage to FOB are kept as the ERP sent them.
    cost.IoCostingLine  : the amount per category of that version.

    Money and SMV are decimal(.., 5): the ERP calculates at 5 decimals (FOB = total document amount / total order qty).
    IOs are removed with their style (ON DELETE CASCADE).

    Requires tables\style.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i tables\cost.tables.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'cost') IS NULL EXEC (N'CREATE SCHEMA cost AUTHORIZATION dbo;');
GO
/* The application's role may run the Costing procedures (docs/installation/02-database.md, section 3). Given here too,
   so a server set up before this schema existed gets it with the deploy; skipped where the role is not created yet. */
IF DATABASE_PRINCIPAL_ID(N'ltodm_app') IS NOT NULL
    GRANT EXECUTE ON SCHEMA::cost TO ltodm_app;
GO

/* The Costing page was a placeholder, hidden in the menu on some servers and open to everyone; prices are not. Once, when
   these tables first appear: show it, and set who sees it if nobody has (Settings > Menu can change both afterwards). */
IF OBJECT_ID(N'cost.Io', N'U') IS NULL AND OBJECT_ID(N'nav.ItemRoles', N'U') IS NOT NULL
BEGIN
    UPDATE nav.Items SET IsVisible = 1, UpdatedUtc = SYSUTCDATETIME(), UpdatedBy = N'seed' WHERE Route = 'costing' AND IsVisible = 0;

    INSERT nav.ItemRoles (ItemId, RoleId)
    SELECT i.ItemId, r.RoleId
    FROM nav.Items i
    JOIN auth.Roles r ON r.Name IN (N'Admin', N'Merchandiser', N'Designer', N'Costing')
    WHERE i.Route = 'costing' AND NOT EXISTS (SELECT 1 FROM nav.ItemRoles x WHERE x.ItemId = i.ItemId);
END;
GO

IF OBJECT_ID(N'cost.Category', N'U') IS NULL
    CREATE TABLE cost.Category
    (
        CategoryCode varchar(12)  NOT NULL CONSTRAINT PK_cost_Category PRIMARY KEY,
        Name         nvarchar(60) NOT NULL,
        GroupCode    varchar(10)  NOT NULL CONSTRAINT CK_cost_Category_Group CHECK (GroupCode IN ('Fabric', 'Trims', 'Making', 'Other')),
        Sign         smallint     NOT NULL CONSTRAINT CK_cost_Category_Sign CHECK (Sign IN (-1, 1)),
        SortOrder    int          NOT NULL
    );
GO

INSERT cost.Category (CategoryCode, Name, GroupCode, Sign, SortOrder)
SELECT v.CategoryCode, v.Name, v.GroupCode, -1, v.SortOrder
FROM (VALUES
    ('MAINFAB',  N'Main fabric',      'Fabric', 10),
    ('SUBFAB',   N'Sub fabric',       'Fabric', 20),
    ('TRIMS',    N'Trims',            'Trims',  30),
    ('LCCHARGE', N'L/C charge',       'Other',  40),
    ('MATFRT',   N'Material freight', 'Other',  50),
    ('LTRIMS',   N'Local trims',      'Trims',  60),
    ('CM',       N'Basic CM',         'Making', 70),
    ('SUBCON',   N'Subcon',           'Making', 80),
    ('GARMWSH',  N'Garment wash',     'Other',  90),
    ('EMBRO',    N'Embroidery',       'Other',  100),
    ('VACOST',   N'VA cost',          'Other',  110),
    ('PRINT',    N'Print',            'Other',  120),
    ('OTHCOST',  N'Other cost',       'Other',  130),
    ('CLAIM',    N'Claim reserve',    'Other',  140)
) v (CategoryCode, Name, GroupCode, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM cost.Category c WHERE c.CategoryCode = v.CategoryCode);
GO

IF OBJECT_ID(N'cost.Import', N'U') IS NULL
    CREATE TABLE cost.Import
    (
        ImportId    int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_cost_Import PRIMARY KEY,
        FileName    nvarchar(260)      NOT NULL,
        RowsRead    int                NOT NULL CONSTRAINT DF_cost_Import_Rows DEFAULT (0),
        Added       int                NOT NULL CONSTRAINT DF_cost_Import_Added DEFAULT (0),
        Updated     int                NOT NULL CONSTRAINT DF_cost_Import_Updated DEFAULT (0),
        Unmatched   int                NOT NULL CONSTRAINT DF_cost_Import_Unmatched DEFAULT (0),
        ImportedBy  nvarchar(256)      NOT NULL,
        ImportedUtc datetime2(3)       NOT NULL CONSTRAINT DF_cost_Import_Utc DEFAULT (SYSUTCDATETIME())
    );
GO

IF OBJECT_ID(N'cost.Io', N'U') IS NULL
    CREATE TABLE cost.Io
    (
        IoId             int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_cost_Io PRIMARY KEY,
        IoNo             nvarchar(30)       NOT NULL CONSTRAINT UQ_cost_Io_IoNo UNIQUE,
        StyleId          int                NOT NULL CONSTRAINT FK_cost_Io_Style REFERENCES style.Style (StyleId) ON DELETE CASCADE,
        PlantCode        nvarchar(20)       NULL CONSTRAINT FK_cost_Io_Plant REFERENCES ref.Plant (PlantCode),
        OrderQty         int                NULL,
        LcoDate          date               NULL,
        PlanDeliveryDate date               NULL,
        MerchSmv         decimal(12, 5)     NULL,
        TcSmv            decimal(12, 5)     NULL,
        ImportId         int                NOT NULL CONSTRAINT FK_cost_Io_Import REFERENCES cost.Import (ImportId),
        UpdatedUtc       datetime2(3)       NOT NULL CONSTRAINT DF_cost_Io_Updated DEFAULT (SYSUTCDATETIME())
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cost_Io_Style' AND object_id = OBJECT_ID(N'cost.Io'))
    CREATE INDEX IX_cost_Io_Style ON cost.Io (StyleId);
GO

IF OBJECT_ID(N'cost.IoCosting', N'U') IS NULL
    CREATE TABLE cost.IoCosting
    (
        CostingVersion int            NOT NULL CONSTRAINT PK_cost_IoCosting PRIMARY KEY,   -- the ERP's number
        IoId           int            NOT NULL CONSTRAINT FK_cost_IoCosting_Io REFERENCES cost.Io (IoId) ON DELETE CASCADE,
        CostType       varchar(10)    NOT NULL CONSTRAINT CK_cost_IoCosting_Type CHECK (CostType IN ('PCOSTING', 'FCOSTING')),
        Status         nvarchar(30)   NULL,
        CurrencyCode   char(3)        NOT NULL,
        CostDate       datetime2(3)   NULL,
        Fob            decimal(18, 5) NOT NULL,
        NetBalance     decimal(18, 5) NULL,
        PercToFob      decimal(18, 5) NULL,
        ImportId       int            NOT NULL CONSTRAINT FK_cost_IoCosting_Import REFERENCES cost.Import (ImportId),
        UpdatedUtc     datetime2(3)   NOT NULL CONSTRAINT DF_cost_IoCosting_Updated DEFAULT (SYSUTCDATETIME())
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cost_IoCosting_Io' AND object_id = OBJECT_ID(N'cost.IoCosting'))
    CREATE INDEX IX_cost_IoCosting_Io ON cost.IoCosting (IoId);
GO

IF OBJECT_ID(N'cost.IoCostingLine', N'U') IS NULL
    CREATE TABLE cost.IoCostingLine
    (
        CostingVersion int            NOT NULL CONSTRAINT FK_cost_IoCostingLine_Costing REFERENCES cost.IoCosting (CostingVersion) ON DELETE CASCADE,
        CategoryCode   varchar(12)    NOT NULL CONSTRAINT FK_cost_IoCostingLine_Category REFERENCES cost.Category (CategoryCode),
        Amount         decimal(18, 5) NOT NULL,
        CONSTRAINT PK_cost_IoCostingLine PRIMARY KEY (CostingVersion, CategoryCode)
    );
GO

/*
    cost.PlantCostPerMinute : the plant's cost per minute (basic CM = cost per minute x TC SMV), as in the ERP's Cost Per Minute
                              table: from an effective date, for orders from a minimum quantity, per sales term and currency. A
                              rate is never removed, only marked deleted, so what was used stays visible. Kept by Admin and
                              Costing on the Costing page.
*/
IF OBJECT_ID(N'cost.PlantCostPerMinute', N'U') IS NULL
    CREATE TABLE cost.PlantCostPerMinute
    (
        RateId        int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_cost_PlantCostPerMinute PRIMARY KEY,
        PlantCode     nvarchar(20)       NOT NULL CONSTRAINT FK_cost_PlantCostPerMinute_Plant REFERENCES ref.Plant (PlantCode),
        EffectiveDate date               NOT NULL,
        MinOrderQty   int                NOT NULL CONSTRAINT DF_cost_PlantCostPerMinute_MinQty DEFAULT (1),
        SalesTerm     varchar(10)        NOT NULL CONSTRAINT DF_cost_PlantCostPerMinute_Term DEFAULT ('FOB'),
        CurrencyCode  char(3)            NOT NULL,
        Cost          decimal(18, 5)     NOT NULL,
        IsDeleted     bit                NOT NULL CONSTRAINT DF_cost_PlantCostPerMinute_Deleted DEFAULT (0),
        CreatedBy     nvarchar(64)       NOT NULL,
        CreatedUtc    datetime2(3)       NOT NULL CONSTRAINT DF_cost_PlantCostPerMinute_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedBy     nvarchar(64)       NULL,
        UpdatedUtc    datetime2(3)       NULL,
        CONSTRAINT CK_cost_PlantCostPerMinute_Cost CHECK (Cost > 0),
        CONSTRAINT CK_cost_PlantCostPerMinute_MinQty CHECK (MinOrderQty >= 0)
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_cost_PlantCostPerMinute_Key' AND object_id = OBJECT_ID(N'cost.PlantCostPerMinute'))
    CREATE UNIQUE INDEX UX_cost_PlantCostPerMinute_Key ON cost.PlantCostPerMinute (PlantCode, EffectiveDate, MinOrderQty, SalesTerm, CurrencyCode) WHERE IsDeleted = 0;
GO

/* Starting rate for CM35 (XTIC YuenThai Cambodia): USD 0.14, the rate every IO of the first costing export was costed at
   (basic CM = 0.14 x TC SMV on all 136 rows). Effective date and cost can be changed on the Costing page. Once only. */
IF NOT EXISTS (SELECT 1 FROM ref.Plant WHERE PlantCode = N'CM35')
    INSERT ref.Plant (PlantCode, Name) VALUES (N'CM35', N'XTIC YuenThai Cambodia');
IF NOT EXISTS (SELECT 1 FROM cost.PlantCostPerMinute WHERE PlantCode = N'CM35')
    INSERT cost.PlantCostPerMinute (PlantCode, EffectiveDate, MinOrderQty, SalesTerm, CurrencyCode, Cost, CreatedBy)
    VALUES (N'CM35', '2026-01-01', 1, 'FOB', 'USD', 0.14, N'seed');
GO
