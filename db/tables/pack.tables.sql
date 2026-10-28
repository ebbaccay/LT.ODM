/*
    LT ODM Style Packages - library styles offered to a client (roadmap 5.8).

      pack.Package --< PackageItem >-- style.Style
                          \--< PackageItemColorway   (the colourways offered, by colourway code)

    pack.Package             : one offer to one customer (optionally for a season), with a status from Draft to Ordered
                               or Closed. SentUtc is set the first time it is marked Sent.
    pack.PackageItem         : one library style in a package. PackageItemId never changes while the item is in the
                               package: costs and quotes attach to it later in their own table (roadmap 5.19), so no
                               price columns here. A style is in a package at most once. Only styles of the package's
                               brand are added directly; another brand's style is offered as a copy (roadmap 5.12).
                               AllColorways = 1 offers every colourway the style has; 0 offers the ones listed in
                               PackageItemColorway.
    pack.PackageItemColorway : chosen colourways by ColorwayCode (unique within a style), not ColorwayId: an import
                               overwrite or rollback re-creates colourways with new ids, and a code that is no longer on
                               the style is shown as missing instead of blocking the import.

    A style in a package cannot be deleted (style.usp_Style_Delete) and an import that added it cannot be rolled back
    until it is removed from the package.
    Requires style.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i tables\pack.tables.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'pack') IS NULL EXEC (N'CREATE SCHEMA pack AUTHORIZATION dbo;');
GO
/* The application's role may run the Style Packages procedures (docs/installation/02-database.md, section 3). Given here
   too, so a server set up before this schema existed gets it with the deploy; skipped where the role is not created yet. */
IF DATABASE_PRINCIPAL_ID(N'ltodm_app') IS NOT NULL
    GRANT EXECUTE ON SCHEMA::pack TO ltodm_app;
GO

IF OBJECT_ID(N'pack.Package', N'U') IS NULL
CREATE TABLE pack.Package
(
    PackageId    int IDENTITY(1, 1) NOT NULL,
    Name         nvarchar(150)      NOT NULL,
    CustomerId   int                NOT NULL,
    SeasonCode   nvarchar(16)       NULL,
    Description  nvarchar(2000)     NULL,
    Status       varchar(10)        NOT NULL CONSTRAINT DF_pack_Package_Status DEFAULT ('Draft'),
    OwnerName    nvarchar(64)       NOT NULL,   -- LT ODM user who looks after the package (the creator unless changed)
    SentUtc      datetime2(3)       NULL,
    CreatedBy    nvarchar(64)       NOT NULL,
    CreatedUtc   datetime2(3)       NOT NULL CONSTRAINT DF_pack_Package_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy    nvarchar(64)       NULL,
    UpdatedUtc   datetime2(3)       NULL,
    RowVer       rowversion         NOT NULL,

    CONSTRAINT PK_pack_Package PRIMARY KEY CLUSTERED (PackageId),
    CONSTRAINT FK_pack_Package_Customer FOREIGN KEY (CustomerId) REFERENCES partner.Partner (PartnerId),
    CONSTRAINT FK_pack_Package_Season FOREIGN KEY (SeasonCode) REFERENCES ref.Season (SeasonCode),
    CONSTRAINT CK_pack_Package_Name CHECK (LEN(Name) > 0),
    CONSTRAINT CK_pack_Package_Status CHECK (Status IN ('Draft', 'Sent', 'Viewed', 'Sampling', 'Ordered', 'Closed'))
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'pack.Package'), N'IX_pack_Package_Customer', 'IndexId') IS NULL
    CREATE INDEX IX_pack_Package_Customer ON pack.Package (CustomerId, SeasonCode) INCLUDE (Status, Name);
GO

