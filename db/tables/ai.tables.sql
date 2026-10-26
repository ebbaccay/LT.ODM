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
    ai.SavedQuestion: Library assistant questions the AI answered, by normalised text, with the look-up (tool) and
                    arguments it chose - never the answer. Status Rule = answered from here without AI (set by a thumbs up
                    or an Admin); Recorded = AI is used; Removed = a thumbs down or an Admin took it out. No asker names.
                    IsPattern = 1: a seeded pattern (seedi.saved-questions.sql), matched with the filter words taken out.
    ai.Job:         slow AI work run in the background (renders, summaries, explanations, readers): who asked, status,
                    the input and the result (JSON, as the API returns it). Finished jobs are deleted after
                    ai.Policy.JobKeepDays (default 7). One row per job; nothing is re-run after an API restart.
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
    -- Up to an hour: a render on an in-house image server without a GPU takes 5-10 minutes or more.
    CONSTRAINT CK_ai_Connections_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 3600),
    -- Gemini is Google's cloud service: never in-house.
    CONSTRAINT CK_ai_Connections_GeminiCloud CHECK (Kind <> 'Gemini' OR InHouse = 0)
);
GO

-- Databases made before October 2026 allow at most 600 s: widen to 3600 s.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ai_Connections_Timeout'
           AND parent_object_id = OBJECT_ID(N'ai.Connections') AND definition NOT LIKE N'%3600%')
BEGIN
    ALTER TABLE ai.Connections DROP CONSTRAINT CK_ai_Connections_Timeout;
    ALTER TABLE ai.Connections ADD CONSTRAINT CK_ai_Connections_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 3600);
END
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

IF OBJECT_ID(N'ai.SavedQuestion', N'U') IS NULL
CREATE TABLE ai.SavedQuestion
(
    SavedQuestionId int IDENTITY(1, 1) NOT NULL,
    QuestionKey     nvarchar(300)  NOT NULL,   -- lower case, no punctuation, singular words: "men jacket per season"
    Question        nvarchar(500)  NOT NULL,   -- the latest wording asked
    Tool            varchar(40)    NOT NULL,
    Arguments       nvarchar(max)  NOT NULL,   -- JSON object of argument name -> text, as shown with the answer
    Status          varchar(16)    NOT NULL CONSTRAINT DF_ai_SavedQuestion_Status DEFAULT ('Recorded'),
    AskCount        int            NOT NULL CONSTRAINT DF_ai_SavedQuestion_AskCount DEFAULT (1),   -- answered by the AI
    RuleHits        int            NOT NULL CONSTRAINT DF_ai_SavedQuestion_RuleHits DEFAULT (0),   -- answered from here
    LastAskedUtc    datetime2(3)   NOT NULL CONSTRAINT DF_ai_SavedQuestion_LastAsked DEFAULT (SYSUTCDATETIME()),
    CreatedUtc      datetime2(3)   NOT NULL CONSTRAINT DF_ai_SavedQuestion_Created DEFAULT (SYSUTCDATETIME()),
    ChangedBy       nvarchar(64)   NULL,       -- who made it a rule or removed it
    ChangedUtc      datetime2(3)   NULL,
    ChangedHow      varchar(16)    NULL,       -- Feedback | Admin | Seed
    IsPattern       bit            NOT NULL CONSTRAINT DF_ai_SavedQuestion_IsPattern DEFAULT (0),   -- key without filter words, {styleno} slots

    CONSTRAINT PK_ai_SavedQuestion PRIMARY KEY CLUSTERED (SavedQuestionId),
    CONSTRAINT UQ_ai_SavedQuestion_Key UNIQUE (QuestionKey),
    CONSTRAINT CK_ai_SavedQuestion_Status CHECK (Status IN ('Recorded', 'Rule', 'Removed')),
    CONSTRAINT CK_ai_SavedQuestion_Arguments CHECK (ISJSON(Arguments) = 1)
);
GO
IF COL_LENGTH(N'ai.SavedQuestion', N'IsPattern') IS NULL
    ALTER TABLE ai.SavedQuestion ADD IsPattern bit NOT NULL CONSTRAINT DF_ai_SavedQuestion_IsPattern DEFAULT (0);
GO

