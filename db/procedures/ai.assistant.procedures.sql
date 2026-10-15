/*
    LT ODM - Library assistant saved questions (ai.SavedQuestion; AI Studio > Library assistant, /api/v1/ai/library-assistant).

    usp_SavedQuestion_UseRule   a Rule for this question key: its tool and arguments (counts the hit), or nothing
    usp_SavedQuestion_Record    after the AI answered: insert, or update the tool / arguments and count (a Rule is only counted)
    usp_SavedQuestion_Feedback  thumbs up = Rule with the arguments shown; thumbs down = Removed
    usp_SavedQuestion_List      Admin list (status, search), most used first
    usp_SavedQuestion_Popular   the most used Rules (suggested questions)
    usp_SavedQuestion_Save      Admin: status and corrected arguments (RowVer-free: last save wins)
    usp_SavedQuestion_Delete    Admin

    Arguments are checked by the API before they are stored. Rule errors are THROWn as 50400 / 50404.
    Requires tables\ai.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\ai.assistant.procedures.sql
*/
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_UseRule
    @QuestionKey nvarchar(300)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE ai.SavedQuestion
    SET RuleHits = RuleHits + 1, LastAskedUtc = SYSUTCDATETIME()
    OUTPUT inserted.SavedQuestionId, inserted.Question, inserted.Tool, inserted.Arguments
    WHERE QuestionKey = @QuestionKey AND Status = 'Rule';
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_Record
    @QuestionKey nvarchar(300),
    @Question    nvarchar(500),
    @Tool        varchar(40),
    @Arguments   nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    DECLARE @id int = (SELECT SavedQuestionId FROM ai.SavedQuestion WITH (UPDLOCK, HOLDLOCK) WHERE QuestionKey = @QuestionKey);
    IF @id IS NULL
    BEGIN
        INSERT ai.SavedQuestion (QuestionKey, Question, Tool, Arguments) VALUES (@QuestionKey, @Question, @Tool, @Arguments);
        SET @id = SCOPE_IDENTITY();
    END
    ELSE
        -- The AI's latest choice replaces an unconfirmed one; a Rule keeps what was confirmed.
        UPDATE ai.SavedQuestion
        SET AskCount = AskCount + 1, LastAskedUtc = SYSUTCDATETIME(), Question = @Question,
            Tool = IIF(Status = 'Rule', Tool, @Tool), Arguments = IIF(Status = 'Rule', Arguments, @Arguments)
        WHERE SavedQuestionId = @id;
    COMMIT;
    SELECT SavedQuestionId = @id;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_Feedback
    @SavedQuestionId int,
    @Helpful         bit,
    @Tool            varchar(40),
    @Arguments       nvarchar(max),
    @ChangedBy       nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE ai.SavedQuestion
    SET Status = IIF(@Helpful = 1, 'Rule', 'Removed'),
        Tool = IIF(@Helpful = 1, @Tool, Tool), Arguments = IIF(@Helpful = 1, @Arguments, Arguments),
        ChangedBy = @ChangedBy, ChangedUtc = SYSUTCDATETIME(), ChangedHow = 'Feedback'
    WHERE SavedQuestionId = @SavedQuestionId;
    IF @@ROWCOUNT = 0 THROW 50404, N'The saved question was not found. It may have been deleted.', 1;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_List
    @Status nvarchar(16)  = NULL,   -- Recorded | Rule | Removed; NULL = all
    @Search nvarchar(100) = NULL,
    @Skip   int = 0,
    @Take   int = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @like nvarchar(110) = N'%' + REPLACE(REPLACE(REPLACE(@Search, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%';
    SELECT SavedQuestionId, Question, Tool, Arguments, Status, AskCount, RuleHits, LastAskedUtc, CreatedUtc, ChangedBy, ChangedUtc, ChangedHow,
           TotalCount = COUNT(*) OVER ()
    FROM ai.SavedQuestion
    WHERE (@Status IS NULL OR Status = @Status)
      AND (@Search IS NULL OR Question LIKE @like OR QuestionKey LIKE @like)
    ORDER BY AskCount + RuleHits DESC, LastAskedUtc DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_Popular
    @Take int = 6
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Take) Question FROM ai.SavedQuestion WHERE Status = 'Rule' ORDER BY AskCount + RuleHits DESC, LastAskedUtc DESC;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_Save
    @SavedQuestionId int,
    @Status          varchar(16),
    @Arguments       nvarchar(max),
    @ChangedBy       nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Status NOT IN ('Recorded', 'Rule', 'Removed') THROW 50400, N'Status must be Recorded, Rule or Removed.', 1;
    UPDATE ai.SavedQuestion
    SET Status = @Status, Arguments = @Arguments, ChangedBy = @ChangedBy, ChangedUtc = SYSUTCDATETIME(), ChangedHow = 'Admin'
    WHERE SavedQuestionId = @SavedQuestionId;
    IF @@ROWCOUNT = 0 THROW 50404, N'The saved question was not found. It may have been deleted.', 1;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_Delete
    @SavedQuestionId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE ai.SavedQuestion WHERE SavedQuestionId = @SavedQuestionId;
    IF @@ROWCOUNT = 0 THROW 50404, N'The saved question was not found. It may have been deleted.', 1;
END
GO

PRINT 'Library assistant saved questions ready.';
GO
