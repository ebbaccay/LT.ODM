/*
    LT ODM Style Library - sidebar menu (replaces the TMS tms_nav_* tables).
    Target: SQL Server 2025. Run after auth.tables.sql (item roles reference auth.Roles).

    Visibility: an item with no rows in nav.ItemRoles is shown to every signed-in user;
    otherwise the user needs at least one of the listed roles. Idempotent.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'nav') IS NULL
    EXEC (N'CREATE SCHEMA nav AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'nav.Groups', N'U') IS NULL
CREATE TABLE nav.Groups
(
    GroupId     int IDENTITY(1, 1)  NOT NULL,
    -- Plain text or a translation key (e.g. nav.manageOfferings).
    Text        nvarchar(100)       NOT NULL,
    -- Lucide icon name, e.g. lucideStore (see src/app/layout/menu-icons.ts).
    Icon        varchar(64)         NOT NULL CONSTRAINT DF_nav_Groups_Icon DEFAULT ('lucideFolder'),
    -- main = scrolling list; bottom = pinned to the bottom of the sidebar (TMS "fixedItems").
    Slot        varchar(16)         NOT NULL CONSTRAINT DF_nav_Groups_Slot DEFAULT ('main'),
    SortOrder   int                 NOT NULL CONSTRAINT DF_nav_Groups_SortOrder DEFAULT (0),
    IsVisible   bit                 NOT NULL CONSTRAINT DF_nav_Groups_IsVisible DEFAULT (1),
    UpdatedUtc  datetime2(3)        NOT NULL CONSTRAINT DF_nav_Groups_UpdatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy   nvarchar(64)        NULL,

    CONSTRAINT PK_nav_Groups PRIMARY KEY CLUSTERED (GroupId),
    CONSTRAINT CK_nav_Groups_Slot CHECK (Slot IN ('main', 'bottom')),
    CONSTRAINT CK_nav_Groups_Icon CHECK (REGEXP_LIKE(Icon, '^lucide[A-Za-z0-9]+$'))
);
GO

IF OBJECT_ID(N'nav.Items', N'U') IS NULL
CREATE TABLE nav.Items
(
    ItemId      int IDENTITY(1, 1)  NOT NULL,
    GroupId     int                 NOT NULL,
    Text        nvarchar(100)       NOT NULL,
    -- App route without the leading slash ('' = home, 'garment-quotation', 'settings/roles').
    Route       varchar(200)        NOT NULL,
    Icon        varchar(64)         NOT NULL CONSTRAINT DF_nav_Items_Icon DEFAULT ('lucideCircleDot'),
    SortOrder   int                 NOT NULL CONSTRAINT DF_nav_Items_SortOrder DEFAULT (0),
    IsVisible   bit                 NOT NULL CONSTRAINT DF_nav_Items_IsVisible DEFAULT (1),
    UpdatedUtc  datetime2(3)        NOT NULL CONSTRAINT DF_nav_Items_UpdatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy   nvarchar(64)        NULL,

    CONSTRAINT PK_nav_Items PRIMARY KEY CLUSTERED (ItemId),
    CONSTRAINT FK_nav_Items_Groups FOREIGN KEY (GroupId) REFERENCES nav.Groups (GroupId) ON DELETE CASCADE,
    CONSTRAINT UQ_nav_Items_Route UNIQUE (Route),
    CONSTRAINT CK_nav_Items_Route CHECK (REGEXP_LIKE(Route, '^[a-z0-9][a-z0-9/_-]*$') OR Route = ''),
    CONSTRAINT CK_nav_Items_Icon CHECK (REGEXP_LIKE(Icon, '^lucide[A-Za-z0-9]+$'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_nav_Items_Group' AND object_id = OBJECT_ID(N'nav.Items'))
    CREATE INDEX IX_nav_Items_Group ON nav.Items (GroupId, SortOrder);
GO

IF OBJECT_ID(N'nav.ItemRoles', N'U') IS NULL
CREATE TABLE nav.ItemRoles
(
    ItemId  int NOT NULL,
    RoleId  int NOT NULL,

    CONSTRAINT PK_nav_ItemRoles PRIMARY KEY CLUSTERED (ItemId, RoleId),
    CONSTRAINT FK_nav_ItemRoles_Items FOREIGN KEY (ItemId) REFERENCES nav.Items (ItemId) ON DELETE CASCADE,
    CONSTRAINT FK_nav_ItemRoles_Roles FOREIGN KEY (RoleId) REFERENCES auth.Roles (RoleId) ON DELETE CASCADE
);
GO