-- AI call limits: how many calls one AI server gets at once (per connection; connections with the same address share the
-- lowest), and how many AI tasks one user may have queued or running. LimitCalls = 0 turns both off.
IF COL_LENGTH(N'ai.Connections', N'MaxConcurrent') IS NULL
BEGIN
    ALTER TABLE ai.Connections ADD MaxConcurrent smallint NOT NULL CONSTRAINT DF_ai_Connections_MaxConcurrent DEFAULT (1)
        CONSTRAINT CK_ai_Connections_MaxConcurrent CHECK (MaxConcurrent BETWEEN 1 AND 64);
    -- Cloud services answer many calls at once; an in-house CPU server one at a time.
    EXEC (N'UPDATE ai.Connections SET MaxConcurrent = 4 WHERE Kind = ''Gemini'' OR InHouse = 0');
END
GO
IF COL_LENGTH(N'ai.Policy', N'LimitCalls') IS NULL
    ALTER TABLE ai.Policy ADD LimitCalls bit NOT NULL CONSTRAINT DF_ai_Policy_LimitCalls DEFAULT (1);
GO
IF COL_LENGTH(N'ai.Policy', N'MaxTasksPerUser') IS NULL
    ALTER TABLE ai.Policy ADD MaxTasksPerUser smallint NOT NULL CONSTRAINT DF_ai_Policy_MaxTasksPerUser DEFAULT (3)
        CONSTRAINT CK_ai_Policy_MaxTasksPerUser CHECK (MaxTasksPerUser BETWEEN 1 AND 50);
GO

IF COL_LENGTH(N'ai.Policy', N'JobKeepDays') IS NULL
    ALTER TABLE ai.Policy ADD JobKeepDays smallint NOT NULL CONSTRAINT DF_ai_Policy_JobKeepDays DEFAULT (7)
        CONSTRAINT CK_ai_Policy_JobKeepDays CHECK (JobKeepDays BETWEEN 1 AND 365);
GO

IF OBJECT_ID(N'ai.Job', N'U') IS NULL
CREATE TABLE ai.Job
(
    JobId        int IDENTITY(1, 1) NOT NULL,
    Kind         varchar(32)    NOT NULL,   -- render | renderPrompt | bomExplain | compareSummary | materialRead | gapsAskAi | importCodes
    Lane         varchar(8)     NOT NULL,   -- text | image: how many run at once is set per lane
    Status       varchar(16)    NOT NULL CONSTRAINT DF_ai_Job_Status DEFAULT ('Queued'),
    OwnerName    nvarchar(64)   NOT NULL,   -- user name of who asked
    OwnerId      nvarchar(64)   NOT NULL,   -- sign-in subject (sub), to show the job to its owner only
    TaskKey      nvarchar(150)  NULL,       -- the page's key for the answer, e.g. render:123 (AI task tray)
    Label        nvarchar(150)  NULL,       -- shown in the tray and the Admin list, e.g. the style number
    Url          nvarchar(400)  NULL,       -- the page to go back to
    Input        nvarchar(max)  NULL,       -- the request, JSON
    Result       nvarchar(max)  NULL,       -- the answer, JSON
    Error        nvarchar(1000) NULL,
    ErrorStatus  smallint       NULL,       -- the HTTP status the request would have answered with
    Seen         bit            NOT NULL CONSTRAINT DF_ai_Job_Seen DEFAULT (0),   -- the owner's page has shown the answer
    CreatedUtc   datetime2(3)   NOT NULL CONSTRAINT DF_ai_Job_Created DEFAULT (SYSUTCDATETIME()),
    StartedUtc   datetime2(3)   NULL,
    FinishedUtc  datetime2(3)   NULL,
    CancelledBy  nvarchar(64)   NULL,

    CONSTRAINT PK_ai_Job PRIMARY KEY CLUSTERED (JobId),
    CONSTRAINT CK_ai_Job_Status CHECK (Status IN ('Queued', 'Running', 'Done', 'Failed', 'Cancelled', 'Interrupted')),
    CONSTRAINT CK_ai_Job_Lane CHECK (Lane IN ('text', 'image')),
    CONSTRAINT CK_ai_Job_Input CHECK (Input IS NULL OR ISJSON(Input) = 1),
    CONSTRAINT CK_ai_Job_Result CHECK (Result IS NULL OR ISJSON(Result) = 1)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_Job_Owner' AND object_id = OBJECT_ID(N'ai.Job'))
    CREATE INDEX IX_ai_Job_Owner ON ai.Job (OwnerId, CreatedUtc DESC) INCLUDE (Status, Seen);
GO

PRINT 'AI connection tables ready.';
