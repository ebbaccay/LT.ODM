/*
    LT ODM Concepts - LT's own design ideas, worked up before they are offered (roadmap 5.7, 5.1, 5.2).

      concept.Concept --< ConceptTag     >-- ref.TrendTag
                      --< ConceptImage                       (inspiration pictures, with a look-alike vector each)
                      --< ConceptStyle   >-- style.Style     (the library styles that inspired it: design provenance)
                      --< pack.PackageItem.ConceptId         (styles offered in a package because of it)
                      --< style.Style.ConceptId              (LT designs developed from it, roadmap 5.21)

    concept.Concept      : one idea, on library codes: customer (optional: an idea need not have a client yet), season,
                           target market, product category (ref.TargetMarket / ref.ProductCategory), gender, target
                           price as typed, a description, and the AI brief (BriefSummary; products, fabrics and notes
                           in BriefJson). Status Draft, Ready (worked up, can be offered) or Dropped.
    concept.ConceptTag   : the trend tags it follows.
    concept.ConceptImage : inspiration pictures (stored in the concept image store, files/concepts). Vector is the
                           picture's look-alike vector from the image model built into the API (the same model as image
                           search, so it compares with every library picture); Model says which. The brand closeness
                           check (5.2) compares it with other brands' pictures.
    concept.ConceptStyle : design provenance (5.1): a library style the concept takes from, with a note and where it
                           was found. Another brand's style may inspire a concept for know-how (materials,
                           construction), never as the look: the closeness check is there for that.
                           Removed with the style (ON DELETE CASCADE), like the image search rows.

    Requires tables\style.tables.sql (run before tables\pack.tables.sql, which links package items to concepts).
    Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i tables\concept.tables.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'concept') IS NULL EXEC (N'CREATE SCHEMA concept AUTHORIZATION dbo;');
GO
/* The application's role may run the Concepts procedures (docs/installation/02-database.md, section 3). Given here too,
   so a server set up before this schema existed gets it with the deploy; skipped where the role is not created yet. */
IF DATABASE_PRINCIPAL_ID(N'ltodm_app') IS NOT NULL
    GRANT EXECUTE ON SCHEMA::concept TO ltodm_app;
GO

IF OBJECT_ID(N'concept.Concept', N'U') IS NULL
CREATE TABLE concept.Concept
(
    ConceptId           int IDENTITY(1, 1) NOT NULL,
    Name                nvarchar(150)      NOT NULL,
    CustomerId          int                NULL,
    SeasonCode          nvarchar(16)       NULL,
    TargetMarketCode    nvarchar(16)       NULL,
    ProductCategoryCode nvarchar(20)       NULL,
    Gender              nvarchar(16)       NULL,
    TargetPrice         nvarchar(50)       NULL,   -- as typed, e.g. "USD 12-14 FOB"
    Description         nvarchar(2000)     NULL,
    Status              varchar(10)        NOT NULL CONSTRAINT DF_concept_Concept_Status DEFAULT ('Draft'),
    OwnerName           nvarchar(64)       NOT NULL,
    BriefSummary        nvarchar(4000)     NULL,
    BriefJson           nvarchar(max)      NULL,   -- {"products":[...],"fabrics":[...],"notes":[...]}
    BriefBy             nvarchar(64)       NULL,
    BriefUtc            datetime2(3)       NULL,
    CreatedBy           nvarchar(64)       NOT NULL,
    CreatedUtc          datetime2(3)       NOT NULL CONSTRAINT DF_concept_Concept_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy           nvarchar(64)       NULL,
    UpdatedUtc          datetime2(3)       NULL,
    RowVer              rowversion         NOT NULL,

    CONSTRAINT PK_concept_Concept PRIMARY KEY CLUSTERED (ConceptId),
    CONSTRAINT FK_concept_Concept_Customer FOREIGN KEY (CustomerId) REFERENCES partner.Partner (PartnerId),
    CONSTRAINT FK_concept_Concept_Season FOREIGN KEY (SeasonCode) REFERENCES ref.Season (SeasonCode),
    CONSTRAINT FK_concept_Concept_Market FOREIGN KEY (TargetMarketCode) REFERENCES ref.TargetMarket (TargetMarketCode),
    CONSTRAINT FK_concept_Concept_Category FOREIGN KEY (ProductCategoryCode) REFERENCES ref.ProductCategory (ProductCategoryCode),
    CONSTRAINT CK_concept_Concept_Name CHECK (LEN(Name) > 0),
    CONSTRAINT CK_concept_Concept_Status CHECK (Status IN ('Draft', 'Ready', 'Dropped')),
    CONSTRAINT CK_concept_Concept_Gender CHECK (Gender IN ('MALE', 'FEMALE', 'UNISEX', 'KIDS')),
    CONSTRAINT CK_concept_Concept_BriefJson CHECK (BriefJson IS NULL OR ISJSON(BriefJson) = 1)
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'concept.Concept'), N'IX_concept_Concept_Customer', 'IndexId') IS NULL
    CREATE INDEX IX_concept_Concept_Customer ON concept.Concept (CustomerId, SeasonCode) INCLUDE (Status, Name);
GO

