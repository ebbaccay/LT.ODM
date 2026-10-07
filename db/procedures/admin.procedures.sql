/*
    LT ODM Style Library - menu, role and user-access procedures used by Settings
    (replaces the TMS web_rd_nav_* / web_rd_roles_* / web_rd_user_roles_* procedures).
    SQL Server 2025. Run after auth.tables.sql, nav.tables.sql and auth.procedures.sql. Safe to re-run.
*/
SET NOCOUNT ON;
GO

/* ================================ Menu ================================ */

/* Menu for one user: visible groups and items, filtered by the item's roles. */
CREATE OR ALTER PROCEDURE nav.usp_Menu_GetForUser
    @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT g.GroupId, g.Text AS GroupText, g.Icon AS GroupIcon, g.Slot AS GroupSlot, g.SortOrder AS GroupSortOrder,
           i.ItemId, i.Text AS ItemText, i.Route AS ItemRoute, i.Icon AS ItemIcon, i.SortOrder AS ItemSortOrder
    FROM nav.Groups g
    JOIN nav.Items i ON i.GroupId = g.GroupId AND i.IsVisible = 1
    WHERE g.IsVisible = 1
      AND (
            NOT EXISTS (SELECT 1 FROM nav.ItemRoles ir WHERE ir.ItemId = i.ItemId)
         OR EXISTS (SELECT 1
                    FROM nav.ItemRoles ir
                    JOIN auth.UserRoles ur ON ur.RoleId = ir.RoleId AND ur.UserId = @UserId
                    WHERE ir.ItemId = i.ItemId)
          )
    ORDER BY g.SortOrder, g.GroupId, i.SortOrder, i.ItemId;
END
GO

/* Full menu for the editor: every group (also empty and hidden ones) with each item's role names. */
CREATE OR ALTER PROCEDURE nav.usp_Config_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT g.GroupId, g.Text AS GroupText, g.Icon AS GroupIcon, g.Slot AS GroupSlot, g.SortOrder AS GroupSortOrder,
           g.IsVisible AS GroupIsVisible,
           i.ItemId, i.Text AS ItemText, i.Route AS ItemRoute, i.Icon AS ItemIcon, i.SortOrder AS ItemSortOrder,
           i.IsVisible AS ItemIsVisible,
           AllowedRoles = (SELECT STRING_AGG(r.Name, N',') WITHIN GROUP (ORDER BY r.Name)
                           FROM nav.ItemRoles ir JOIN auth.Roles r ON r.RoleId = ir.RoleId
                           WHERE ir.ItemId = i.ItemId)
    FROM nav.Groups g
    LEFT JOIN nav.Items i ON i.GroupId = g.GroupId
    ORDER BY g.SortOrder, g.GroupId, i.SortOrder, i.ItemId;
END
GO

/* Insert (@GroupId = 0) or update a group. Returns the GroupId. */
CREATE OR ALTER PROCEDURE nav.usp_Group_Upsert
    @GroupId    int,
    @Text       nvarchar(100),
    @Icon       varchar(64),
    @Slot       varchar(16),
    @SortOrder  int,
    @IsVisible  bit,
    @UpdatedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @GroupId > 0
    BEGIN
        UPDATE nav.Groups
        SET Text = @Text, Icon = @Icon, Slot = @Slot, SortOrder = @SortOrder, IsVisible = @IsVisible,
            UpdatedUtc = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
        WHERE GroupId = @GroupId;
        IF @@ROWCOUNT = 0 THROW 50020, 'Menu group not found.', 1;
        SELECT GroupId = @GroupId;
    END
    ELSE
    BEGIN
        INSERT nav.Groups (Text, Icon, Slot, SortOrder, IsVisible, UpdatedBy)
        VALUES (@Text, @Icon, @Slot, @SortOrder, @IsVisible, @UpdatedBy);
        SELECT GroupId = CAST(SCOPE_IDENTITY() AS int);
    END
END
GO

/* Deletes a group and its items. */
CREATE OR ALTER PROCEDURE nav.usp_Group_Delete
    @GroupId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE nav.Groups WHERE GroupId = @GroupId;
END
GO

