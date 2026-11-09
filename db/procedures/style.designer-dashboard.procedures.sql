/*
    LT ODM - Designer dashboard (home page for roles whose dashboard is "designer"; GET /api/v1/dashboards/designer)

    A designer's styles with the figures the completeness checks need: the active styles of their business unit team
    (auth.UserBusinessUnit) plus the ones they created; without business units, the ones they created. The checks
    themselves (which count as missing, the score) are worked out in the API (DesignerChecks), so the rules live in
    one place and are unit-tested.

    Requires style.tables.sql, style.procedures.sql (style.fn_UserBusinessUnits), concept.tables.sql and pack.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.designer-dashboard.procedures.sql
*/
SET NOCOUNT ON;
GO

/*
    @UserName: the designer. @SeasonCode: one season, NULL = all.
    Result sets:
      1. Seasons of the designer's active styles (all seasons, for the season picker), newest first.
      2. The designer's active styles in the scope (BusinessUnitCode, CreatedBy, LastChangedBy: who works on it), with:
           Colorways          colorways not dropped
           ColorwaysNotOnBom  of those, colorways no BOM line has a colour for
           BomLines           BOM lines
           LinesNoColour      fabric and trim lines (FAB, TRI) missing a colour for at least one colorway not dropped
           LinesNoDetails     lines without a UOM, a material type or a content class
           LinesNoLco         lines without an LCO consumption
           LinesNoSupplier    fabric, trim and accessory lines (FAB, TRI, ACC) without a supplier
           Packages           packages the style is in; Outcome: the furthest it got (Ordered, Sampling, Dropped, NULL)
      3. The designer's concepts (OwnerName), latest first (at most @Take).
      4. The designer's styles in packages, latest first (at most @Take).
      5. The designer's business units (their team; none = only their own styles are listed).
*/
CREATE OR ALTER PROCEDURE style.usp_Dashboard_Designer
    @UserName   nvarchar(64),
    @SeasonCode nvarchar(16) = NULL,
    @Take       int = 8
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #mine (StyleId int PRIMARY KEY, SeasonCode nvarchar(16) NOT NULL);
    INSERT #mine (StyleId, SeasonCode)
    SELECT s.StyleId, s.SeasonCode
    FROM style.Style s
    WHERE s.IsActive = 1
      AND (s.CreatedBy = @UserName
           OR s.BusinessUnitCode IN (SELECT BusinessUnitCode FROM style.fn_UserBusinessUnits(@UserName)));

    /* 1. Seasons. */
    SELECT m.SeasonCode, Styles = COUNT(*)
    FROM #mine m
    LEFT JOIN ref.Season se ON se.SeasonCode = m.SeasonCode
    LEFT JOIN ref.SeasonTerm t ON t.Term = se.Term
    GROUP BY m.SeasonCode, se.SeasonYear, t.SortOrder
    ORDER BY CASE WHEN se.SeasonYear IS NULL THEN 1 ELSE 0 END, se.SeasonYear DESC, t.SortOrder DESC, m.SeasonCode;

    IF @SeasonCode IS NOT NULL
        DELETE #mine WHERE SeasonCode <> @SeasonCode;

    /* Colorways not dropped, and BOM lines, of the styles in scope. */
    SELECT k.ColorwayId, k.StyleId,
           OnBom = IIF(EXISTS (SELECT 1 FROM style.BomLineColorway x WHERE x.ColorwayId = k.ColorwayId AND x.MaterialColorCode IS NOT NULL), 1, 0)
    INTO #k
    FROM style.Colorway k
    JOIN #mine m ON m.StyleId = k.StyleId
    WHERE ISNULL(k.Status, '') <> 'DROPPED';

    SELECT StyleId, Colorways = COUNT(*), NotOnBom = SUM(1 - OnBom)
    INTO #ks
    FROM #k
    GROUP BY StyleId;

    -- per line: how many of its style's colorways (not dropped) it has a colour for
    SELECT b.StyleId, b.ContentClassCode, b.UomCode, b.MaterialTypeCode, b.LcoConsumption, b.SupplierId,
           Coloured = (SELECT COUNT(*) FROM style.BomLineColorway x JOIN #k k ON k.ColorwayId = x.ColorwayId
                       WHERE x.BomLineId = b.BomLineId AND x.MaterialColorCode IS NOT NULL)
    INTO #b
    FROM style.BomLine b
    JOIN #mine m ON m.StyleId = b.StyleId;

    SELECT b.StyleId,
           Lines      = COUNT(*),
           NoColour   = SUM(IIF(b.ContentClassCode IN (N'FAB', N'TRI') AND b.Coloured < ISNULL(ks.Colorways, 0), 1, 0)),
           NoDetails  = SUM(IIF(b.UomCode IS NULL OR b.MaterialTypeCode IS NULL OR b.ContentClassCode IS NULL, 1, 0)),
           NoLco      = SUM(IIF(b.LcoConsumption IS NULL, 1, 0)),
           NoSupplier = SUM(IIF(b.SupplierId IS NULL AND b.ContentClassCode IN (N'FAB', N'TRI', N'ACC'), 1, 0))
    INTO #bs
    FROM #b b
    LEFT JOIN #ks ks ON ks.StyleId = b.StyleId
    GROUP BY b.StyleId;

    SELECT pi.StyleId, Packages = COUNT(DISTINCT pi.PackageId),
           Outcome = CASE MAX(CASE pi.Outcome WHEN 'Ordered' THEN 3 WHEN 'Sampling' THEN 2 WHEN 'Dropped' THEN 1 ELSE 0 END)
                         WHEN 3 THEN 'Ordered' WHEN 2 THEN 'Sampling' WHEN 1 THEN 'Dropped' END
    INTO #ps
    FROM pack.PackageItem pi
    JOIN #mine m ON m.StyleId = pi.StyleId
    GROUP BY pi.StyleId;

    /* 2. Styles. */
    SELECT s.StyleId, s.StyleNo, s.Description, s.ModelName, CustomerCode = c.PartnerCode, s.SeasonCode, s.ImageUrl, s.SketchUrl,
           s.ProductTypeCode, s.Gender, s.ConceptId, s.BusinessUnitCode, s.CreatedBy, LastChangedBy = ISNULL(s.UpdatedBy, s.CreatedBy),
           Colorways         = ISNULL(k.Colorways, 0),
           ColorwaysNotOnBom = ISNULL(k.NotOnBom, 0),
           BomLines          = ISNULL(b.Lines, 0),
           LinesNoColour     = ISNULL(b.NoColour, 0),
           LinesNoDetails    = ISNULL(b.NoDetails, 0),
           LinesNoLco        = ISNULL(b.NoLco, 0),
           LinesNoSupplier   = ISNULL(b.NoSupplier, 0),
           Packages          = ISNULL(p.Packages, 0),
           p.Outcome,
           s.CreatedUtc,
           LastChangedUtc    = ISNULL(s.UpdatedUtc, s.CreatedUtc)
    FROM #mine m
    JOIN style.Style s ON s.StyleId = m.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN #ks k ON k.StyleId = m.StyleId
    LEFT JOIN #bs b ON b.StyleId = m.StyleId
    LEFT JOIN #ps p ON p.StyleId = m.StyleId
    ORDER BY ISNULL(s.UpdatedUtc, s.CreatedUtc) DESC, s.StyleId DESC;

    /* 3. Concepts. */
    SELECT TOP (@Take) k.ConceptId, k.Name, CustomerCode = c.PartnerCode, k.SeasonCode, k.Status,
           -- linked styles and styles made from the concept, each once
           Styles = (SELECT COUNT(*) FROM (SELECT cs.StyleId FROM concept.ConceptStyle cs WHERE cs.ConceptId = k.ConceptId
                                           UNION SELECT s.StyleId FROM style.Style s WHERE s.ConceptId = k.ConceptId) u),
           LastChangedUtc = ISNULL(k.UpdatedUtc, k.CreatedUtc)
    FROM concept.Concept k
    LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    WHERE k.OwnerName = @UserName
      AND (@SeasonCode IS NULL OR k.SeasonCode IS NULL OR k.SeasonCode = @SeasonCode)
    ORDER BY CASE k.Status WHEN 'Dropped' THEN 1 ELSE 0 END, ISNULL(k.UpdatedUtc, k.CreatedUtc) DESC;

    /* 4. Styles in packages. */
    SELECT TOP (@Take) pk.PackageId, PackageName = pk.Name, PackageStatus = pk.Status, CustomerCode = c.PartnerCode,
           s.StyleId, s.StyleNo, pi.Outcome,
           AtUtc = COALESCE(pi.OutcomeUtc, pk.SentUtc, pi.CreatedUtc)
    FROM pack.PackageItem pi
    JOIN #mine m ON m.StyleId = pi.StyleId
    JOIN style.Style s ON s.StyleId = pi.StyleId
    JOIN pack.Package pk ON pk.PackageId = pi.PackageId
    LEFT JOIN partner.Partner c ON c.PartnerId = pk.CustomerId
    ORDER BY COALESCE(pi.OutcomeUtc, pk.SentUtc, pi.CreatedUtc) DESC, pi.PackageItemId DESC;

    /* 5. The team's business units. */
    SELECT BusinessUnitCode FROM style.fn_UserBusinessUnits(@UserName) ORDER BY BusinessUnitCode;
END
GO
