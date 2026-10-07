/*
    LT ODM Style Library - authentication stored procedures (SQL Server 2025).
    Run after db/tables/auth.tables.sql. All CREATE OR ALTER: safe to re-run.
*/
SET NOCOUNT ON;
GO

/* Writes one security audit row. @Details must be valid JSON or NULL. */
CREATE OR ALTER PROCEDURE auth.usp_Audit_Insert
    @UserId     int             = NULL,
    @LoginName  nvarchar(256)   = NULL,
    @EventType  varchar(32),
    @IpAddress  varchar(45)     = NULL,
    @UserAgent  nvarchar(512)   = NULL,
    @Details    nvarchar(max)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT auth.LoginAudit (UserId, LoginName, EventType, IpAddress, UserAgent, Details)
    VALUES (@UserId, @LoginName, @EventType, @IpAddress, LEFT(@UserAgent, 512), CAST(@Details AS json));
END
GO

/* Returns one user (by id, user name or email) with roles as a comma-separated list. */
CREATE OR ALTER PROCEDURE auth.usp_User_Get
    @UserId int             = NULL,
    @Login  nvarchar(256)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1)
        u.UserId, u.UserName, u.Email, u.DisplayName, u.PasswordHash, u.SecurityStamp,
        u.IsActive, u.MustChangePassword, u.FailedLoginCount, u.LockoutEndUtc, u.UserGroup, u.Location,
        Roles = (SELECT STRING_AGG(r.Name, N',') FROM auth.UserRoles ur JOIN auth.Roles r ON r.RoleId = ur.RoleId WHERE ur.UserId = u.UserId)
    FROM auth.Users u
    WHERE (@UserId IS NOT NULL AND u.UserId = @UserId)
       OR (@UserId IS NULL AND (u.UserName = @Login OR u.Email = @Login));
END
GO

/*
    Records a sign-in attempt for a known user: resets or increments the failure counter,
    applies the lockout and writes the audit row. Returns IsLockedOut and LockoutEndUtc.
*/
CREATE OR ALTER PROCEDURE auth.usp_User_RecordSignIn
    @UserId             int,
    @Succeeded          bit,
    @MaxFailedAttempts  int,
    @LockoutMinutes     int,
    @LoginName          nvarchar(256)   = NULL,
    @IpAddress          varchar(45)     = NULL,
    @UserAgent          nvarchar(512)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @lockedOut bit = 0, @lockoutEnd datetime2(3) = NULL;

    BEGIN TRANSACTION;

    IF @Succeeded = 1
    BEGIN
        UPDATE auth.Users
        SET FailedLoginCount = 0, LockoutEndUtc = NULL, LastLoginUtc = SYSUTCDATETIME()
        WHERE UserId = @UserId;

        EXEC auth.usp_Audit_Insert @UserId, @LoginName, 'LoginSucceeded', @IpAddress, @UserAgent;
    END
    ELSE
    BEGIN
        -- Column references on the right-hand side read the values before this update.
        UPDATE auth.Users
        SET @lockedOut       = IIF(FailedLoginCount + 1 >= @MaxFailedAttempts, 1, 0),
            LockoutEndUtc    = IIF(FailedLoginCount + 1 >= @MaxFailedAttempts, DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME()), LockoutEndUtc),
            @lockoutEnd      = IIF(FailedLoginCount + 1 >= @MaxFailedAttempts, DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME()), LockoutEndUtc),
            FailedLoginCount = IIF(FailedLoginCount + 1 >= @MaxFailedAttempts, 0, FailedLoginCount + 1)
        WHERE UserId = @UserId;

        DECLARE @event varchar(32) = IIF(@lockedOut = 1, 'LockedOut', 'LoginFailed');
        EXEC auth.usp_Audit_Insert @UserId, @LoginName, @event, @IpAddress, @UserAgent;
    END

    COMMIT TRANSACTION;

    SELECT IsLockedOut = @lockedOut, LockoutEndUtc = @lockoutEnd;
END
GO

