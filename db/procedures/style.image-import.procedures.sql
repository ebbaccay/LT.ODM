/*
    LT ODM style picture import (Settings > Import pictures). The API reads the style number from each file name, cuts
    the garment out of sheets that hold other things, sharpens small pictures and stores the file; these procedures find
    the styles a file belongs to and set its address on them.

    usp_StyleImageImport_Match  styles per file key (the file name before its first "_", e.g. RC2606OW001 from
                                RC2606OW001_Img01.jpg), with how they match:
                                  Exact    the style number is the key                            RC2606OW001
                                  Suffix   the key plus a suffix after "_" or "-"                 RC2606OW001_H, RC2617OW006-H
                                  Variant  the key plus a letter version (its own style)          RC2701OW001A_H for RC2701OW001
                                  Root     the key without its letter version, when the key has one  RC2702OM001 for RC2702OM001A
                                Exact and Suffix are what the screen ticks; Variant and Root are offered, not ticked.
    usp_Style_SetPicture        sets the picture (Image) or sketch (Sketch) of the styles given; styles that already
                                have one are skipped unless @Overwrite = 1.
    usp_StyleImageImport_Existing    styles with a stored picture or sketch ("Check existing pictures": the API tells
                                     sketches from photos and suggests moves)
    usp_StyleImageImport_MoveExisting  swaps picture and sketch of the styles given, only where both still hold the
                                     addresses the check saw (a style changed since then is skipped)

    Requires tables\style.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.image-import.procedures.sql
*/
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE style.usp_StyleImageImport_Match
    @Keys nvarchar(max)   -- JSON array of file keys
