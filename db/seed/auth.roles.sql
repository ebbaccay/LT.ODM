/*
    Base roles (one role system for LT ODM and the modules ported from TMS).
    TMS role keys map as: admin -> Admin, merchandiser -> Merchandiser, factory -> Factory, viewer -> Viewer.
    Role names are JWT role claims and menu rules; manage them in Settings > Roles.
    DashboardKey: the home page dashboard for the role's users (NULL = the library overview). Set only when a role is
    added, so a dashboard changed in Settings > Roles is kept.
*/
SET NOCOUNT ON;

MERGE auth.Roles AS t
USING (VALUES
    (N'Admin',          N'Administrator',   N'Full access, including users, roles and menu settings',   CAST(NULL AS varchar(32))),
    (N'Merchandiser',   N'Merchandiser',    N'Styles, BOMs, quotations and offering modules',            NULL),
    (N'Designer',       N'Designer',        N'Creates styles: sketches, colorways and BOMs',             'designer'),
    (N'Factory',        N'Factory',         N'Factory-facing features: quotations and submissions',      NULL),
    (N'Costing',        N'Costing',         N'Maintains workmanship, SMV and costing',                   NULL),
    (N'Viewer',         N'Viewer',          N'Read-only access',                                         NULL)
) AS s (Name, DisplayName, Description, DashboardKey)
ON t.Name = s.Name
WHEN MATCHED AND t.DisplayName IS NULL THEN UPDATE SET DisplayName = s.DisplayName
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, DashboardKey) VALUES (s.Name, s.DisplayName, s.Description, s.DashboardKey);
GO