/* Creates a user with an already-hashed password. @Roles is a comma-separated list of role names. */
CREATE OR ALTER PROCEDURE auth.usp_User_Create
    @UserName           nvarchar(64),
    @Email              nvarchar(256),
    @DisplayName        nvarchar(128),
    @PasswordHash       nvarchar(256),
    @MustChangePassword bit             = 0,
    @Roles              nvarchar(1000)  = N'',
    @UserGroup          nvarchar(16)    = NULL,
    @Location           nvarchar(32)    = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    INSERT auth.Users (UserName, Email, DisplayName, PasswordHash, MustChangePassword, UserGroup, Location)
    VALUES (@UserName, @Email, @DisplayName, @PasswordHash, @MustChangePassword, @UserGroup, @Location);

    DECLARE @userId int = CAST(SCOPE_IDENTITY() AS int);

    INSERT auth.PasswordHistory (UserId, PasswordHash) VALUES (@userId, @PasswordHash);

    INSERT auth.UserRoles (UserId, RoleId)
    SELECT @userId, r.RoleId
    FROM auth.Roles r
    WHERE r.Name IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@Roles, N',') WHERE LTRIM(RTRIM(value)) <> N'');

    EXEC auth.usp_Audit_Insert @userId, @UserName, 'UserCreated';

    COMMIT TRANSACTION;

    SELECT UserId = @userId;
END
GO

/* Current hash plus the most recent historic hashes, for the "no re-use" check. */
CREATE OR ALTER PROCEDURE auth.usp_User_GetPasswordHistory
    @UserId int,
    @Count  int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Count) PasswordHash
    FROM auth.PasswordHistory
    WHERE UserId = @UserId
    ORDER BY CreatedUtc DESC, PasswordHistoryId DESC;
END
GO

/*
    Sets a new password (change or reset). When @ResetTokenHash is supplied the token is consumed
    in the same transaction; an invalid/expired/used token raises error 50010.
    Also: new security stamp, clears lockout, trims history, revokes all refresh tokens
    (signs the user out everywhere) and voids any other pending reset links.
*/
CREATE OR ALTER PROCEDURE auth.usp_User_SetPassword
    @UserId             int,
    @PasswordHash       nvarchar(256),
    @HistoryToKeep      int,
    @EventType          varchar(32),            -- 'PasswordChanged' or 'PasswordReset'
    @ResetTokenHash     binary(32)      = NULL,
    @IpAddress          varchar(45)     = NULL,
    @UserAgent          nvarchar(512)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @now datetime2(3) = SYSUTCDATETIME();

    BEGIN TRANSACTION;

    IF @ResetTokenHash IS NOT NULL
    BEGIN
        UPDATE auth.PasswordResetTokens
        SET UsedUtc = @now
        WHERE TokenHash = @ResetTokenHash AND UserId = @UserId AND UsedUtc IS NULL AND ExpiresUtc > @now;

        IF @@ROWCOUNT = 0
            THROW 50010, 'The password reset link is invalid or has expired.', 1;
    END

    UPDATE auth.Users
    SET PasswordHash = @PasswordHash,
        SecurityStamp = NEWID(),
        PasswordChangedUtc = @now,
        MustChangePassword = 0,
        FailedLoginCount = 0,
        LockoutEndUtc = NULL
    WHERE UserId = @UserId;

    INSERT auth.PasswordHistory (UserId, PasswordHash) VALUES (@UserId, @PasswordHash);

    DELETE h
    FROM auth.PasswordHistory h
    WHERE h.UserId = @UserId
      AND h.PasswordHistoryId NOT IN (
            SELECT TOP (@HistoryToKeep) PasswordHistoryId
            FROM auth.PasswordHistory
            WHERE UserId = @UserId
            ORDER BY CreatedUtc DESC, PasswordHistoryId DESC);

    UPDATE auth.RefreshTokens SET RevokedUtc = @now WHERE UserId = @UserId AND RevokedUtc IS NULL;
    UPDATE auth.PasswordResetTokens SET UsedUtc = @now WHERE UserId = @UserId AND UsedUtc IS NULL;

    EXEC auth.usp_Audit_Insert @UserId, NULL, @EventType, @IpAddress, @UserAgent;

    COMMIT TRANSACTION;