AS
BEGIN
    SET NOCOUNT ON;

    WITH k AS (
        SELECT DISTINCT FileKey = CAST(TRIM(value) AS nvarchar(100))
        FROM OPENJSON(@Keys)
        WHERE LEN(TRIM(value)) BETWEEN 1 AND 100
    ),
    keys AS (
        -- LIKE patterns with the key's own wildcards escaped; Root only when the key ends in 1-3 letters after a digit.
        SELECT FileKey,
               Pattern = REPLACE(REPLACE(REPLACE(FileKey, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]'),
               RootKey = IIF(t.Letters BETWEEN 1 AND 3 AND LEN(FileKey) > t.Letters
                             AND SUBSTRING(FileKey, LEN(FileKey) - t.Letters, 1) LIKE N'[0-9]',
                             LEFT(FileKey, LEN(FileKey) - t.Letters), NULL)
        FROM k
        CROSS APPLY (SELECT Letters = IIF(FileKey LIKE N'%[^A-Za-z]%', PATINDEX(N'%[^A-Za-z]%', REVERSE(FileKey)) - 1, LEN(FileKey))) t
    ),
    hits AS (
        SELECT k.FileKey, s.StyleId,
               Match = CASE WHEN s.StyleNo = k.FileKey THEN 'Exact'
                            WHEN s.StyleNo LIKE k.Pattern + N'[_-]%' THEN 'Suffix'
                            WHEN s.StyleNo LIKE k.Pattern + N'[A-Za-z]' OR s.StyleNo LIKE k.Pattern + N'[A-Za-z][_-]%' THEN 'Variant'
                            ELSE 'Root' END
        FROM keys k
        JOIN style.Style s
          ON s.StyleNo = k.FileKey
          OR s.StyleNo LIKE k.Pattern + N'[_-]%'
          OR s.StyleNo LIKE k.Pattern + N'[A-Za-z]'
          OR s.StyleNo LIKE k.Pattern + N'[A-Za-z][_-]%'
          OR (k.RootKey IS NOT NULL AND (s.StyleNo = k.RootKey OR s.StyleNo LIKE REPLACE(REPLACE(REPLACE(k.RootKey, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'[_-]%'))
    )
    SELECT h.FileKey, h.Match, s.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, s.ImageUrl, s.SketchUrl, s.IsActive
    FROM hits h
    JOIN style.Style s ON s.StyleId = h.StyleId
    LEFT JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    ORDER BY h.FileKey, CASE h.Match WHEN 'Exact' THEN 1 WHEN 'Suffix' THEN 2 WHEN 'Variant' THEN 3 ELSE 4 END, s.StyleNo, s.SeasonCode;
END
GO

CREATE OR ALTER PROCEDURE style.usp_Style_SetPicture
    @StyleIds  nvarchar(max),     -- JSON array of style ids
    @Field     varchar(10),       -- Image | Sketch
    @ImageUrl  nvarchar(500),
    @Overwrite bit = 0,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Field NOT IN ('Image', 'Sketch') THROW 50000, N'Choose whether the file is the style''s picture or its sketch.', 1;
    IF NULLIF(TRIM(@ImageUrl), N'') IS NULL THROW 50000, N'The picture address is missing.', 1;

    DECLARE @ids TABLE (StyleId int PRIMARY KEY);
    INSERT @ids (StyleId)
    SELECT DISTINCT TRY_CAST(value AS int) FROM OPENJSON(@StyleIds) WHERE TRY_CAST(value AS int) IS NOT NULL;

    DECLARE @set TABLE (StyleId int PRIMARY KEY);
    UPDATE s
    SET ImageUrl  = IIF(@Field = 'Image', @ImageUrl, s.ImageUrl),
        SketchUrl = IIF(@Field = 'Sketch', @ImageUrl, s.SketchUrl),
        UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    OUTPUT inserted.StyleId INTO @set
    FROM style.Style s
    JOIN @ids i ON i.StyleId = s.StyleId
    WHERE @Overwrite = 1 OR IIF(@Field = 'Image', s.ImageUrl, s.SketchUrl) IS NULL;

    -- Updated: set now. Skipped: already had one (or no longer exists).
    SELECT i.StyleId, Updated = CAST(IIF(x.StyleId IS NULL, 0, 1) AS bit)
    FROM @ids i LEFT JOIN @set x ON x.StyleId = i.StyleId
    ORDER BY i.StyleId;
END
GO

CREATE OR ALTER PROCEDURE style.usp_StyleImageImport_Existing
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, s.ImageUrl, s.SketchUrl
    FROM style.Style s
    LEFT JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    WHERE s.ImageUrl LIKE N'/api/v1/styles/images/%' OR s.SketchUrl LIKE N'/api/v1/styles/images/%'
    ORDER BY s.StyleNo, s.SeasonCode;
END
GO

CREATE OR ALTER PROCEDURE style.usp_StyleImageImport_MoveExisting
    @Items     nvarchar(max),     -- JSON [{"StyleId": 1, "ImageUrl": "...", "SketchUrl": null}]: the addresses the check saw
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @want TABLE (StyleId int PRIMARY KEY, ImageUrl nvarchar(500) NULL, SketchUrl nvarchar(500) NULL);
    INSERT @want
    SELECT DISTINCT StyleId, ImageUrl, SketchUrl
    FROM OPENJSON(@Items) WITH (StyleId int, ImageUrl nvarchar(500), SketchUrl nvarchar(500))
    WHERE StyleId IS NOT NULL;

    DECLARE @moved TABLE (StyleId int PRIMARY KEY);
    -- Both columns are read before either is written, so this swaps (or moves the only one across).
    UPDATE s
    SET ImageUrl = s.SketchUrl, SketchUrl = s.ImageUrl, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
    OUTPUT inserted.StyleId INTO @moved
    FROM style.Style s
    JOIN @want i ON i.StyleId = s.StyleId
    WHERE ISNULL(s.ImageUrl, N'') = ISNULL(i.ImageUrl, N'') AND ISNULL(s.SketchUrl, N'') = ISNULL(i.SketchUrl, N'');

    SELECT i.StyleId, Moved = CAST(IIF(m.StyleId IS NULL, 0, 1) AS bit)
    FROM @want i LEFT JOIN @moved m ON m.StyleId = i.StyleId
    ORDER BY i.StyleId;
END
GO