/*
    Insert (@ItemId = 0) or update an item and replace its roles in one transaction.
    @RoleNames: comma-separated role names; empty = visible to every signed-in user.
    Returns the ItemId. Route must be unique (error 50021).
*/
CREATE OR ALTER PROCEDURE nav.usp_Item_Save
    @ItemId     int,
    @GroupId    int,
    @Text       nvarchar(100),
    @Route      varchar(200),
    @Icon       varchar(64),
    @SortOrder  int,
    @IsVisible  bit,
    @RoleNames  nvarchar(1000),
    @UpdatedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM nav.Items WHERE Route = @Route AND ItemId <> @ItemId)
        THROW 50021, 'Another menu item already uses this route.', 1;

    BEGIN TRANSACTION;

    IF @ItemId > 0
    BEGIN
        UPDATE nav.Items
        SET GroupId = @GroupId, Text = @Text, Route = @Route, Icon = @Icon, SortOrder = @SortOrder, IsVisible = @IsVisible,
            UpdatedUtc = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
        WHERE ItemId = @ItemId;
        IF @@ROWCOUNT = 0 THROW 50022, 'Menu item not found.', 1;
    END
    ELSE
    BEGIN
        INSERT nav.Items (GroupId, Text, Route, Icon, SortOrder, IsVisible, UpdatedBy)
        VALUES (@GroupId, @Text, @Route, @Icon, @SortOrder, @IsVisible, @UpdatedBy);
        SET @ItemId = CAST(SCOPE_IDENTITY() AS int);
    END

    DELETE nav.ItemRoles WHERE ItemId = @ItemId;
    INSERT nav.ItemRoles (ItemId, RoleId)
    SELECT @ItemId, r.RoleId
    FROM auth.Roles r
    WHERE r.Name IN (SELECT TRIM(value) FROM STRING_SPLIT(@RoleNames, N',') WHERE TRIM(value) <> N'');

    COMMIT TRANSACTION;
    SELECT ItemId = @ItemId;
END
GO

CREATE OR ALTER PROCEDURE nav.usp_Item_Delete
    @ItemId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE nav.Items WHERE ItemId = @ItemId;
END
GO

/* ================================ Roles ================================ */

CREATE OR ALTER PROCEDURE auth.usp_Role_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RoleId, r.Name, r.DisplayName, r.Description,
           UserCount = (SELECT COUNT(*) FROM auth.UserRoles ur WHERE ur.RoleId = r.RoleId)
    FROM auth.Roles r
    ORDER BY r.Name;
END
GO