IF OBJECT_ID(N'concept.ConceptTag', N'U') IS NULL
CREATE TABLE concept.ConceptTag
(
    ConceptId    int          NOT NULL,
    TrendTagCode nvarchar(40) NOT NULL,

    CONSTRAINT PK_concept_ConceptTag PRIMARY KEY CLUSTERED (ConceptId, TrendTagCode),
    CONSTRAINT FK_concept_ConceptTag_Concept FOREIGN KEY (ConceptId) REFERENCES concept.Concept (ConceptId) ON DELETE CASCADE,
    CONSTRAINT FK_concept_ConceptTag_Tag FOREIGN KEY (TrendTagCode) REFERENCES ref.TrendTag (TrendTagCode)
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'concept.ConceptTag'), N'IX_concept_ConceptTag_Tag', 'IndexId') IS NULL
    CREATE INDEX IX_concept_ConceptTag_Tag ON concept.ConceptTag (TrendTagCode);
GO

IF OBJECT_ID(N'concept.ConceptImage', N'U') IS NULL
CREATE TABLE concept.ConceptImage
(
    ConceptImageId int IDENTITY(1, 1) NOT NULL,
    ConceptId      int                NOT NULL,
    ImageUrl       nvarchar(500)      NOT NULL,
    Caption        nvarchar(200)      NULL,
    SortOrder      smallint           NOT NULL,
    Model          nvarchar(200)      NULL,   -- image model of Vector (NULL = not read yet)
    Vector         vector(768)        NULL,
    VectorError    nvarchar(300)      NULL,
    AddedBy        nvarchar(64)       NOT NULL,
    AddedUtc       datetime2(3)       NOT NULL CONSTRAINT DF_concept_ConceptImage_AddedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_concept_ConceptImage PRIMARY KEY CLUSTERED (ConceptImageId),
    CONSTRAINT FK_concept_ConceptImage_Concept FOREIGN KEY (ConceptId) REFERENCES concept.Concept (ConceptId) ON DELETE CASCADE
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'concept.ConceptImage'), N'IX_concept_ConceptImage_Concept', 'IndexId') IS NULL
    CREATE INDEX IX_concept_ConceptImage_Concept ON concept.ConceptImage (ConceptId, SortOrder);
GO

IF OBJECT_ID(N'concept.ConceptStyle', N'U') IS NULL
CREATE TABLE concept.ConceptStyle
(
    ConceptId int            NOT NULL,
    StyleId   int            NOT NULL,
    Note      nvarchar(500)  NULL,   -- what is taken from it (fabric, construction, fit, ...)
    AddedFrom varchar(12)    NULL,   -- Pick (typed or picked), Match (AI matches), LookAlike (closeness check / pictures)
    AddedBy   nvarchar(64)   NOT NULL,
    AddedUtc  datetime2(3)   NOT NULL CONSTRAINT DF_concept_ConceptStyle_AddedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_concept_ConceptStyle PRIMARY KEY CLUSTERED (ConceptId, StyleId),
    CONSTRAINT FK_concept_ConceptStyle_Concept FOREIGN KEY (ConceptId) REFERENCES concept.Concept (ConceptId) ON DELETE CASCADE,
    CONSTRAINT FK_concept_ConceptStyle_Style FOREIGN KEY (StyleId) REFERENCES style.Style (StyleId) ON DELETE CASCADE,
    CONSTRAINT CK_concept_ConceptStyle_AddedFrom CHECK (AddedFrom IN ('Pick', 'Match', 'LookAlike'))
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'concept.ConceptStyle'), N'IX_concept_ConceptStyle_Style', 'IndexId') IS NULL
    CREATE INDEX IX_concept_ConceptStyle_Style ON concept.ConceptStyle (StyleId);
GO

/*
    Added 2026-10-29 (roadmap 5.21, "Develop as style"): an LT original design worked up from a concept becomes a style of the
    concept's customer straight away (the offered product becomes the brand's design once adopted). style.Style.ConceptId marks
    such a style and the concept it came from; NULL for every other style. A concept with developed styles cannot be deleted.
*/
IF COL_LENGTH(N'style.Style', N'ConceptId') IS NULL
    ALTER TABLE style.Style ADD ConceptId int NULL CONSTRAINT FK_style_Style_Concept REFERENCES concept.Concept (ConceptId);
GO
IF INDEXPROPERTY(OBJECT_ID(N'style.Style'), N'IX_style_Style_Concept', 'IndexId') IS NULL
    CREATE INDEX IX_style_Style_Concept ON style.Style (ConceptId) WHERE ConceptId IS NOT NULL;
GO

/*
    Added 2026-10-29 (roadmap 5.22, the concept story for the client): ClientStory is the text the client reads on the story page of
    a line sheet and share link (the idea in Description stays LT's own); ConceptImage.ForClient marks the pictures shown there
    (the API allows it only for pictures the brand closeness check found clear of other brands' designs).
*/
IF COL_LENGTH(N'concept.Concept', N'ClientStory') IS NULL
    ALTER TABLE concept.Concept ADD ClientStory nvarchar(2000) NULL;
GO
IF COL_LENGTH(N'concept.ConceptImage', N'ForClient') IS NULL
    ALTER TABLE concept.ConceptImage ADD ForClient bit NOT NULL CONSTRAINT DF_concept_ConceptImage_ForClient DEFAULT (0);
GO
