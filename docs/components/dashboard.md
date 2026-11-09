# Dashboard (Home)

**Where:** Home > Dashboard (`/`). **Who:** every signed-in user; the library figures are shown to Style Library readers (Admin, Merchandiser, Designer, Costing, Viewer). Factory users see a welcome and the system status only.

**Which dashboard opens.** Each role picks one in [Settings > Roles](settings.md#roles-settingsroles) (*Home dashboard*): the **library overview** (below, the default) or **My styles** (the [designer dashboard](#designer-dashboard-my-styles); the `Designer` role's default). A role that picked a dashboard wins over roles that kept the default; when roles picked different dashboards, the first in `DashboardKeys.All` order wins. **Users see only that one dashboard.** **Admins** get a dashboard picker in the page header to open any of them (the pick is kept for the browser tab; their role's dashboard opens again after the next sign-in). API: `GET /api/v1/dashboards/mine` (every signed-in user) returns the default and the dashboards the user may open (only the default, or all for Admins).

A summary of the Style Library:

- **Totals**: styles, colorways, BOM lines, materials, suppliers, customers, seasons, style families and reused styles.
- **By season**: styles, colorways and BOM lines for the latest seasons.
- **Library health**: how many styles have a BOM, colorways, colorways used on the BOM, an image, a gender, a product type and a year-term season, and how many BOM lines have an LCO consumption. Low coverage is highlighted. The last four rows have a **Fix** link to [Fill the gaps](fill-the-gaps.md) on that tab when something is missing (the percentage is rounded down, so 100% means nothing is missing).
- **Customers, product types, material mix, top materials, top suppliers**: where the library is concentrated.
- **Recently changed styles** and the **last import** (file, who, when).
- **API status**: whether the server and database answer.

Admins see shortcuts to import styles when the library is empty.

**Speed.** The gap counts come from `style.usp_Gaps_Summary` without the suggestion counts (those take about 1.5 s and are shown on Fill the gaps only). The figures are worked out once and kept in the server's memory for everyone (`StyleDashboardCache`): the query (`style.usp_Dashboard_Get`) reads every BOM line and took about 0.6-0.9 s on 1,000 styles / 29,000 lines, growing with the library; kept, the page gets them in milliseconds. They are worked out again after any change made in the app (a style, colorway or BOM line saved or deleted, a copy, history links, an import commit, a reference-list change), and at the latest every **5 minutes**, which covers changes made outside the app (SQL scripts). With several web servers behind a load balancer, each keeps its own copy. Pictures of recently changed styles come as small thumbnails ([File storage](../installation/08-file-storage.md#thumbnails-and-browser-caching)).

The charts use the primary colour and its companion colours, and the cards follow the glass effect; both are set in the theme panel (see [Appearance](appearance.md)).

## Designer dashboard (My styles)

**Who:** Style Library readers; it opens first for roles whose home dashboard is *My styles* (the `Designer` role). **API:** `GET /api/v1/dashboards/designer?season=` (`style.usp_Dashboard_Designer` in `db/procedures/style.designer-dashboard.procedures.sql`). No AI.

It shows the signed-in user's **active styles**: the styles they created (`style.Style.CreatedBy`, so styles they added, copied to another season or made from a concept; an Excel import counts for the person who committed it) and, for a user in a [business unit team](styles.md#business-unit-teams), every style of the team's units. Pick a season or *All seasons*. For a team, the page names the units, each style shows its unit and who changed it last, and **Mine only** narrows the list to the user's own styles.

- **Tiles**: my styles (with colorways and BOM lines), complete styles, average completeness, and styles offered in packages (how many are sampling or ordered).
- **What's missing**: for each check below, how many styles miss it. Click a row to list only those styles; click again to clear.
- **Styles to finish**: each style with a completeness bar and a tag for each gap; a tag opens the style on the tab where it is fixed (colorways or BOM; sketch, product type and gender on the style itself). Line counts on a tag say how many lines or colorways are affected. Filter *To finish* (default) / *Complete* / *All*, search by style, model or customer, sort by least complete or latest change; 20 at a time with *Show more*.
- **My concepts**: concepts the user owns (latest first, dropped ones last) with their linked styles.
- **My styles in packages**: where the user's styles were offered and the client's answer (sampling, ordered, dropped, or no answer yet).
- **New style** (Admin, Merchandiser, Designer) opens the same form as the Styles list.

**Completeness checks** (`DesignerChecks` in the API; the score is the share passed, rounded down, so 100% means nothing is missing):

| Check | Passes when |
|---|---|
| Sketch or photo | the style has a sketch or a photo |
| Product type, Gender | set on the style |
| Colorways | at least one colorway that is not dropped |
| BOM | at least one BOM line |
| Colorways on the BOM | every colorway (not dropped) has a colour on some BOM line |
| Fabric and trim colours | every fabric and trim line (content class FAB, TRI) has a colour for every colorway (not dropped). Accessories are left out: many (thread, labels) are the same for all colorways |
| BOM line details | every line has a UOM, a material type and a content class |
| LCO consumption | every line has an LCO consumption |
| Suppliers | every fabric, trim and accessory line (FAB, TRI, ACC) has a supplier |

Every check counts in the score. When a style has no BOM (or no colorways), the checks that need it fail too but are not listed as tags, so the list says what to do next (add the BOM) rather than everything that follows from it.

**Speed.** Worked out on each visit, per user (not cached): about 0.3 s for one season and about 1 s for 2,500 styles on the laptop test database.

## Adding a dashboard

More role dashboards are planned (at least five). Each one is a key, an API endpoint with its own role check, and a component:

1. **API**: add the key to `DashboardKeys` (constant and `All`; the order in `All` decides between roles that picked different dashboards, and is the order of the Admin picker). Add its endpoint to `DashboardsController` with the roles that may read its data (the picker only hides dashboards; the endpoint must check), and a test.
2. **Database**: its procedure in `db/procedures/` and in `db/deploy-db.cmd` and [Database](../installation/02-database.md). No change to `auth.Roles` (the key is free text checked by the API). To give a seeded role the new dashboard, set it in `db/seed/auth.roles.sql` for new databases; existing roles are changed in Settings > Roles.
3. **Web app**: add the key to `DashboardKey`, `DASHBOARD_KEYS` and `DASHBOARD_TEXT` (`pages/home/dashboards.service.ts`), a component under `pages/home/`, and a branch for it in `home.html` (inside `@defer`, so it does not grow the first download). Settings > Roles and the Admin picker list it automatically.
4. **Texts**: `home.views.<key>` plus the component's texts in `en.json` and `zh-Hans.json`; sample data in `mock-api/data/dashboards.mjs`; a section on this page.

**Quotation Dashboard** (Home > Quotation Dashboard, Admin, Merchandiser, Factory) is the ported TMS dashboard of garment quotations; see [Garment Quotation](garment-quotation.md).