IF OBJECT_ID(N'pack.PackageItem', N'U') IS NULL
CREATE TABLE pack.PackageItem
(
    PackageItemId int IDENTITY(1, 1) NOT NULL,
    PackageId     int                NOT NULL,
    StyleId       int                NOT NULL,
    SortOrder     smallint           NOT NULL,
    PitchNote     nvarchar(1000)     NULL,   -- shown to the client (cards, line sheet)
    InternalNote  nvarchar(1000)     NULL,   -- LT only, never on the line sheet
    AllColorways  bit                NOT NULL CONSTRAINT DF_pack_PackageItem_AllColorways DEFAULT (1),
    -- Where the style was found, for the hit rate later (roadmap 5.17); NULL = not recorded.
    AddedFrom     varchar(12)        NULL,
    CreatedBy     nvarchar(64)       NOT NULL,
    CreatedUtc    datetime2(3)       NOT NULL CONSTRAINT DF_pack_PackageItem_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy     nvarchar(64)       NULL,
    UpdatedUtc    datetime2(3)       NULL,
    RowVer        rowversion         NOT NULL,

    CONSTRAINT PK_pack_PackageItem PRIMARY KEY CLUSTERED (PackageItemId),
    CONSTRAINT UQ_pack_PackageItem_Style UNIQUE (PackageId, StyleId),
    CONSTRAINT FK_pack_PackageItem_Package FOREIGN KEY (PackageId) REFERENCES pack.Package (PackageId),
    CONSTRAINT FK_pack_PackageItem_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId),
    CONSTRAINT CK_pack_PackageItem_AddedFrom CHECK (AddedFrom IN ('Styles', 'StylePage', 'Similar', 'ImageSearch', 'Assistant', 'Concept', 'Copy', 'Offer'))
);
GO
/* Added 2026-10-28: 'Offer' = picked on the What to offer page (pack.usp_Offer_Suggest, roadmap 5.14). */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_pack_PackageItem_AddedFrom' AND definition NOT LIKE N'%Offer%')
BEGIN
    ALTER TABLE pack.PackageItem DROP CONSTRAINT CK_pack_PackageItem_AddedFrom;
    ALTER TABLE pack.PackageItem ADD CONSTRAINT CK_pack_PackageItem_AddedFrom
        CHECK (AddedFrom IN ('Styles', 'StylePage', 'Similar', 'ImageSearch', 'Assistant', 'Concept', 'Copy', 'Offer'));
END
GO
IF INDEXPROPERTY(OBJECT_ID(N'pack.PackageItem'), N'IX_pack_PackageItem_Style', 'IndexId') IS NULL
    CREATE INDEX IX_pack_PackageItem_Style ON pack.PackageItem (StyleId) INCLUDE (PackageId);
GO

IF OBJECT_ID(N'pack.PackageItemColorway', N'U') IS NULL
CREATE TABLE pack.PackageItemColorway
(
    PackageItemId int           NOT NULL,
    ColorwayCode  nvarchar(100) NOT NULL,

    CONSTRAINT PK_pack_PackageItemColorway PRIMARY KEY CLUSTERED (PackageItemId, ColorwayCode),
    CONSTRAINT FK_pack_PackageItemColorway_Item FOREIGN KEY (PackageItemId) REFERENCES pack.PackageItem (PackageItemId)
);
GO