/* Insert (@RoleId = 0) or update. The name of an existing role cannot change (it is used in tokens and menu rules). */
CREATE OR ALTER PROCEDURE auth.usp_Role_Save
    @RoleId         int,
    @Name           nvarchar(64),
    @DisplayName    nvarchar(128),
    @Description    nvarchar(256),
    @ChangedBy      nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    IF @RoleId > 0
    BEGIN
        UPDATE auth.Roles SET DisplayName = @DisplayName, Description = @Description WHERE RoleId = @RoleId;
        IF @@ROWCOUNT = 0 THROW 50030, 'Role not found.', 1;
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM auth.Roles WHERE Name = @Name)
            THROW 50031, 'A role with this name already exists.', 1;
        INSERT auth.Roles (Name, DisplayName, Description) VALUES (@Name, @DisplayName, @Description);
        SET @RoleId = CAST(SCOPE_IDENTITY() AS int);
    END

    DECLARE @details nvarchar(max) = (SELECT role = @Name FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC auth.usp_Audit_Insert NULL, @ChangedBy, 'RoleChanged', NULL, NULL, @details;
    COMMIT TRANSACTION;

    SELECT RoleId = @RoleId;
END
GO

/* Deletes a role (its user and menu assignments go with it). The Admin role cannot be deleted. */
CREATE OR ALTER PROCEDURE auth.usp_Role_Delete
    @Name       nvarchar(64),
    @ChangedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Name = N'Admin'
        THROW 50032, 'The Admin role cannot be deleted.', 1;

    BEGIN TRANSACTION;
    DELETE auth.Roles WHERE Name = @Name;
    DECLARE @details nvarchar(max) = (SELECT role = @Name, deleted = CAST(1 AS bit) FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC auth.usp_Audit_Insert NULL, @ChangedBy, 'RoleChanged', NULL, NULL, @details;
    COMMIT TRANSACTION;
END
GO

/* ================================ Users ================================ */

CREATE OR ALTER PROCEDURE auth.usp_User_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.UserId, u.UserName, u.DisplayName, u.Email, u.IsActive, u.UserGroup, u.Location, u.LastLoginUtc,
           u.LockoutEndUtc,
           Roles = (SELECT STRING_AGG(r.Name, N',') WITHIN GROUP (ORDER BY r.Name)
                    FROM auth.UserRoles ur JOIN auth.Roles r ON r.RoleId = ur.RoleId
                    WHERE ur.UserId = u.UserId)
    FROM auth.Users u
    ORDER BY u.UserName;
END
GO

/*
    Replaces a user's roles and TMS attributes (user group / location) and audits the change.
    Takes effect at the user's next token refresh (within 15 minutes) or sign-in.
*/
CREATE OR ALTER PROCEDURE auth.usp_User_SetAccess
    @UserId     int,
    @RoleNames  nvarchar(1000),
    @UserGroup  nvarchar(16)    = NULL,
    @Location   nvarchar(32)    = NULL,
    @ChangedBy  nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE auth.Users
    SET UserGroup = NULLIF(TRIM(@UserGroup), N''), Location = NULLIF(TRIM(@Location), N'')
    WHERE UserId = @UserId;
    IF @@ROWCOUNT = 0 THROW 50040, 'User not found.', 1;

    DELETE auth.UserRoles WHERE UserId = @UserId;
    INSERT auth.UserRoles (UserId, RoleId)
    SELECT @UserId, r.RoleId
    FROM auth.Roles r
    WHERE r.Name IN (SELECT TRIM(value) FROM STRING_SPLIT(@RoleNames, N',') WHERE TRIM(value) <> N'');

    DECLARE @details nvarchar(max) =
        (SELECT roles = @RoleNames, userGroup = @UserGroup, location = @Location, changedBy = @ChangedBy
         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC auth.usp_Audit_Insert @UserId, NULL, 'AccessChanged', NULL, NULL, @details;

    COMMIT TRANSACTION;
END
GO

/*
    Creates a user from Settings > User roles and stores a one-time "set your password" token (the API emails the link).
    @PasswordHash is a hash of a random secret nobody knows, so the account cannot be used until the link is.
    Unknown role names are refused (50043) rather than silently dropped.
*/
CREATE OR ALTER PROCEDURE auth.usp_User_Invite
    @UserName       nvarchar(64),
    @Email          nvarchar(256),
    @DisplayName    nvarchar(128),
    @PasswordHash   nvarchar(256),
    @RoleNames      nvarchar(1000),
    @UserGroup      nvarchar(16)    = NULL,
    @Location       nvarchar(32)    = NULL,
    @TokenHash      binary(32),
    @ExpiresUtc     datetime2(3),
    @CreatedBy      nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM auth.Users WHERE UserName = @UserName)
        THROW 50041, 'A user with this user name already exists.', 1;
    IF EXISTS (SELECT 1 FROM auth.Users WHERE Email = @Email)
        THROW 50042, 'A user with this email address already exists.', 1;

    DECLARE @roles TABLE (Name nvarchar(64) COLLATE Latin1_General_100_CI_AS PRIMARY KEY);
    INSERT @roles (Name)
    SELECT DISTINCT TRIM(value) FROM STRING_SPLIT(@RoleNames, N',') WHERE TRIM(value) <> N'';
    IF EXISTS (SELECT 1 FROM @roles x WHERE NOT EXISTS (SELECT 1 FROM auth.Roles r WHERE r.Name = x.Name))
        THROW 50043, 'One or more roles do not exist. Reload the page and try again.', 1;

    BEGIN TRANSACTION;

    INSERT auth.Users (UserName, Email, DisplayName, PasswordHash, MustChangePassword, UserGroup, Location)
    VALUES (@UserName, @Email, @DisplayName, @PasswordHash, 0, NULLIF(TRIM(@UserGroup), N''), NULLIF(TRIM(@Location), N''));

    DECLARE @userId int = CAST(SCOPE_IDENTITY() AS int);

    INSERT auth.UserRoles (UserId, RoleId)
    SELECT @userId, r.RoleId FROM auth.Roles r JOIN @roles x ON x.Name = r.Name;

    INSERT auth.PasswordResetTokens (UserId, TokenHash, ExpiresUtc, RequestedIp)
    VALUES (@userId, @TokenHash, @ExpiresUtc, NULL);

    DECLARE @details nvarchar(max) =
        (SELECT roles = @RoleNames, userGroup = @UserGroup, location = @Location, createdBy = @CreatedBy
         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC auth.usp_Audit_Insert @userId, @UserName, 'UserCreated', NULL, NULL, @details;

    COMMIT TRANSACTION;

    SELECT UserId = @userId;
END
GO

/*
    Admin-requested "set your password" link (new user whose link expired, forgotten password, locked account).
    Only the newest link works. Returns the user to email; THROW 50040 when the user does not exist or is inactive.
*/
CREATE OR ALTER PROCEDURE auth.usp_User_CreatePasswordLink
    @UserId         int,
    @TokenHash      binary(32),
    @ExpiresUtc     datetime2(3),
    @RequestedBy    nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @userName nvarchar(64), @email nvarchar(256), @displayName nvarchar(128);
    SELECT @userName = UserName, @email = Email, @displayName = DisplayName
    FROM auth.Users
    WHERE UserId = @UserId AND IsActive = 1;
    IF @userName IS NULL THROW 50040, 'User not found or inactive.', 1;

    BEGIN TRANSACTION;
    UPDATE auth.PasswordResetTokens SET UsedUtc = SYSUTCDATETIME() WHERE UserId = @UserId AND UsedUtc IS NULL;
    INSERT auth.PasswordResetTokens (UserId, TokenHash, ExpiresUtc, RequestedIp)
    VALUES (@UserId, @TokenHash, @ExpiresUtc, NULL);

    DECLARE @details nvarchar(max) = (SELECT requestedBy = @RequestedBy FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC auth.usp_Audit_Insert @UserId, @userName, 'PasswordResetRequested', NULL, NULL, @details;
    COMMIT TRANSACTION;

    SELECT UserId = @UserId, UserName = @userName, Email = @email, DisplayName = @displayName;
END
GO
