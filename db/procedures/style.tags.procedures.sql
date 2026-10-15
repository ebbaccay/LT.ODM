/*
    LT ODM picture tags (style.ImageTag): what style pictures look like, read by the image model built into the API from
    the look-alike vectors (AI Studio > Image search > Index pictures tags them after the vectors). No AI server.

    usp_ImageTag_ToTag    vectors of @Model not tagged with @Version yet, with the style's product type (bottoms get no top tags)
    usp_ImageTag_Save     replaces the tags of the vectors given and marks them tagged with @Version
    usp_ImageTag_Summary  styles per current tag (for the phrase list and status)

    Requires tables\style.tables.sql (style.ImageVector.TagVersion, style.ImageTag, style.fn_StyleTags). Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.tags.procedures.sql
*/
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE style.usp_ImageTag_ToTag
    @Model   nvarchar(200),
    @Version varchar(40),
    @Take    int = 1000
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Take) v.ImageVectorId, Vector = CAST(v.Vector AS nvarchar(max)), ProductTypeName = pt.Name
    FROM style.ImageVector v
    JOIN style.Style s ON s.StyleId = v.StyleId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE v.Model = @Model AND v.Vector IS NOT NULL AND (v.TagVersion IS NULL OR v.TagVersion <> @Version)
    ORDER BY v.ImageVectorId;
END
GO

/* @Items: [{ "ImageVectorId": 1, "Tags": [{ "Attribute": "hood", "Value": "hooded", "Score": 0.071, "Margin": 0.067 }] }] */
CREATE OR ALTER PROCEDURE style.usp_ImageTag_Save
    @Items   nvarchar(max),
    @Version varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @vectors TABLE (ImageVectorId int PRIMARY KEY);
    INSERT @vectors SELECT DISTINCT j.ImageVectorId FROM OPENJSON(@Items) WITH (ImageVectorId int) j
    WHERE EXISTS (SELECT 1 FROM style.ImageVector v WHERE v.ImageVectorId = j.ImageVectorId);

    BEGIN TRAN;
    DELETE t FROM style.ImageTag t JOIN @vectors x ON x.ImageVectorId = t.ImageVectorId;
    INSERT style.ImageTag (ImageVectorId, Attribute, Value, Score, Margin, Version)
    SELECT j.ImageVectorId, LEFT(g.Attribute, 20), LEFT(g.Value, 30), g.Score, g.Margin, @Version
    FROM OPENJSON(@Items) WITH (ImageVectorId int, Tags nvarchar(max) AS JSON) j
    CROSS APPLY OPENJSON(j.Tags) WITH (Attribute varchar(40), Value varchar(60), Score decimal(9, 4), Margin decimal(9, 4)) g
    WHERE j.ImageVectorId IN (SELECT ImageVectorId FROM @vectors);
    UPDATE v SET TagVersion = @Version, TaggedUtc = SYSUTCDATETIME()
    FROM style.ImageVector v JOIN @vectors x ON x.ImageVectorId = v.ImageVectorId;
    COMMIT;

    SELECT COUNT(*) FROM @vectors;
END
GO

CREATE OR ALTER PROCEDURE style.usp_ImageTag_Summary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Attribute, Value, Styles = COUNT(*) FROM style.fn_StyleTags() GROUP BY Attribute, Value ORDER BY Attribute, COUNT(*) DESC;
END
GO

PRINT 'Picture tag procedures ready.';
GO