/*
    Share links (roadmap 5.16): a package opened by the client without signing in.
    pack.PackageShare  : one link (e.g. per buyer). Only the SHA-256 of the link's secret is kept, so a link cannot be read
                         back: it is shown once when made. A link works until ExpiresUtc, or until it is revoked.
    pack.ShareFeedback : the clients' answers per style through one link: Like / Pass and a comment, one per person (the
                         name they typed; VoterKey), so a buying team can share a link.
    pack.ShareAccess   : every opening of the package page (time, IP, browser), for "who looked and when".
*/
IF OBJECT_ID(N'pack.PackageShare', N'U') IS NULL
CREATE TABLE pack.PackageShare
(
    ShareId        int IDENTITY(1, 1) NOT NULL,
    PackageId      int                NOT NULL,
    TokenHash      binary(32)         NOT NULL,
    Label          nvarchar(150)      NOT NULL,   -- who it is for, e.g. "Skechers men's buying team"
    ExpiresUtc     datetime2(3)       NOT NULL,
    RevokedUtc     datetime2(3)       NULL,
    RevokedBy      nvarchar(64)       NULL,
    Views          int                NOT NULL CONSTRAINT DF_pack_PackageShare_Views DEFAULT (0),
    FirstViewedUtc datetime2(3)       NULL,
    LastViewedUtc  datetime2(3)       NULL,
    CreatedBy      nvarchar(64)       NOT NULL,
    CreatedUtc     datetime2(3)       NOT NULL CONSTRAINT DF_pack_PackageShare_CreatedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_pack_PackageShare PRIMARY KEY CLUSTERED (ShareId),
    CONSTRAINT UQ_pack_PackageShare_Token UNIQUE (TokenHash),
    CONSTRAINT FK_pack_PackageShare_Package FOREIGN KEY (PackageId) REFERENCES pack.Package (PackageId),
    CONSTRAINT CK_pack_PackageShare_Label CHECK (LEN(Label) > 0)
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'pack.PackageShare'), N'IX_pack_PackageShare_Package', 'IndexId') IS NULL
    CREATE INDEX IX_pack_PackageShare_Package ON pack.PackageShare (PackageId);
GO
/* Added 2026-10-28: the link's secret, encrypted by the API (ASP.NET Data Protection, key ring on the web server), so an
   editor can copy an active link again to re-send it. The database alone cannot read it; lookups still use TokenHash.
   NULL on links made before this column: they can only be made again. */
IF COL_LENGTH(N'pack.PackageShare', N'TokenProtected') IS NULL
    ALTER TABLE pack.PackageShare ADD TokenProtected nvarchar(500) NULL;
GO

IF OBJECT_ID(N'pack.ShareFeedback', N'U') IS NULL
CREATE TABLE pack.ShareFeedback
(
    ShareId       int            NOT NULL,
    PackageItemId int            NOT NULL,
    Verdict       varchar(4)     NULL,       -- Like | Pass | NULL (only a comment)
    Comment       nvarchar(1000) NULL,
    Name          nvarchar(100)  NULL,       -- typed by the client; not checked
    UpdatedUtc    datetime2(3)   NOT NULL CONSTRAINT DF_pack_ShareFeedback_UpdatedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_pack_ShareFeedback PRIMARY KEY CLUSTERED (ShareId, PackageItemId),
    CONSTRAINT FK_pack_ShareFeedback_Share FOREIGN KEY (ShareId) REFERENCES pack.PackageShare (ShareId),
    CONSTRAINT FK_pack_ShareFeedback_Item FOREIGN KEY (PackageItemId) REFERENCES pack.PackageItem (PackageItemId),
    CONSTRAINT CK_pack_ShareFeedback_Verdict CHECK (Verdict IN ('Like', 'Pass'))
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'pack.ShareFeedback'), N'IX_pack_ShareFeedback_Item', 'IndexId') IS NULL
    CREATE INDEX IX_pack_ShareFeedback_Item ON pack.ShareFeedback (PackageItemId);
