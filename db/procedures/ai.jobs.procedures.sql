/*
    LT ODM - AI jobs (ai.Job): slow AI work run in the background by the API (AiJobRunner), followed by the AI task tray
    and listed for Admins on Settings > AI connections (/api/v1/ai/jobs, /api/v1/admin/ai/jobs).

    usp_Job_Add          a new job (Queued); returns JobId
    usp_Job_Start        Queued -> Running (nothing when it was cancelled meanwhile); returns 1 when it may run
    usp_Job_Finish       Running -> Done | Failed | Cancelled, with the result or the error
    usp_Job_Get          one job with its input and result
    usp_Job_Mine         the owner's jobs for the tray: running, or finished and not yet seen (last 2 days)
    usp_Job_List         Admin list (status filter), newest first, without input and result
    usp_Job_Seen         the owner's page showed the answer
    usp_Job_Cancel       Queued / Running -> Cancelled (who cancelled); returns 1 when it changed
    usp_Job_Interrupt    at API start: Queued / Running -> Interrupted (nothing is re-run)
    usp_Job_Purge        deletes finished jobs older than ai.Policy.JobKeepDays
    usp_Job_KeepDays_Save  Admin: days to keep finished jobs (1-365)

    Requires tables\ai.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\ai.jobs.procedures.sql
*/
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Add
    @Kind      varchar(32),
    @Lane      varchar(8),
    @OwnerName nvarchar(64),
    @OwnerId   nvarchar(64),
    @TaskKey   nvarchar(150) = NULL,
    @Label     nvarchar(150) = NULL,
    @Url       nvarchar(400) = NULL,
    @Input     nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT ai.Job (Kind, Lane, OwnerName, OwnerId, TaskKey, Label, Url, Input)
    VALUES (@Kind, @Lane, @OwnerName, @OwnerId, @TaskKey, @Label, @Url, @Input);
    SELECT JobId = CAST(SCOPE_IDENTITY() AS int);
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Start
    @JobId int
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE ai.Job SET Status = 'Running', StartedUtc = SYSUTCDATETIME() WHERE JobId = @JobId AND Status = 'Queued';
    SELECT Started = CAST(@@ROWCOUNT AS bit);
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Finish
    @JobId       int,
    @Status      varchar(16),
    @Result      nvarchar(max) = NULL,
    @Error       nvarchar(1000) = NULL,
    @ErrorStatus smallint = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Status NOT IN ('Done', 'Failed', 'Cancelled') THROW 50400, N'A job finishes as Done, Failed or Cancelled.', 1;
    -- A job an Admin cancelled stays Cancelled, whatever the work answered after.
    UPDATE ai.Job
    SET Status = IIF(Status = 'Cancelled', Status, @Status), Result = IIF(Status = 'Cancelled', NULL, @Result),
        Error = IIF(Status = 'Cancelled', Error, @Error), ErrorStatus = IIF(Status = 'Cancelled', ErrorStatus, @ErrorStatus),
        FinishedUtc = SYSUTCDATETIME()
    WHERE JobId = @JobId AND Status IN ('Queued', 'Running', 'Cancelled') AND FinishedUtc IS NULL;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Get
    @JobId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT JobId, Kind, Lane, Status, OwnerName, OwnerId, TaskKey, Label, Url, Input, Result, Error, ErrorStatus, Seen,
           CreatedUtc, StartedUtc, FinishedUtc, CancelledBy
    FROM ai.Job WHERE JobId = @JobId;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Mine
    @OwnerId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT JobId, Kind, Lane, Status, OwnerName, OwnerId, TaskKey, Label, Url, Input = CAST(NULL AS nvarchar(max)), Result, Error, ErrorStatus, Seen,
           CreatedUtc, StartedUtc, FinishedUtc, CancelledBy
    FROM ai.Job
    WHERE OwnerId = @OwnerId AND CreatedUtc >= DATEADD(day, -2, SYSUTCDATETIME()) AND TaskKey IS NOT NULL
      AND (Status IN ('Queued', 'Running') OR Seen = 0 AND Status <> 'Cancelled')
    ORDER BY JobId DESC;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_List
    @Status varchar(16) = NULL,   -- NULL = all; 'Active' = Queued or Running
    @Skip   int = 0,
    @Take   int = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT JobId, Kind, Lane, Status, OwnerName, OwnerId, TaskKey, Label, Url, Input = CAST(NULL AS nvarchar(max)), Result = CAST(NULL AS nvarchar(max)),
           Error, ErrorStatus, Seen, CreatedUtc, StartedUtc, FinishedUtc, CancelledBy, TotalCount = COUNT(*) OVER ()
    FROM ai.Job
    WHERE @Status IS NULL OR Status = @Status OR @Status = 'Active' AND Status IN ('Queued', 'Running')
    ORDER BY JobId DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Seen
    @JobId   int,
    @OwnerId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE ai.Job SET Seen = 1 WHERE JobId = @JobId AND OwnerId = @OwnerId AND Seen = 0;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Cancel
    @JobId       int,
    @CancelledBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE ai.Job
    SET Status = 'Cancelled', CancelledBy = @CancelledBy, FinishedUtc = IIF(Status = 'Queued', SYSUTCDATETIME(), FinishedUtc)
    WHERE JobId = @JobId AND Status IN ('Queued', 'Running');
    SELECT Cancelled = CAST(@@ROWCOUNT AS bit);
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Interrupt
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE ai.Job
    SET Status = 'Interrupted', FinishedUtc = SYSUTCDATETIME(),
        Error = N'The server restarted before the job finished. Run it again.'
    WHERE Status IN ('Queued', 'Running') OR Status = 'Cancelled' AND FinishedUtc IS NULL;
    SELECT Interrupted = @@ROWCOUNT;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_Purge
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @days int = ISNULL((SELECT JobKeepDays FROM ai.Policy WHERE PolicyId = 1), 7);
    DELETE ai.Job WHERE FinishedUtc < DATEADD(day, -@days, SYSUTCDATETIME());
    SELECT Deleted = @@ROWCOUNT, KeepDays = @days;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_KeepDays_Save
    @Days      smallint,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Days NOT BETWEEN 1 AND 365 THROW 50400, N'Keep finished jobs for 1 to 365 days.', 1;
    UPDATE ai.Policy SET JobKeepDays = @Days WHERE PolicyId = 1;   -- not the switches' "last changed": that is for AI on/off and cloud
    IF @@ROWCOUNT = 0 THROW 50404, N'The AI switches are not set up yet (run tables\ai.tables.sql).', 1;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Job_KeepDays_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT KeepDays = ISNULL((SELECT JobKeepDays FROM ai.Policy WHERE PolicyId = 1), 7);
END
GO

PRINT 'AI jobs ready.';
GO
