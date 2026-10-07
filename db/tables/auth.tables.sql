/*
    LT ODM Style Library - authentication schema
    Target: SQL Server 2025, database compatibility level 170
      - REGEXP_LIKE in CHECK constraints (email / user name format)
      - native json data type (auth.LoginAudit.Details)

    Passwords are never stored: PasswordHash holds an ASP.NET Core Identity v3 hash
    (PBKDF2-HMAC-SHA512, per-user salt). Refresh and password-reset tokens are stored as
    SHA-256 hashes only; the raw tokens exist only in the user's cookie / email link.

    Idempotent: safe to run more than once.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) < 170
    THROW 50000, 'Compatibility level 170 (SQL Server 2025) is required. Run: ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;', 1;
GO

IF SCHEMA_ID(N'auth') IS NULL
    EXEC (N'CREATE SCHEMA auth AUTHORIZATION dbo;');
GO

/* ---------- Users ---------- */
IF OBJECT_ID(N'auth.Users', N'U') IS NULL
CREATE TABLE auth.Users
(
    UserId              int IDENTITY(1, 1)  NOT NULL,
    UserName            nvarchar(64)        COLLATE Latin1_General_100_CI_AS NOT NULL,
    Email               nvarchar(256)       COLLATE Latin1_General_100_CI_AS NOT NULL,
    DisplayName         nvarchar(128)       NOT NULL,
    PasswordHash        nvarchar(256)       NOT NULL,
    -- Changes whenever the password changes; carried in tokens so old sessions can be invalidated.
    SecurityStamp       uniqueidentifier    NOT NULL CONSTRAINT DF_auth_Users_SecurityStamp DEFAULT (NEWID()),
    IsActive            bit                 NOT NULL CONSTRAINT DF_auth_Users_IsActive DEFAULT (1),
    MustChangePassword  bit                 NOT NULL CONSTRAINT DF_auth_Users_MustChangePassword DEFAULT (0),
    FailedLoginCount    int                 NOT NULL CONSTRAINT DF_auth_Users_FailedLoginCount DEFAULT (0),
    LockoutEndUtc       datetime2(3)        NULL,
    LastLoginUtc        datetime2(3)        NULL,
    PasswordChangedUtc  datetime2(3)        NOT NULL CONSTRAINT DF_auth_Users_PasswordChangedUtc DEFAULT (SYSUTCDATETIME()),
    CreatedUtc          datetime2(3)        NOT NULL CONSTRAINT DF_auth_Users_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    RowVer              rowversion          NOT NULL,

    CONSTRAINT PK_auth_Users PRIMARY KEY CLUSTERED (UserId),
    CONSTRAINT UQ_auth_Users_UserName UNIQUE (UserName),
    CONSTRAINT UQ_auth_Users_Email UNIQUE (Email),
    CONSTRAINT CK_auth_Users_UserName CHECK (REGEXP_LIKE(UserName, N'^[A-Za-z0-9._-]{3,64}$')),
    CONSTRAINT CK_auth_Users_Email CHECK (REGEXP_LIKE(Email, N'^[A-Za-z0-9._%+''-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$')),
    CONSTRAINT CK_auth_Users_FailedLoginCount CHECK (FailedLoginCount >= 0)
);
GO

/* TMS user attributes used by the ported TMS screens: UserGroup 'FTY' = factory user, Location = factory/office code. */
IF COL_LENGTH(N'auth.Users', N'UserGroup') IS NULL
    ALTER TABLE auth.Users ADD UserGroup nvarchar(16) NULL;
GO
IF COL_LENGTH(N'auth.Users', N'Location') IS NULL
    ALTER TABLE auth.Users ADD Location nvarchar(32) NULL;
GO

