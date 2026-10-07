/*
    Base roles (one role system for LT ODM and the modules ported from TMS).
    TMS role keys map as: admin -> Admin, merchandiser -> Merchandiser, factory -> Factory, viewer -> Viewer.
    Role names are JWT role claims and menu rules; manage them in Settings > Roles.
*/
SET NOCOUNT ON;

MERGE auth.Roles AS t
USING (VALUES
    (N'Admin',          N'Administrator',   N'Full access, including users, roles and menu settings'),
    (N'Merchandiser',   N'Merchandiser',    N'Styles, BOMs, quotations and offering modules'),
    (N'Factory',        N'Factory',         N'Factory-facing features: quotations and submissions'),
    (N'Costing',        N'Costing',         N'Maintains workmanship, SMV and costing'),
    (N'Viewer',         N'Viewer',          N'Read-only access')
) AS s (Name, DisplayName, Description)
ON t.Name = s.Name
WHEN MATCHED AND t.DisplayName IS NULL THEN UPDATE SET DisplayName = s.DisplayName
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description) VALUES (s.Name, s.DisplayName, s.Description);
GO
