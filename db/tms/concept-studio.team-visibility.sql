/*
    Concept Studio: who sees and who may change a saved concept.

    Replaces the TMS rule (each user saw only their own concepts, and anyone could change any concept by recid):
      - Office users see their team's concepts: same user group + location as the caller (plus their own).
      - Admins and factory users see every active concept (factories submit SBU offers against them).
      - Only the creator or an Admin may edit or delete a concept; an edit keeps the concept in its creator's team.

    The caller (@username, @location, @user_group, @is_admin, @modified_by) is always filled by the API from the
    sign-in token (tms-procedures.json); the screen only chooses @scope.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i db\tms\concept-studio.team-visibility.sql
*/

/*
    @scope: 'mine' = own concepts; 'team' = same user group + location; 'all' = every concept (Admins and factory
    users only - anyone else gets 'team'). NULL = 'all' for Admins and factory users, otherwise 'team'.
    can_edit = 1 when the caller may edit or delete the concept.
*/
CREATE OR ALTER PROCEDURE dbo.web_rd_get_concept_studio_results
    @username   nvarchar(100),
    @location   nvarchar(50)  = N'',
    @user_group nvarchar(100) = N'',
    @is_admin   bit           = 0,
    @scope      varchar(10)   = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @seesAll bit = IIF(@is_admin = 1 OR UPPER(TRIM(ISNULL(@user_group, N''))) IN (N'FTY', N'FACTORY'), 1, 0);
    SET @scope = LOWER(TRIM(ISNULL(@scope, '')));
    IF @scope NOT IN ('mine', 'team', 'all') OR (@scope = 'all' AND @seesAll = 0)
        SET @scope = IIF(@scope = '' AND @seesAll = 1, 'all', 'team');

    SELECT
        recid,
        concept_name,
        customer,
        customer_id,
        season,
        season_id,
        target_market,
        fob_price,
        active_tags,
        concept_brief,
        suggested_products,
        fabric_direction,
        sustainability_notes,
        inspiration_images,
        created_by                                AS createdBy,
        location,
        user_group,
        modified_by,
        status,
        CONVERT(nvarchar(30), created_date,  120) AS created_date,
        CONVERT(nvarchar(30), modified_date, 120) AS modified_date,
        CAST(IIF(@is_admin = 1 OR created_by = @username, 1, 0) AS bit) AS can_edit
    FROM dbo.tms_concept_studio_results
    WHERE status = 'Active'
      AND (   @scope = 'all'
           OR created_by = @username
           OR (@scope = 'team'
               AND ISNULL(user_group, N'') = ISNULL(@user_group, N'')
               AND ISNULL(location,   N'') = ISNULL(@location,   N'')))
    ORDER BY created_date DESC;
END
GO

/* Only the creator or an Admin; location and user group stay the creator's (filled only when missing). */
CREATE OR ALTER PROCEDURE dbo.web_rd_upd_concept_studio_result
    @recid                int,
    @concept_name         nvarchar(255),
    @customer             nvarchar(255) = NULL,
    @customer_id          nvarchar(50)  = NULL,
    @season               nvarchar(255) = NULL,
    @season_id            nvarchar(50)  = NULL,
    @target_market        nvarchar(255) = NULL,
    @fob_price            nvarchar(50)  = NULL,
    @active_tags          nvarchar(max) = NULL,
    @concept_brief        nvarchar(max) = NULL,
    @suggested_products   nvarchar(max) = NULL,
    @fabric_direction     nvarchar(max) = NULL,
    @sustainability_notes nvarchar(max) = NULL,
    @inspiration_images   nvarchar(max) = NULL,
    @username             nvarchar(100) = NULL,
    @location             nvarchar(50)  = NULL,
    @user_group           nvarchar(100) = NULL,
    @modified_by          nvarchar(100) = NULL,
    @is_admin             bit           = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tms_concept_studio_results
                   WHERE recid = @recid AND status = 'Active' AND (@is_admin = 1 OR created_by = @username))
    BEGIN
        SELECT 0 AS rows_affected, 'Only the person who created this concept or an Admin can change it.' AS result_status;
        RETURN;
    END

    BEGIN TRY
        UPDATE dbo.tms_concept_studio_results
        SET concept_name         = @concept_name,
            customer             = @customer,
            customer_id          = @customer_id,
            season               = @season,
            season_id            = @season_id,
            target_market        = @target_market,
            fob_price            = @fob_price,
            active_tags          = @active_tags,
            concept_brief        = @concept_brief,
            suggested_products   = @suggested_products,
            fabric_direction     = @fabric_direction,
            sustainability_notes = @sustainability_notes,
            inspiration_images   = @inspiration_images,
            location             = COALESCE(NULLIF(location, N''),   NULLIF(@location, N''),   location),
            user_group           = COALESCE(NULLIF(user_group, N''), NULLIF(@user_group, N''), user_group),
            modified_by          = COALESCE(@modified_by, @username, modified_by),
            modified_date        = GETDATE()
        WHERE recid = @recid;

        SELECT @@ROWCOUNT AS rows_affected, 'Success' AS result_status;
    END TRY
    BEGIN CATCH
        SELECT 0 AS rows_affected, ERROR_MESSAGE() AS result_status;
    END CATCH
END
GO

/* Soft delete; only the creator or an Admin (@modified_by is the signed-in user). */
CREATE OR ALTER PROCEDURE dbo.web_rd_del_concept_studio_result
    @recid       int,
    @modified_by nvarchar(100) = NULL,
    @is_admin    bit           = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tms_concept_studio_results
                   WHERE recid = @recid AND status = 'Active' AND (@is_admin = 1 OR created_by = @modified_by))
    BEGIN
        SELECT 0 AS rows_affected, 'Only the person who created this concept or an Admin can delete it.' AS result_status;
        RETURN;
    END

    BEGIN TRY
        UPDATE dbo.tms_concept_studio_results
        SET status        = 'Deleted',
            modified_by   = COALESCE(@modified_by, modified_by),
            modified_date = GETDATE()
        WHERE recid = @recid;

        SELECT @@ROWCOUNT AS rows_affected, 'Success' AS result_status;
    END TRY
    BEGIN CATCH
        SELECT 0 AS rows_affected, ERROR_MESSAGE() AS result_status;
    END CATCH
END
GO