/* ---------- Roles ---------- */
IF OBJECT_ID(N'auth.Roles', N'U') IS NULL
CREATE TABLE auth.Roles
(
    RoleId          int IDENTITY(1, 1)  NOT NULL,
    Name            nvarchar(64)        COLLATE Latin1_General_100_CI_AS NOT NULL,
    DisplayName     nvarchar(128)       NULL,
    Description     nvarchar(256)       NULL,

    CONSTRAINT PK_auth_Roles PRIMARY KEY CLUSTERED (RoleId),
    CONSTRAINT UQ_auth_Roles_Name UNIQUE (Name),
    -- Role names are used in sign-in tokens and menu rules: letters, digits, underscore.
    CONSTRAINT CK_auth_Roles_Name CHECK (REGEXP_LIKE(Name, N'^[A-Za-z][A-Za-z0-9_]{1,63}$'))
);
GO
IF COL_LENGTH(N'auth.Roles', N'DisplayName') IS NULL
    ALTER TABLE auth.Roles ADD DisplayName nvarchar(128) NULL;
GO

IF OBJECT_ID(N'auth.UserRoles', N'U') IS NULL
CREATE TABLE auth.UserRoles
(
    UserId  int NOT NULL,
    RoleId  int NOT NULL,

    CONSTRAINT PK_auth_UserRoles PRIMARY KEY CLUSTERED (UserId, RoleId),
    CONSTRAINT FK_auth_UserRoles_Users FOREIGN KEY (UserId) REFERENCES auth.Users (UserId) ON DELETE CASCADE,
    CONSTRAINT FK_auth_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES auth.Roles (RoleId)
);
GO

