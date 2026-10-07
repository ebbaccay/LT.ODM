/*
    LT ODM - Material description reader (Materials > Description reader; /api/v1/ai/material-specs)

    mat.MaterialSpec holds what the AI read from a material's free-text description: fibre composition (with recycled
    share), construction, weight, width and a suggested content class. Every reading starts Pending and counts only once
    a person Accepts it; the description itself is never changed. SourceDescription is the text that was read, so a
    reading whose material description changed since is shown as out of date.

    usp_MaterialSpec_List    materials with their reading (or none), filtered; counts per status
    usp_MaterialSpec_ToRead  the next materials to read (unread first), for one AI call
    usp_MaterialSpec_Save    stores readings as Pending (replaces an earlier reading of the same material)
    usp_MaterialSpec_Review  Accept / Reject readings
    usp_MaterialSpec_Get     one material's reading (material page)
    Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\mat.spec.procedures.sql
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'mat.MaterialSpec', N'U') IS NULL
CREATE TABLE mat.MaterialSpec
(
    MaterialId            int             NOT NULL,
    Composition           nvarchar(400)   NULL,      -- readable: "Cotton 70% · Recycled polyester 30%"
    Fibres                nvarchar(2000)  NULL,      -- JSON: [{"fibre":"Polyester","percent":30,"recycled":true}, ...]
    RecycledPct           decimal(5, 2)   NULL,
    Construction          nvarchar(150)   NULL,
    WeightGsm             decimal(7, 1)   NULL,
    WidthCm               decimal(6, 1)   NULL,
    SuggestedContentClass nvarchar(8)     NULL,
    Confidence            decimal(4, 2)   NOT NULL,
    Notes                 nvarchar(300)   NULL,      -- what the check found (e.g. percentages do not add up)
    Status                varchar(10)     NOT NULL CONSTRAINT DF_mat_MaterialSpec_Status DEFAULT ('Pending'),
    SourceDescription     nvarchar(4000)  NOT NULL,
    Provider              nvarchar(100)   NOT NULL,
    Model                 nvarchar(200)   NOT NULL,
    ReadBy                nvarchar(64)    NOT NULL,
    ReadUtc               datetime2(3)    NOT NULL CONSTRAINT DF_mat_MaterialSpec_ReadUtc DEFAULT (SYSUTCDATETIME()),
    ReviewedBy            nvarchar(64)    NULL,
    ReviewedUtc           datetime2(3)    NULL,

    CONSTRAINT PK_mat_MaterialSpec PRIMARY KEY CLUSTERED (MaterialId),
    CONSTRAINT FK_mat_MaterialSpec_Material FOREIGN KEY (MaterialId) REFERENCES mat.Material (MaterialId) ON DELETE CASCADE,
    CONSTRAINT CK_mat_MaterialSpec_Status CHECK (Status IN ('Pending', 'Accepted', 'Rejected')),
    CONSTRAINT CK_mat_MaterialSpec_Confidence CHECK (Confidence BETWEEN 0 AND 1)
);
GO
IF INDEXPROPERTY(OBJECT_ID(N'mat.MaterialSpec'), N'IX_mat_MaterialSpec_Status', 'IndexId') IS NULL
    CREATE INDEX IX_mat_MaterialSpec_Status ON mat.MaterialSpec (Status) INCLUDE (Confidence);
GO

/*
    Materials in scope (section and type as on the master, falling back to the BOM; search in code or description) with
    their reading. @Status: Unread, Pending, Accepted, Rejected, Outdated (description changed since it was read), or NULL.
    Result sets: 1. counts per status for the scope; 2. the page (lowest confidence first among Pending).
*/
CREATE OR ALTER PROCEDURE mat.usp_MaterialSpec_List
    @ContentClassCode nvarchar(8)   = NULL,
    @MaterialTypeCode nvarchar(16)  = NULL,
    @Status           varchar(10)   = NULL,
    @Search           nvarchar(100) = NULL,
    @Skip             int           = 0,
    @Take             int           = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @like nvarchar(110) = UPPER(N'%' + REPLACE(REPLACE(REPLACE(@Search, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%');

    CREATE TABLE #m
    (
        MaterialId int NOT NULL PRIMARY KEY, MaterialCode nvarchar(64) NOT NULL, Description nvarchar(4000) NULL, ContentClassCode nvarchar(8) NULL,
        MaterialTypeCode nvarchar(16) NULL, Styles int NOT NULL, State varchar(10) NOT NULL
    );
    INSERT #m
    SELECT m.MaterialId, m.MaterialCode, m.Description, cls.ContentClassCode, cls.MaterialTypeCode,
           (SELECT COUNT(DISTINCT b.StyleId) FROM style.BomLine b WHERE b.MaterialId = m.MaterialId),
           CASE WHEN s.MaterialId IS NULL THEN 'Unread'
                WHEN s.SourceDescription <> ISNULL(m.Description, N'') THEN 'Outdated'
                ELSE s.Status END
    FROM mat.Material m
    OUTER APPLY (SELECT ContentClassCode = COALESCE(m.ContentClassCode, (SELECT TOP 1 b.ContentClassCode FROM style.BomLine b WHERE b.MaterialId = m.MaterialId)),
                        MaterialTypeCode = COALESCE(m.MaterialTypeCode, (SELECT TOP 1 b.MaterialTypeCode FROM style.BomLine b WHERE b.MaterialId = m.MaterialId))) cls
    LEFT JOIN mat.MaterialSpec s ON s.MaterialId = m.MaterialId
    WHERE NULLIF(TRIM(m.Description), N'') IS NOT NULL
      AND (@ContentClassCode IS NULL OR cls.ContentClassCode = @ContentClassCode)
      AND (@MaterialTypeCode IS NULL OR cls.MaterialTypeCode = @MaterialTypeCode)
      AND (@Search IS NULL OR UPPER(m.MaterialCode) COLLATE Latin1_General_100_BIN2 LIKE @like
                           OR UPPER(m.Description) COLLATE Latin1_General_100_BIN2 LIKE @like);

    SELECT Total = COUNT(*), Unread = SUM(IIF(State = 'Unread', 1, 0)), Pending = SUM(IIF(State = 'Pending', 1, 0)),
           Accepted = SUM(IIF(State = 'Accepted', 1, 0)), Rejected = SUM(IIF(State = 'Rejected', 1, 0)), Outdated = SUM(IIF(State = 'Outdated', 1, 0))
    FROM #m;

    SELECT x.MaterialId, x.MaterialCode, x.Description, x.ContentClassCode, x.MaterialTypeCode, x.Styles, State = x.State,
           s.Composition, s.Fibres, s.RecycledPct, s.Construction, s.WeightGsm, s.WidthCm, s.SuggestedContentClass, s.Confidence, s.Notes,
           s.Provider, s.Model, s.ReadBy, s.ReadUtc, s.ReviewedBy, s.ReviewedUtc,
           TotalCount = COUNT(*) OVER ()
    FROM #m x
    LEFT JOIN mat.MaterialSpec s ON s.MaterialId = x.MaterialId
    WHERE @Status IS NULL OR x.State = @Status
    ORDER BY IIF(x.State = 'Pending', 0, 1), s.Confidence, x.Styles DESC, x.MaterialCode
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

/* The next materials to read in the scope: never read first, then readings whose description changed; most used first. */
CREATE OR ALTER PROCEDURE mat.usp_MaterialSpec_ToRead
    @ContentClassCode nvarchar(8)  = NULL,
    @MaterialTypeCode nvarchar(16) = NULL,
    @Take             int          = 25
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH scope AS (
        SELECT m.MaterialId, m.MaterialCode, m.Description,
               ContentClassCode = COALESCE(m.ContentClassCode, (SELECT TOP 1 b.ContentClassCode FROM style.BomLine b WHERE b.MaterialId = m.MaterialId)),
               MaterialTypeCode = COALESCE(m.MaterialTypeCode, (SELECT TOP 1 b.MaterialTypeCode FROM style.BomLine b WHERE b.MaterialId = m.MaterialId)),
               Styles = (SELECT COUNT(DISTINCT b.StyleId) FROM style.BomLine b WHERE b.MaterialId = m.MaterialId),
               Rank = CASE WHEN s.MaterialId IS NULL THEN 0 WHEN s.SourceDescription <> m.Description THEN 1 ELSE 9 END
        FROM mat.Material m
        LEFT JOIN mat.MaterialSpec s ON s.MaterialId = m.MaterialId
        WHERE NULLIF(TRIM(m.Description), N'') IS NOT NULL)
    SELECT TOP (@Take) MaterialId, MaterialCode, Description, ContentClassCode, MaterialTypeCode,
           Remaining = COUNT(*) OVER ()
    FROM scope
    WHERE Rank < 9
      AND (@ContentClassCode IS NULL OR ContentClassCode = @ContentClassCode)
      AND (@MaterialTypeCode IS NULL OR MaterialTypeCode = @MaterialTypeCode)
    ORDER BY Rank, Styles DESC, MaterialCode;
END
GO

/* Stores readings (JSON array) as Pending, replacing earlier readings of those materials. */
CREATE OR ALTER PROCEDURE mat.usp_MaterialSpec_Save
    @Specs    nvarchar(max),
    @Provider nvarchar(100),
    @Model    nvarchar(200),
    @ReadBy   nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    CREATE TABLE #s
    (
        MaterialId int NOT NULL PRIMARY KEY, Composition nvarchar(400) NULL, Fibres nvarchar(2000) NULL, RecycledPct decimal(5, 2) NULL,
        Construction nvarchar(150) NULL, WeightGsm decimal(7, 1) NULL, WidthCm decimal(6, 1) NULL, SuggestedContentClass nvarchar(8) NULL,
        Confidence decimal(4, 2) NOT NULL, Notes nvarchar(300) NULL, SourceDescription nvarchar(4000) NOT NULL
    );
    INSERT #s
    SELECT j.MaterialId, j.Composition, j.Fibres, j.RecycledPct, j.Construction, j.WeightGsm, j.WidthCm, j.SuggestedContentClass, j.Confidence, j.Notes,
           m.Description
    FROM OPENJSON(@Specs) WITH (
        MaterialId int, Composition nvarchar(400), Fibres nvarchar(max) AS JSON, RecycledPct decimal(5, 2), Construction nvarchar(150),
        WeightGsm decimal(7, 1), WidthCm decimal(6, 1), SuggestedContentClass nvarchar(8), Confidence decimal(4, 2), Notes nvarchar(300)) j
    JOIN mat.Material m ON m.MaterialId = j.MaterialId
    WHERE m.Description IS NOT NULL;

    BEGIN TRANSACTION;
    DELETE s FROM mat.MaterialSpec s JOIN #s x ON x.MaterialId = s.MaterialId;
    INSERT mat.MaterialSpec (MaterialId, Composition, Fibres, RecycledPct, Construction, WeightGsm, WidthCm, SuggestedContentClass, Confidence, Notes,
                             SourceDescription, Provider, Model, ReadBy)
    SELECT MaterialId, Composition, Fibres, RecycledPct, Construction, WeightGsm, WidthCm,
           IIF(EXISTS (SELECT 1 FROM ref.ContentClass c WHERE c.ContentClassCode = x.SuggestedContentClass), x.SuggestedContentClass, NULL),
           Confidence, Notes, SourceDescription, @Provider, @Model, @ReadBy
    FROM #s x;
    COMMIT TRANSACTION;

    SELECT Saved = COUNT(*) FROM #s;
END
GO

/* Accept or Reject readings (JSON array of material ids). Only readings of the current description can be accepted. */
CREATE OR ALTER PROCEDURE mat.usp_MaterialSpec_Review
    @MaterialIds nvarchar(max),
    @Status      varchar(10),
    @ReviewedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Status NOT IN ('Accepted', 'Rejected', 'Pending') THROW 50400, N'Choose Accept or Reject.', 1;
    UPDATE s SET Status = @Status, ReviewedBy = IIF(@Status = 'Pending', NULL, @ReviewedBy), ReviewedUtc = IIF(@Status = 'Pending', NULL, SYSUTCDATETIME())
    FROM mat.MaterialSpec s
    JOIN OPENJSON(@MaterialIds) WITH (MaterialId int '$') j ON j.MaterialId = s.MaterialId
    JOIN mat.Material m ON m.MaterialId = s.MaterialId
    WHERE @Status <> 'Accepted' OR s.SourceDescription = m.Description;
    SELECT Updated = @@ROWCOUNT;
END
GO

CREATE OR ALTER PROCEDURE mat.usp_MaterialSpec_Get
    @MaterialId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.MaterialId, m.MaterialCode, m.Description, ContentClassCode = m.ContentClassCode, MaterialTypeCode = m.MaterialTypeCode, Styles = 0,
           State = IIF(s.SourceDescription <> ISNULL(m.Description, N''), 'Outdated', s.Status),
           s.Composition, s.Fibres, s.RecycledPct, s.Construction, s.WeightGsm, s.WidthCm, s.SuggestedContentClass, s.Confidence, s.Notes,
           s.Provider, s.Model, s.ReadBy, s.ReadUtc, s.ReviewedBy, s.ReviewedUtc, TotalCount = 1
    FROM mat.MaterialSpec s JOIN mat.Material m ON m.MaterialId = s.MaterialId
    WHERE s.MaterialId = @MaterialId;
END
GO

PRINT 'Material description reader ready.';