END
GO

/* Stores a new refresh token (start of a sign-in session). */
CREATE OR ALTER PROCEDURE auth.usp_RefreshToken_Create
    @UserId         int,
    @TokenHash      binary(32),
    @IsPersistent   bit,
    @ExpiresUtc     datetime2(3),
    @IpAddress      varchar(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT auth.RefreshTokens (UserId, TokenHash, FamilyId, IsPersistent, ExpiresUtc, CreatedByIp)
    VALUES (@UserId, @TokenHash, NEWID(), @IsPersistent, @ExpiresUtc, @IpAddress);
END
GO

/*
    Rotates a refresh token. Status:
      Ok       - new token stored; user columns returned
      Invalid  - unknown token, expired, or user inactive
      Stale    - token was rotated seconds ago (e.g. two tabs refreshing at once); client should retry
      Reused   - an old token was presented again: whole family revoked (possible theft)
*/
CREATE OR ALTER PROCEDURE auth.usp_RefreshToken_Rotate
    @OldTokenHash           binary(32),
    @NewTokenHash           binary(32),
    @PersistentLifetimeMin  int,
    @SessionLifetimeMin     int,
    @IpAddress              varchar(45)     = NULL,
    @UserAgent              nvarchar(512)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @now datetime2(3) = SYSUTCDATETIME();
    DECLARE @id bigint, @userId int, @familyId uniqueidentifier, @isPersistent bit,
            @expires datetime2(3), @revoked datetime2(3), @replacedBy bigint;

    BEGIN TRANSACTION;

    SELECT @id = RefreshTokenId, @userId = UserId, @familyId = FamilyId, @isPersistent = IsPersistent,
           @expires = ExpiresUtc, @revoked = RevokedUtc, @replacedBy = ReplacedByTokenId
    FROM auth.RefreshTokens WITH (UPDLOCK, HOLDLOCK)
    WHERE TokenHash = @OldTokenHash;

    IF @id IS NULL OR @expires <= @now
       OR NOT EXISTS (SELECT 1 FROM auth.Users WHERE UserId = @userId AND IsActive = 1)
    BEGIN
        COMMIT TRANSACTION;
        SELECT Status = 'Invalid';
        RETURN;
    END

    IF @revoked IS NOT NULL
    BEGIN
        IF @replacedBy IS NOT NULL AND @revoked > DATEADD(SECOND, -30, @now)
        BEGIN
            COMMIT TRANSACTION;
            SELECT Status = 'Stale';
            RETURN;
        END

        UPDATE auth.RefreshTokens SET RevokedUtc = @now WHERE FamilyId = @familyId AND RevokedUtc IS NULL;
        EXEC auth.usp_Audit_Insert @userId, NULL, 'RefreshTokenReuse', @IpAddress, @UserAgent;
        COMMIT TRANSACTION;
        SELECT Status = 'Reused';
        RETURN;
    END

    DECLARE @newExpires datetime2(3) =
        DATEADD(MINUTE, IIF(@isPersistent = 1, @PersistentLifetimeMin, @SessionLifetimeMin), @now);

    INSERT auth.RefreshTokens (UserId, TokenHash, FamilyId, IsPersistent, ExpiresUtc, CreatedByIp)
    VALUES (@userId, @NewTokenHash, @familyId, @isPersistent, @newExpires, @IpAddress);

    UPDATE auth.RefreshTokens
    SET RevokedUtc = @now, ReplacedByTokenId = CAST(SCOPE_IDENTITY() AS bigint)
    WHERE RefreshTokenId = @id;

    COMMIT TRANSACTION;

    SELECT Status = 'Ok', IsPersistent = @isPersistent, ExpiresUtc = @newExpires,
           u.UserId, u.UserName, u.Email, u.DisplayName, u.SecurityStamp, u.MustChangePassword, u.UserGroup, u.Location,
           Roles = (SELECT STRING_AGG(r.Name, N',') FROM auth.UserRoles ur JOIN auth.Roles r ON r.RoleId = ur.RoleId WHERE ur.UserId = u.UserId)
    FROM auth.Users u
    WHERE u.UserId = @userId;
END
GO

/* Sign-out: revokes the presented token and every rotation of the same sign-in. */
CREATE OR ALTER PROCEDURE auth.usp_RefreshToken_Revoke
    @TokenHash  binary(32),
    @IpAddress  varchar(45)     = NULL,
    @UserAgent  nvarchar(512)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @familyId uniqueidentifier, @userId int;
    SELECT @familyId = FamilyId, @userId = UserId FROM auth.RefreshTokens WHERE TokenHash = @TokenHash;
    IF @familyId IS NULL RETURN;

    BEGIN TRANSACTION;
    UPDATE auth.RefreshTokens SET RevokedUtc = SYSUTCDATETIME() WHERE FamilyId = @familyId AND RevokedUtc IS NULL;
    EXEC auth.usp_Audit_Insert @userId, NULL, 'Logout', @IpAddress, @UserAgent;
    COMMIT TRANSACTION;
END
GO

/*
    Creates a password reset token for an active user with this email.
    Returns the user (to address the email) or no row when the email is unknown, the user is
    inactive, or @MaxPerHour requests were already made - the API answers the same in every case.
*/
CREATE OR ALTER PROCEDURE auth.usp_PasswordReset_Create
    @Email          nvarchar(256),
    @TokenHash      binary(32),
    @ExpiresUtc     datetime2(3),
    @MaxPerHour     int,
    @IpAddress      varchar(45)     = NULL,
    @UserAgent      nvarchar(512)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @userId int, @userName nvarchar(64), @displayName nvarchar(128), @userEmail nvarchar(256);
    SELECT @userId = UserId, @userName = UserName, @displayName = DisplayName, @userEmail = Email
    FROM auth.Users
    WHERE Email = @Email AND IsActive = 1;

    DECLARE @details nvarchar(max) = (SELECT matched = IIF(@userId IS NULL, 0, 1) FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);

    IF @userId IS NULL
    BEGIN
        EXEC auth.usp_Audit_Insert NULL, @Email, 'PasswordResetRequested', @IpAddress, @UserAgent, @details;
        RETURN;
    END

    IF (SELECT COUNT(*) FROM auth.PasswordResetTokens
        WHERE UserId = @userId AND CreatedUtc > DATEADD(HOUR, -1, SYSUTCDATETIME())) >= @MaxPerHour
        RETURN;

    BEGIN TRANSACTION;
    -- Only the newest link works.
    UPDATE auth.PasswordResetTokens SET UsedUtc = SYSUTCDATETIME() WHERE UserId = @userId AND UsedUtc IS NULL;
    INSERT auth.PasswordResetTokens (UserId, TokenHash, ExpiresUtc, RequestedIp)
    VALUES (@userId, @TokenHash, @ExpiresUtc, @IpAddress);
    EXEC auth.usp_Audit_Insert @userId, @Email, 'PasswordResetRequested', @IpAddress, @UserAgent, @details;
    COMMIT TRANSACTION;

    SELECT UserId = @userId, UserName = @userName, Email = @userEmail, DisplayName = @displayName;
END
GO

/* Returns the user for a valid (unused, unexpired) reset token, or no row. */
CREATE OR ALTER PROCEDURE auth.usp_PasswordReset_Get
    @TokenHash binary(32)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.UserId, u.UserName, u.Email, u.DisplayName
    FROM auth.PasswordResetTokens t
    JOIN auth.Users u ON u.UserId = t.UserId
    WHERE t.TokenHash = @TokenHash
      AND t.UsedUtc IS NULL
      AND t.ExpiresUtc > SYSUTCDATETIME()
      AND u.IsActive = 1;
END
GO
