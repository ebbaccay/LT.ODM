/*
    LT ODM - AI connections (Settings > AI connections, Admin).

    ai.Connections : where to call an AI service (kind, endpoint, API key, in-house or cloud). The API key is encrypted by
                    the API (ASP.NET Data Protection, key ring in App_Data/keys) before it is stored; ApiKeyHint keeps the
                    last 4 characters so admins can tell which key is saved. The key is never sent to the browser.
    ai.Purposes    : one row per job (text, image, embedding, vision, document, prediction) with its connection and model.
                    No connection = the app's default from appsettings (text, image) or not set up (AI Lab jobs);
                    Disabled = the job is turned off (no default either).
    ai.Policy      : one row, the central switches. AiEnabled = 0 turns every AI call off. AllowCloud = 0 blocks every
                    service outside the LT network (Gemini, or a connection not marked in-house), whatever the jobs or
                    appsettings say. A new database starts with AllowCloud = 0; a database that already used a cloud
                    connection when this table was added keeps it on.
    Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i tables\ai.tables.sql
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'ai') IS NULL EXEC (N'CREATE SCHEMA ai AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'ai.Connections', N'U') IS NULL
CREATE TABLE ai.Connections
(
    ConnectionId     int IDENTITY(1, 1) NOT NULL,
    Name             nvarchar(100)      NOT NULL,
    Kind             varchar(20)        NOT NULL,
    Endpoint         nvarchar(400)      NOT NULL,
    ApiKeyProtected  nvarchar(max)      NULL,
    ApiKeyHint       nvarchar(8)        NULL,
    InHouse          bit                NOT NULL,
    TimeoutSeconds   int                NOT NULL CONSTRAINT DF_ai_Connections_Timeout DEFAULT (120),
    Notes            nvarchar(400)      NULL,
    IsActive         bit                NOT NULL CONSTRAINT DF_ai_Connections_IsActive DEFAULT (1),
    CreatedBy        nvarchar(64)       NOT NULL,
    CreatedUtc       datetime2(3)       NOT NULL CONSTRAINT DF_ai_Connections_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy        nvarchar(64)       NOT NULL,
    UpdatedUtc       datetime2(3)       NOT NULL CONSTRAINT DF_ai_Connections_UpdatedUtc DEFAULT (SYSUTCDATETIME()),
    RowVer           rowversion         NOT NULL,

    CONSTRAINT PK_ai_Connections PRIMARY KEY CLUSTERED (ConnectionId),
    CONSTRAINT UQ_ai_Connections_Name UNIQUE (Name),
    CONSTRAINT CK_ai_Connections_Kind CHECK (Kind IN ('Gemini', 'OpenAiCompatible', 'Custom')),
    CONSTRAINT CK_ai_Connections_Endpoint CHECK (Endpoint LIKE N'http://%' OR Endpoint LIKE N'https://%'),
    CONSTRAINT CK_ai_Connections_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 600),
    -- Gemini is Google's cloud service: never in-house.
    CONSTRAINT CK_ai_Connections_GeminiCloud CHECK (Kind <> 'Gemini' OR InHouse = 0)
);
GO

IF OBJECT_ID(N'ai.Purposes', N'U') IS NULL
CREATE TABLE ai.Purposes
(
    Purpose       varchar(20)    NOT NULL,
    ConnectionId  int            NULL,
    Model         nvarchar(200)  NULL,
    UpdatedBy     nvarchar(64)   NULL,
    UpdatedUtc    datetime2(3)   NULL,

    CONSTRAINT PK_ai_Purposes PRIMARY KEY CLUSTERED (Purpose),
    CONSTRAINT FK_ai_Purposes_Connection FOREIGN KEY (ConnectionId) REFERENCES ai.Connections (ConnectionId),
    CONSTRAINT CK_ai_Purposes_Purpose CHECK (Purpose IN ('text', 'image', 'embedding', 'vision', 'document', 'prediction')),
    CONSTRAINT CK_ai_Purposes_Model CHECK (ConnectionId IS NULL OR Model IS NOT NULL)
);
GO

-- Databases created before jobs could be turned off.
IF COL_LENGTH(N'ai.Purposes', N'Disabled') IS NULL
    ALTER TABLE ai.Purposes ADD Disabled bit NOT NULL CONSTRAINT DF_ai_Purposes_Disabled DEFAULT (0);
GO

INSERT ai.Purposes (Purpose)
SELECT v.Purpose FROM (VALUES ('text'), ('image'), ('embedding'), ('vision'), ('document'), ('prediction')) v (Purpose)
WHERE NOT EXISTS (SELECT 1 FROM ai.Purposes p WHERE p.Purpose = v.Purpose);
GO

IF OBJECT_ID(N'ai.Policy', N'U') IS NULL
BEGIN
    CREATE TABLE ai.Policy
    (
        PolicyId    tinyint        NOT NULL CONSTRAINT DF_ai_Policy_Id DEFAULT (1),
        AiEnabled   bit            NOT NULL,
        AllowCloud  bit            NOT NULL,
        UpdatedBy   nvarchar(64)   NULL,
        UpdatedUtc  datetime2(3)   NULL,

        CONSTRAINT PK_ai_Policy PRIMARY KEY CLUSTERED (PolicyId),
        CONSTRAINT CK_ai_Policy_OneRow CHECK (PolicyId = 1)
    );

    -- Safe by default: no cloud AI until an Admin allows it. Keep today's behaviour where a job already uses a cloud connection.
    INSERT ai.Policy (PolicyId, AiEnabled, AllowCloud, UpdatedBy, UpdatedUtc)
    SELECT 1, 1,
           IIF(EXISTS (SELECT 1 FROM ai.Purposes p JOIN ai.Connections c ON c.ConnectionId = p.ConnectionId
                       WHERE c.Kind = 'Gemini' OR c.InHouse = 0), 1, 0),
           N'setup', SYSUTCDATETIME();
END
GO

PRINT 'AI connection tables ready.';