GO
/*
    Added 2026-10-28: several people may answer through the same link, each under the name they type. VoterKey is that name
    trimmed and in lower case ('' = no name given: one shared answer for the link), so one answer per person per style.
*/
IF COL_LENGTH(N'pack.ShareFeedback', N'VoterKey') IS NULL
BEGIN
    ALTER TABLE pack.ShareFeedback ADD VoterKey nvarchar(100) NOT NULL CONSTRAINT DF_pack_ShareFeedback_VoterKey DEFAULT (N'');
    EXEC (N'UPDATE pack.ShareFeedback SET VoterKey = LOWER(LTRIM(RTRIM(ISNULL(Name, N''''))));');
END
GO
IF EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
           WHERE i.object_id = OBJECT_ID(N'pack.ShareFeedback') AND i.is_primary_key = 1
           HAVING COUNT(*) = 2)
BEGIN
    ALTER TABLE pack.ShareFeedback DROP CONSTRAINT PK_pack_ShareFeedback;
    ALTER TABLE pack.ShareFeedback ADD CONSTRAINT PK_pack_ShareFeedback PRIMARY KEY CLUSTERED (ShareId, PackageItemId, VoterKey);
END
GO

IF OBJECT_ID(N'pack.ShareAccess', N'U') IS NULL
CREATE TABLE pack.ShareAccess
(
    ShareAccessId bigint IDENTITY(1, 1) NOT NULL,
    ShareId       int                   NOT NULL,
    AtUtc         datetime2(3)          NOT NULL CONSTRAINT DF_pack_ShareAccess_AtUtc DEFAULT (SYSUTCDATETIME()),
    Ip            varchar(45)           NULL,
    UserAgent     nvarchar(300)         NULL,

    CONSTRAINT PK_pack_ShareAccess PRIMARY KEY CLUSTERED (ShareAccessId),
    CONSTRAINT FK_pack_ShareAccess_Share FOREIGN KEY (ShareId) REFERENCES pack.PackageShare (ShareId)
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'pack.ShareAccess'), N'IX_pack_ShareAccess_Share', 'IndexId') IS NULL
    CREATE INDEX IX_pack_ShareAccess_Share ON pack.ShareAccess (ShareId, AtUtc);
GO

/*
    Added 2026-10-28 (roadmap 5.17, hit rate): what became of each style offered, set by the merchandiser on the package
    page. NULL = no outcome yet; Sampling = the client asked for samples; Ordered = the client ordered it; Dropped = not taken.
*/
IF COL_LENGTH(N'pack.PackageItem', N'Outcome') IS NULL
    ALTER TABLE pack.PackageItem ADD
        Outcome    varchar(10)  NULL CONSTRAINT CK_pack_PackageItem_Outcome CHECK (Outcome IN ('Sampling', 'Ordered', 'Dropped')),
        OutcomeBy  nvarchar(64) NULL,
        OutcomeUtc datetime2(3) NULL;
GO

/*
    Added 2026-10-28: notices to LT users when a client answers through a share link (the bell in the top bar). One notice
    per user and link gathers the answers given within 10 minutes of each other while unread, so a client clicking
    through 20 styles makes one notice. Recipients: the package's owner and whoever made the link. Read notices go after
    90 days.
*/
IF OBJECT_ID(N'pack.PackageNotice', N'U') IS NULL
CREATE TABLE pack.PackageNotice
(
    NoticeId   int IDENTITY(1, 1) NOT NULL,
    UserName   nvarchar(64)       NOT NULL,
    PackageId  int                NOT NULL,
    ShareId    int                NOT NULL,
    Likes      int                NOT NULL CONSTRAINT DF_pack_PackageNotice_Likes DEFAULT (0),
    Passes     int                NOT NULL CONSTRAINT DF_pack_PackageNotice_Passes DEFAULT (0),
    Comments   int                NOT NULL CONSTRAINT DF_pack_PackageNotice_Comments DEFAULT (0),
    ClientName nvarchar(100)      NULL,      -- the name the client typed, last one given
    FirstUtc   datetime2(3)       NOT NULL CONSTRAINT DF_pack_PackageNotice_FirstUtc DEFAULT (SYSUTCDATETIME()),
    LastUtc    datetime2(3)       NOT NULL CONSTRAINT DF_pack_PackageNotice_LastUtc DEFAULT (SYSUTCDATETIME()),
    ReadUtc    datetime2(3)       NULL,

    CONSTRAINT PK_pack_PackageNotice PRIMARY KEY CLUSTERED (NoticeId),
    CONSTRAINT FK_pack_PackageNotice_Package FOREIGN KEY (PackageId) REFERENCES pack.Package (PackageId),
    CONSTRAINT FK_pack_PackageNotice_Share FOREIGN KEY (ShareId) REFERENCES pack.PackageShare (ShareId)
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'pack.PackageNotice'), N'IX_pack_PackageNotice_User', 'IndexId') IS NULL
    CREATE INDEX IX_pack_PackageNotice_User ON pack.PackageNotice (UserName, LastUtc DESC) INCLUDE (ReadUtc, ShareId);
GO
