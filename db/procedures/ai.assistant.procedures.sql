/*
    LT ODM - Library assistant saved questions (ai.SavedQuestion; AI Studio > Library assistant, /api/v1/ai/library-assistant).

    usp_SavedQuestion_UseRule   a Rule for this question key, else a pattern Rule (IsPattern) for one of the pattern keys:
                                its tool and arguments (counts the hit), or nothing
    usp_SavedQuestion_Record    after the AI answered: insert, or update the tool / arguments and count (a Rule is only counted)
    usp_SavedQuestion_Feedback  thumbs up = Rule with the arguments shown; thumbs down = Removed
    usp_SavedQuestion_List      Admin list (status, search), most used first
    usp_SavedQuestion_Popular   the most used Rules (suggested questions)
    usp_SavedQuestion_Save      Admin: status and corrected arguments (RowVer-free: last save wins)
    usp_SavedQuestion_Delete    Admin
    usp_SavedQuestion_RecordUnanswered  a question nobody could answer: Unanswered, for Admins to review
    usp_SavedQuestion_Get       one saved question
    usp_Synonym_List / _Save / _Delete  synonyms (ai.Synonym), read before every question
    usp_Guide_List              how-to guides (ai.Guide) and the questions that open them (pattern Rules, tool how_to)
    usp_Guide_Save              Admin: add or change a guide and replace its questions
    usp_Guide_Delete            Admin: a guide and its questions

    Arguments are checked by the API before they are stored. Rule errors are THROWn as 50400 / 50404.
    Requires tables\ai.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\ai.assistant.procedures.sql
*/
SET NOCOUNT ON;
GO

/*
    @PatternKey: the question with its filter words taken out; @PatternKey2: the same with product type words kept
    ("top suppliers"). The question's own key wins over a pattern.
*/
CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_UseRule
    @QuestionKey nvarchar(300),
    @PatternKey  nvarchar(300) = NULL,
    @PatternKey2 nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @id int = (SELECT SavedQuestionId FROM ai.SavedQuestion WHERE QuestionKey = @QuestionKey AND Status = 'Rule');
    IF @id IS NULL
        SET @id = (SELECT TOP (1) SavedQuestionId FROM ai.SavedQuestion
                   WHERE QuestionKey IN (@PatternKey, @PatternKey2) AND Status = 'Rule' AND IsPattern = 1
                   ORDER BY IIF(QuestionKey = @PatternKey, 0, 1));
    IF @id IS NULL RETURN;
    UPDATE ai.SavedQuestion
    SET RuleHits = RuleHits + 1, LastAskedUtc = SYSUTCDATETIME()
    OUTPUT inserted.SavedQuestionId, inserted.Question, inserted.Tool, inserted.Arguments, inserted.IsPattern, inserted.QuestionKey
    WHERE SavedQuestionId = @id;
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
        -- The AI's latest choice replaces an unconfirmed one; a Rule keeps what was confirmed. An unanswered question
        -- the AI now answers becomes Recorded.
        UPDATE ai.SavedQuestion
        SET AskCount = AskCount + 1, LastAskedUtc = SYSUTCDATETIME(), Question = @Question,
            Tool = IIF(Status = 'Rule', Tool, @Tool), Arguments = IIF(Status = 'Rule', Arguments, @Arguments),
            Status = IIF(Status = 'Unanswered', 'Recorded', Status)
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
    -- A pattern keeps its own arguments: the ones shown hold this question's filters (customer, season).
    UPDATE ai.SavedQuestion
    SET Status = IIF(@Helpful = 1, 'Rule', 'Removed'),
        Tool = IIF(@Helpful = 1 AND IsPattern = 0, @Tool, Tool), Arguments = IIF(@Helpful = 1 AND IsPattern = 0, @Arguments, Arguments),
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
           IsPattern, TotalCount = COUNT(*) OVER ()
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
    -- Seeded patterns only once someone asked them, and never one written with a slot ("{styleNo}").
    SELECT TOP (@Take) Question FROM ai.SavedQuestion
    WHERE Status = 'Rule' AND (IsPattern = 0 OR RuleHits > 0 AND Question NOT LIKE N'%{%')
    ORDER BY AskCount + RuleHits DESC, LastAskedUtc DESC;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_Save
    @SavedQuestionId int,
    @Status          varchar(16),
    @Arguments       nvarchar(max),
    @ChangedBy       nvarchar(64),
    @Tool            varchar(40) = NULL   -- another look-up (checked by the API); NULL = keep