/* ---------- Password history (blocks re-use of recent passwords) ---------- */
IF OBJECT_ID(N'auth.PasswordHistory', N'U') IS NULL
CREATE TABLE auth.PasswordHistory
(
    PasswordHistoryId   bigint IDENTITY(1, 1)   NOT NULL,
    UserId              int                     NOT NULL,
    PasswordHash        nvarchar(256)           NOT NULL,
    CreatedUtc          datetime2(3)            NOT NULL CONSTRAINT DF_auth_PasswordHistory_CreatedUtc DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_auth_PasswordHistory PRIMARY KEY CLUSTERED (PasswordHistoryId),
    CONSTRAINT FK_auth_PasswordHistory_Users FOREIGN KEY (UserId) REFERENCES auth.Users (UserId) ON DELETE CASCADE
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_auth_PasswordHistory_User' AND object_id = OBJECT_ID(N'auth.PasswordHistory'))
    CREATE INDEX IX_auth_PasswordHistory_User ON auth.PasswordHistory (UserId, CreatedUtc DESC) INCLUDE (PasswordHash);
GO

/* ---------- Refresh tokens (rotated on every use; FamilyId detects token re-use) ---------- */
IF OBJECT_ID(N'auth.RefreshTokens', N'U') IS NULL
CREATE TABLE auth.RefreshTokens
(
    RefreshTokenId      bigint IDENTITY(1, 1)   NOT NULL,
    UserId              int                     NOT NULL,
    TokenHash           binary(32)              NOT NULL,   -- SHA-256 of the raw token
    FamilyId            uniqueidentifier        NOT NULL,   -- all rotations of one sign-in
    IsPersistent        bit                     NOT NULL,   -- "Keep me signed in"
    ExpiresUtc          datetime2(3)            NOT NULL,
    CreatedUtc          datetime2(3)            NOT NULL CONSTRAINT DF_auth_RefreshTokens_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    CreatedByIp         varchar(45)             NULL,
    RevokedUtc          datetime2(3)            NULL,
    ReplacedByTokenId   bigint                  NULL,

    CONSTRAINT PK_auth_RefreshTokens PRIMARY KEY CLUSTERED (RefreshTokenId),
    CONSTRAINT UQ_auth_RefreshTokens_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_auth_RefreshTokens_Users FOREIGN KEY (UserId) REFERENCES auth.Users (UserId) ON DELETE CASCADE
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_auth_RefreshTokens_User' AND object_id = OBJECT_ID(N'auth.RefreshTokens'))
    CREATE INDEX IX_auth_RefreshTokens_User ON auth.RefreshTokens (UserId) WHERE RevokedUtc IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_auth_RefreshTokens_Family' AND object_id = OBJECT_ID(N'auth.RefreshTokens'))
    CREATE INDEX IX_auth_RefreshTokens_Family ON auth.RefreshTokens (FamilyId);
GO

/* ---------- Password reset tokens (single use, short-lived) ---------- */
IF OBJECT_ID(N'auth.PasswordResetTokens', N'U') IS NULL
CREATE TABLE auth.PasswordResetTokens
(
    PasswordResetTokenId    bigint IDENTITY(1, 1)   NOT NULL,
    UserId                  int                     NOT NULL,
    TokenHash               binary(32)              NOT NULL,   -- SHA-256 of the raw token
    ExpiresUtc              datetime2(3)            NOT NULL,
    CreatedUtc              datetime2(3)            NOT NULL CONSTRAINT DF_auth_PasswordResetTokens_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    RequestedIp             varchar(45)             NULL,
    UsedUtc                 datetime2(3)            NULL,

    CONSTRAINT PK_auth_PasswordResetTokens PRIMARY KEY CLUSTERED (PasswordResetTokenId),
    CONSTRAINT UQ_auth_PasswordResetTokens_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_auth_PasswordResetTokens_Users FOREIGN KEY (UserId) REFERENCES auth.Users (UserId) ON DELETE CASCADE
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_auth_PasswordResetTokens_User' AND object_id = OBJECT_ID(N'auth.PasswordResetTokens'))
    CREATE INDEX IX_auth_PasswordResetTokens_User ON auth.PasswordResetTokens (UserId, CreatedUtc DESC);
GO

/* ---------- Security audit ---------- */
IF OBJECT_ID(N'auth.LoginAudit', N'U') IS NULL
CREATE TABLE auth.LoginAudit
(
    LoginAuditId    bigint IDENTITY(1, 1)   NOT NULL,
    UserId          int                     NULL,       -- NULL when the login name matched no user
    LoginName       nvarchar(256)           NULL,
    EventType       varchar(32)             NOT NULL,
    IpAddress       varchar(45)             NULL,
    UserAgent       nvarchar(512)           NULL,
    OccurredUtc     datetime2(3)            NOT NULL CONSTRAINT DF_auth_LoginAudit_OccurredUtc DEFAULT (SYSUTCDATETIME()),
    Details         json                    NULL,       -- SQL Server 2025 native JSON

    CONSTRAINT PK_auth_LoginAudit PRIMARY KEY CLUSTERED (LoginAuditId),
    CONSTRAINT FK_auth_LoginAudit_Users FOREIGN KEY (UserId) REFERENCES auth.Users (UserId) ON DELETE SET NULL,
    CONSTRAINT CK_auth_LoginAudit_EventType CHECK (EventType IN (
        'LoginSucceeded', 'LoginFailed', 'LockedOut', 'Logout',
        'RefreshTokenReuse', 'PasswordResetRequested', 'PasswordReset', 'PasswordChanged', 'UserCreated'))
);
GO

-- Event types added after the first release (re-created so existing databases pick them up).
ALTER TABLE auth.LoginAudit DROP CONSTRAINT IF EXISTS CK_auth_LoginAudit_EventType;
ALTER TABLE auth.LoginAudit ADD CONSTRAINT CK_auth_LoginAudit_EventType CHECK (EventType IN (
    'LoginSucceeded', 'LoginFailed', 'LockedOut', 'Logout',
    'RefreshTokenReuse', 'PasswordResetRequested', 'PasswordReset', 'PasswordChanged', 'UserCreated',
    'AccessChanged', 'RoleChanged'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_auth_LoginAudit_Occurred' AND object_id = OBJECT_ID(N'auth.LoginAudit'))
    CREATE INDEX IX_auth_LoginAudit_Occurred ON auth.LoginAudit (OccurredUtc DESC) INCLUDE (UserId, EventType);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_auth_LoginAudit_User' AND object_id = OBJECT_ID(N'auth.LoginAudit'))
    CREATE INDEX IX_auth_LoginAudit_User ON auth.LoginAudit (UserId, OccurredUtc DESC);
GO
