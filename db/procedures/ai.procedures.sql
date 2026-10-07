/*
    LT ODM - AI connections (Settings > AI connections, /api/v1/admin/ai, Admin).

    usp_Settings_Get       connections (with the encrypted key, for the API only) and jobs
    usp_Connection_Save    create / update (RowVer); KeyAction keep | set | clear
    usp_Connection_Delete  refused while a job uses the connection
    usp_Purpose_Save       a job's connection and model (NULL connection = app default)

    Rule errors are THROWn as 50400 / 50404 / 50409 with a message for the user. Requires tables\ai.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\ai.procedures.sql
*/
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE ai.usp_Settings_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ConnectionId, Name, Kind, Endpoint, ApiKeyProtected, ApiKeyHint, InHouse, TimeoutSeconds, Notes, IsActive, UpdatedBy, UpdatedUtc, RowVer
    FROM ai.Connections
    ORDER BY Name;

    SELECT Purpose, ConnectionId, Model, UpdatedBy, UpdatedUtc
    FROM ai.Purposes;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Connection_Save
    @ConnectionId    int            = NULL,
    @RowVer          binary(8)      = NULL,
    @Name            nvarchar(100),
    @Kind            varchar(20),
    @Endpoint        nvarchar(400),
    @KeyAction       varchar(5),     -- keep | set | clear
    @ApiKeyProtected nvarchar(max)  = NULL,
    @ApiKeyHint      nvarchar(8)    = NULL,
    @InHouse         bit,
    @TimeoutSeconds  int,
    @Notes           nvarchar(400)  = NULL,
    @IsActive        bit,
    @ChangedBy       nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF EXISTS (SELECT 1 FROM ai.Connections WHERE Name = @Name AND (@ConnectionId IS NULL OR ConnectionId <> @ConnectionId))
        THROW 50409, N'Another connection already has this name.', 1;

    IF @ConnectionId IS NULL
    BEGIN
        INSERT ai.Connections (Name, Kind, Endpoint, ApiKeyProtected, ApiKeyHint, InHouse, TimeoutSeconds, Notes, IsActive, CreatedBy, UpdatedBy)
        VALUES (@Name, @Kind, @Endpoint, IIF(@KeyAction = 'set', @ApiKeyProtected, NULL), IIF(@KeyAction = 'set', @ApiKeyHint, NULL),
                @InHouse, @TimeoutSeconds, @Notes, @IsActive, @ChangedBy, @ChangedBy);
        SELECT CAST(SCOPE_IDENTITY() AS int);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM ai.Connections WHERE ConnectionId = @ConnectionId)
        THROW 50404, N'The connection was not found. It may have been deleted.', 1;
    IF @IsActive = 0 AND EXISTS (SELECT 1 FROM ai.Purposes WHERE ConnectionId = @ConnectionId)
        THROW 50409, N'A job uses this connection. Point the job elsewhere before turning the connection off.', 1;

    UPDATE ai.Connections
    SET Name = @Name, Kind = @Kind, Endpoint = @Endpoint,
        ApiKeyProtected = CASE @KeyAction WHEN 'set' THEN @ApiKeyProtected WHEN 'clear' THEN NULL ELSE ApiKeyProtected END,
        ApiKeyHint = CASE @KeyAction WHEN 'set' THEN @ApiKeyHint WHEN 'clear' THEN NULL ELSE ApiKeyHint END,
        InHouse = @InHouse, TimeoutSeconds = @TimeoutSeconds, Notes = @Notes, IsActive = @IsActive,
        UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    WHERE ConnectionId = @ConnectionId AND RowVer = @RowVer;
    IF @@ROWCOUNT = 0 THROW 50409, N'Someone else changed this connection after you opened it. Reload and try again.', 1;

    SELECT @ConnectionId;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Connection_Delete
    @ConnectionId int,
    @RowVer       binary(8)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM ai.Purposes WHERE ConnectionId = @ConnectionId)
        THROW 50409, N'A job uses this connection. Point the job elsewhere before deleting it.', 1;
    DELETE ai.Connections WHERE ConnectionId = @ConnectionId AND RowVer = @RowVer;
    IF @@ROWCOUNT = 0
    BEGIN
        IF EXISTS (SELECT 1 FROM ai.Connections WHERE ConnectionId = @ConnectionId)
            THROW 50409, N'Someone else changed this connection after you opened it. Reload and try again.', 1;
        THROW 50404, N'The connection was not found. It may have been deleted.', 1;
    END
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Purpose_Save
    @Purpose      varchar(20),
    @ConnectionId int           = NULL,
    @Model        nvarchar(200) = NULL,
    @ChangedBy    nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM ai.Purposes WHERE Purpose = @Purpose)
        THROW 50404, N'Unknown job.', 1;
    IF @ConnectionId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ai.Connections WHERE ConnectionId = @ConnectionId AND IsActive = 1)
        THROW 50400, N'Choose an active connection.', 1;
    IF @ConnectionId IS NOT NULL AND NULLIF(TRIM(@Model), N'') IS NULL
        THROW 50400, N'Enter the model name the service uses.', 1;

    UPDATE ai.Purposes
    SET ConnectionId = @ConnectionId, Model = IIF(@ConnectionId IS NULL, NULL, TRIM(@Model)), UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    WHERE Purpose = @Purpose;
END
GO