AS
BEGIN
    SET NOCOUNT ON;
    IF @Status NOT IN ('Recorded', 'Rule', 'Removed') THROW 50400, N'Status must be Recorded, Rule or Removed.', 1;
    UPDATE ai.SavedQuestion
    SET Status = @Status, Arguments = @Arguments, Tool = COALESCE(@Tool, Tool),
        ChangedBy = @ChangedBy, ChangedUtc = SYSUTCDATETIME(), ChangedHow = 'Admin'
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

/*
    A question nobody could answer: added as Unanswered (Tool 'none'), or counted again. A row that already exists keeps
    its status (an Admin's Ignore = Removed stays; a Rule or recorded answer is not overwritten).
*/
CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_RecordUnanswered
    @QuestionKey nvarchar(300),
    @Question    nvarchar(500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    UPDATE ai.SavedQuestion WITH (UPDLOCK, HOLDLOCK)
    SET AskCount = AskCount + 1, LastAskedUtc = SYSUTCDATETIME(), Question = IIF(Status = 'Unanswered', @Question, Question)
    WHERE QuestionKey = @QuestionKey;
    IF @@ROWCOUNT = 0
        INSERT ai.SavedQuestion (QuestionKey, Question, Tool, Arguments, Status) VALUES (@QuestionKey, @Question, 'none', N'{}', 'Unanswered');
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_SavedQuestion_Get
    @SavedQuestionId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SavedQuestionId, Question, Tool, Arguments, Status, AskCount, RuleHits, LastAskedUtc, CreatedUtc, ChangedBy, ChangedUtc, ChangedHow,
           IsPattern, QuestionKey
    FROM ai.SavedQuestion WHERE SavedQuestionId = @SavedQuestionId;
END
GO

/* Synonyms (ai.Synonym): checked by the API (word form, value on its kind's list). */
CREATE OR ALTER PROCEDURE ai.usp_Synonym_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SynonymId, Word, Kind, Value, ChangedBy, ChangedUtc FROM ai.Synonym ORDER BY Word;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Synonym_Save
    @Word      nvarchar(60),
    @Kind      varchar(16),
    @Value     nvarchar(100),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    UPDATE ai.Synonym WITH (UPDLOCK, HOLDLOCK)
    SET Kind = @Kind, Value = @Value, ChangedBy = @ChangedBy, ChangedUtc = SYSUTCDATETIME()
    WHERE Word = @Word;
    IF @@ROWCOUNT = 0
        INSERT ai.Synonym (Word, Kind, Value, ChangedBy) VALUES (@Word, @Kind, @Value, @ChangedBy);
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE ai.usp_Synonym_Delete
    @SynonymId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE ai.Synonym WHERE SynonymId = @SynonymId;
    IF @@ROWCOUNT = 0 THROW 50404, N'The synonym was not found. It may have been deleted.', 1;
END
GO

/*
    How-to guides (ai.Guide). A guide's questions are pattern Rules in ai.SavedQuestion with Tool 'how_to' and
    Arguments {"guide":"<Code>"}; their keys are computed by the API (QuestionPatterns).
*/
CREATE OR ALTER PROCEDURE ai.usp_Guide_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Code, Title, Who, Intro, Steps, IsActive, ChangedBy, ChangedUtc, ChangedHow FROM ai.Guide ORDER BY Title;
    SELECT Code = JSON_VALUE(Arguments, '$.guide'), Question
    FROM ai.SavedQuestion
    WHERE Tool = 'how_to' AND IsPattern = 1 AND Status = 'Rule' AND ISJSON(Arguments) = 1
    ORDER BY SavedQuestionId;
END
GO

/*
    Adds or changes a guide and replaces its questions (@Questions: JSON array of {"questionKey": ..., "question": ...}).
    A wording another look-up answers as a Rule is refused (50400); one that was only recorded, removed, or another
    guide's is taken over.
*/
CREATE OR ALTER PROCEDURE ai.usp_Guide_Save
    @Code      varchar(40),
    @Title     nvarchar(200),
    @Who       nvarchar(200),
    @Intro     nvarchar(1000),
    @Steps     nvarchar(max),
    @IsActive  bit,
    @Questions nvarchar(max),
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Code NOT LIKE '[a-z]%' OR @Code LIKE '%[^a-z0-9_]%' THROW 50400, N'The code is lower-case letters, digits or _.', 1;
    IF ISJSON(@Steps) = 0 OR ISJSON(@Questions) = 0 THROW 50400, N'Steps and questions must be JSON.', 1;

    DECLARE @q TABLE (QuestionKey nvarchar(300) NOT NULL PRIMARY KEY, Question nvarchar(500) NOT NULL);
    INSERT @q (QuestionKey, Question)
    SELECT QuestionKey, MIN(Question) FROM OPENJSON(@Questions) WITH (QuestionKey nvarchar(300) '$.questionKey', Question nvarchar(500) '$.question')
    WHERE QuestionKey IS NOT NULL AND Question IS NOT NULL GROUP BY QuestionKey;

    DECLARE @args nvarchar(100) = N'{"guide":"' + @Code + N'"}';
    -- Checked before the transaction, so a refusal leaves nothing open for the caller.
    DECLARE @clash nvarchar(600) = (
        SELECT TOP (1) N'"' + q.Question + N'" already answers another look-up (' + s.Tool + N'). Change that saved question first, or word this one differently.'
        FROM @q q JOIN ai.SavedQuestion s ON s.QuestionKey = q.QuestionKey
        WHERE s.Status = 'Rule' AND s.Tool <> 'how_to');
    IF @clash IS NOT NULL THROW 50400, @clash, 1;

    BEGIN TRAN;
    UPDATE ai.Guide
    SET Title = @Title, Who = @Who, Intro = @Intro, Steps = @Steps, IsActive = @IsActive,
        ChangedBy = @ChangedBy, ChangedUtc = SYSUTCDATETIME(), ChangedHow = 'Admin'
    WHERE Code = @Code;
    IF @@ROWCOUNT = 0
        INSERT ai.Guide (Code, Title, Who, Intro, Steps, IsActive, ChangedBy, ChangedHow)
        VALUES (@Code, @Title, @Who, @Intro, @Steps, @IsActive, @ChangedBy, 'Admin');

    DELETE ai.SavedQuestion
    WHERE Tool = 'how_to' AND IsPattern = 1 AND ISJSON(Arguments) = 1 AND JSON_VALUE(Arguments, '$.guide') = @Code
      AND QuestionKey NOT IN (SELECT QuestionKey FROM @q);

    UPDATE s
    SET Question = q.Question, Tool = 'how_to', Arguments = @args, Status = 'Rule', IsPattern = 1,
        ChangedBy = @ChangedBy, ChangedUtc = SYSUTCDATETIME(), ChangedHow = 'Admin'
    FROM ai.SavedQuestion s JOIN @q q ON q.QuestionKey = s.QuestionKey;

    INSERT ai.SavedQuestion (QuestionKey, Question, Tool, Arguments, Status, AskCount, ChangedBy, ChangedUtc, ChangedHow, IsPattern)
    SELECT q.QuestionKey, q.Question, 'how_to', @args, 'Rule', 0, @ChangedBy, SYSUTCDATETIME(), 'Admin', 1
    FROM @q q
    WHERE NOT EXISTS (SELECT 1 FROM ai.SavedQuestion s WHERE s.QuestionKey = q.QuestionKey);
    COMMIT;
END
GO

/* The guide and its questions (answers the AI recorded for it stay, and go back to the AI when asked). */
CREATE OR ALTER PROCEDURE ai.usp_Guide_Delete
    @Code varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    DELETE ai.SavedQuestion
    WHERE Tool = 'how_to' AND IsPattern = 1 AND ISJSON(Arguments) = 1 AND JSON_VALUE(Arguments, '$.guide') = @Code;
    DELETE ai.Guide WHERE Code = @Code;
    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK;
        THROW 50404, N'The guide was not found. It may have been deleted.', 1;
    END
    COMMIT;
END
GO

PRINT 'Library assistant saved questions and how-to guides ready.';
GO
