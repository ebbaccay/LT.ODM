/*
    Default sidebar menu: LT ODM groups plus the modules ported from TMS, with the TMS role rules
    (admin -> Admin, merchandiser -> Merchandiser, factory -> Factory, viewer -> Viewer).
    Idempotent: only adds groups/items/rules that are missing, so changes made in Settings > Menu are kept.
    Run after auth.roles.sql.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Menu text is plain text or a translation key; the LT ODM entries used to be English text. Switch the ones still
-- holding the old seed text to keys so they follow the UI language (texts changed in Settings > Menu are left alone).
UPDATE nav.Groups SET Text = v.NewText, UpdatedBy = N'seed'
FROM nav.Groups g
JOIN (VALUES (N'Home', N'nav.home'), (N'Style Library', N'nav.styleLibrary'), (N'Trimcard Reports', N'nav.trimcardReports'), (N'Developer', N'nav.developer')) v (OldText, NewText) ON g.Text = v.OldText
WHERE NOT EXISTS (SELECT 1 FROM nav.Groups x WHERE x.Text = v.NewText);

UPDATE nav.Items SET Text = v.NewText, UpdatedBy = N'seed'
FROM nav.Items i
JOIN (VALUES
    ('', N'Dashboard', N'nav.dashboard'),
    ('dashboard', N'Quotation Dashboard', N'nav.quotationDashboard'),
    ('styles', N'Styles', N'nav.styles'),
    ('boms', N'BOMs', N'nav.boms'),
    ('workmanship', N'Workmanship & SMV', N'nav.workmanship'),
    ('costing', N'Costing', N'nav.costing'),
    ('trimcard', N'Trimcard', N'nav.trimcard'),
    ('ui-reference', N'UI reference', N'nav.uiReference')
) v (Route, OldText, NewText) ON i.Route = v.Route AND i.Text = v.OldText;

-- Style Library > BOMs became Materials (the BOM seen by material), for the Styles readers.
UPDATE nav.Items SET Text = N'nav.materials', Route = 'materials', UpdatedBy = N'seed'
WHERE Route = 'boms' AND NOT EXISTS (SELECT 1 FROM nav.Items x WHERE x.Route = 'materials');
INSERT nav.ItemRoles (ItemId, RoleId)
SELECT i.ItemId, r.RoleId
FROM nav.Items i
JOIN auth.Roles r ON r.Name IN (N'Admin', N'Merchandiser', N'Costing', N'Viewer')
WHERE i.Route = 'materials' AND NOT EXISTS (SELECT 1 FROM nav.ItemRoles x WHERE x.ItemId = i.ItemId);

-- Settings > Content classes grew into Settings > Reference lists (all Styles pick lists).
UPDATE nav.Items SET Text = N'settings.nav.refLists', Route = 'settings/reference-lists', Icon = 'lucideListChecks', UpdatedBy = N'seed'
WHERE Route = 'settings/content-classes' AND NOT EXISTS (SELECT 1 FROM nav.Items x WHERE x.Route = 'settings/reference-lists');
GO

DECLARE @groups TABLE (Text nvarchar(100), Icon varchar(64), Slot varchar(16), SortOrder int);
INSERT @groups VALUES
    (N'nav.home',            'lucideLayoutDashboard',  'main',    1),
    (N'nav.styleLibrary',    'lucideImages',           'main',    2),
    (N'nav.aiStudio',        'lucideSparkles',         'main',    3),
    (N'nav.manageOfferings', 'lucideStore',            'main',    10),
    (N'nav.sbuModule',       'lucideFactory',          'main',    20),
    (N'nav.trimcardReports', 'lucideTable',            'main',    30),
    (N'nav.developer',       'lucideComponent',        'main',    90),
    (N'nav.settings',        'lucideSettings',         'bottom',  100);

INSERT nav.Groups (Text, Icon, Slot, SortOrder, UpdatedBy)
SELECT g.Text, g.Icon, g.Slot, g.SortOrder, N'seed'
FROM @groups g
WHERE NOT EXISTS (SELECT 1 FROM nav.Groups x WHERE x.Text = g.Text);

-- Group, text, route, icon, sort, roles ('' = every signed-in user)
DECLARE @items TABLE (GroupText nvarchar(100), Text nvarchar(100), Route varchar(200), Icon varchar(64), SortOrder int, Roles nvarchar(200) COLLATE Latin1_General_100_CI_AS);
INSERT @items VALUES
    (N'nav.home',             N'nav.dashboard',               '',                     'lucideLayoutDashboard',  10,   N''),
    (N'nav.home',             N'nav.quotationDashboard',      'dashboard',            'lucideChartColumn',      20,   N'Admin,Merchandiser,Factory'),
    (N'nav.styleLibrary',     N'nav.styles',                  'styles',               'lucideImages',           10,   N'Admin,Merchandiser,Costing,Viewer'),
    (N'nav.styleLibrary',     N'nav.materials',               'materials',            'lucideListTree',         20,   N'Admin,Merchandiser,Costing,Viewer'),
    (N'nav.styleLibrary',     N'nav.workmanship',             'workmanship',          'lucideTimer',            30,   N''),
    (N'nav.styleLibrary',     N'nav.costing',                 'costing',              'lucideCalculator',       40,   N''),
    (N'nav.aiStudio',         N'nav.aiSearch',                'ai/search',            'lucideScanSearch',       10,   N'Admin,Merchandiser,Costing,Viewer'),
    (N'nav.aiStudio',         N'nav.aiCompare',               'ai/compare',           'lucideGitCompareArrows', 20,   N'Admin,Merchandiser,Costing,Viewer'),
    (N'nav.aiStudio',         N'nav.aiBomCheck',              'ai/bom-check',         'lucideShieldAlert',      30,   N'Admin,Merchandiser,Costing,Viewer'),
    (N'nav.aiStudio',         N'nav.aiRender',                'ai/render',            'lucideWandSparkles',     40,   N'Admin,Merchandiser,Costing,Viewer'),
    (N'nav.aiStudio',         N'nav.aiLab',                   'ai/lab',               'lucideFlaskConical',     50,   N'Admin,Merchandiser,Costing,Viewer'),
    (N'nav.manageOfferings',  N'nav.garmentQuotation',        'garment-quotation',    'lucideReceipt',          10,   N'Admin,Merchandiser,Factory'),
    (N'nav.manageOfferings',  N'nav.conceptStudio',           'concept-studio',       'lucideLightbulb',        20,   N'Admin,Merchandiser'),
    (N'nav.manageOfferings',  N'nav.sbuSubmission',           'order-management',     'lucideSend',             30,   N'Admin,Merchandiser'),
    (N'nav.manageOfferings',  N'nav.collectionBuilder',       'collection-builder',   'lucideLayers',           40,   N'Admin,Merchandiser'),
    (N'nav.manageOfferings',  N'nav.productMatching',         'product-catalog',      'lucideShapes',           50,   N'Admin,Merchandiser,Viewer'),
    (N'nav.manageOfferings',  N'nav.costOptimization',        'cost-optimization',    'lucideTrendingDown',     60,   N'Admin,Merchandiser'),
    (N'nav.manageOfferings',  N'nav.customerProposal',        'collection',           'lucideFileText',         70,   N'Admin,Merchandiser,Viewer'),
    (N'nav.manageOfferings',  N'nav.marketTrends',            'product-trends',       'lucideTrendingUp',       80,   N'Admin,Merchandiser,Viewer'),
    (N'nav.sbuModule',        N'nav.overview',                'sbu-overview',         'lucideBuilding2',        10,   N'Admin,Merchandiser'),
    (N'nav.sbuModule',        N'nav.performanceDashboard',    'sbu-performance',      'lucideChartColumn',      20,   N'Admin,Merchandiser'),
    (N'nav.trimcardReports',  N'nav.trimcard',                'trimcard',             'lucideTable',            10,   N''),
    (N'nav.developer',        N'nav.uiReference',             'ui-reference',         'lucideComponent',        10,   N'Admin'),
    (N'nav.settings',         N'settings.nav.navConfig',      'settings/nav-config',  'lucidePanelsTopLeft',    10,   N'Admin'),
    (N'nav.settings',         N'settings.nav.roles',          'settings/roles',       'lucideShieldCheck',      20,   N'Admin'),
    (N'nav.settings',         N'settings.nav.userRoles',      'settings/user-roles',  'lucideUsers',            30,   N'Admin'),
    (N'nav.settings',         N'settings.nav.import',         'settings/import',      'lucideFileSpreadsheet',  40,   N'Admin'),
    (N'nav.settings',         N'settings.nav.refLists',       'settings/reference-lists', 'lucideListChecks',   50,   N'Admin'),
    (N'nav.settings',         N'settings.nav.aiConnections',  'settings/ai',          'lucidePlug',             60,   N'Admin');

BEGIN TRANSACTION;

DECLARE @added TABLE (ItemId int, Route varchar(200));

INSERT nav.Items (GroupId, Text, Route, Icon, SortOrder, UpdatedBy)
OUTPUT inserted.ItemId, inserted.Route INTO @added
SELECT g.GroupId, i.Text, i.Route, i.Icon, i.SortOrder, N'seed'
FROM @items i
JOIN nav.Groups g ON g.Text = i.GroupText
WHERE NOT EXISTS (SELECT 1 FROM nav.Items x WHERE x.Route = i.Route);

-- Role rules only for items added now (rules edited in Settings are left alone).
INSERT nav.ItemRoles (ItemId, RoleId)
SELECT a.ItemId, r.RoleId
FROM @added a
JOIN @items i ON i.Route = a.Route
CROSS APPLY STRING_SPLIT(i.Roles, N',') s
JOIN auth.Roles r ON r.Name = TRIM(s.value);

COMMIT TRANSACTION;
GO
