# LT ODM Style Library

Garment style library (styles, BOMs, workmanship and SMV, costing) with AI features.
It replaces TMS: the TMS modules are ported, and the Style Library, Materials and AI Studio are built on new tables.

**Stack:** ASP.NET Core 10 Web API (gateway) - Angular 22 PWA - MS SQL Server 2025 via stored procedures (Dapper). Hosted on premises on IIS.

**Documentation:** [docs/](docs/README.md) - server installation, IIS deployment and configuration, one page per component, and the AI documentation (including exactly what each AI feature sends to outside AI services). This README is for developers.

## Keeping the docs current

`docs/` is what IT installs from and what management reads to know which data leaves LT. **A change to behaviour or specs updates the matching docs page in the same change:**

| You changed | Update |
|---|---|
| What an AI feature sends, its calls, or a new AI feature | `docs/ai/data-sent.md`, `docs/ai/overview.md`, the component page |
| A setting (appsettings / environment variable) | `docs/installation/04-configuration.md` (and `03-iis-deployment.md` for server values) |
| A SQL script, or the deploy order | `docs/installation/02-database.md` |
| Where files are stored | `docs/installation/08-file-storage.md` |
| A screen, role or menu item | `docs/components/<area>.md`, roles table in `docs/README.md` |
| Hosting, build or release steps | `docs/installation/03-iis-deployment.md`, `06-operations.md`, `07-troubleshooting.md` |

Then run `python tools/check-docs.py`: it fails on any broken link or heading anchor in `docs/` and this README.

## Prerequisites

- .NET 10 SDK
- Node.js LTS (22.x) and Angular CLI 22 (`npm install -g @angular/cli`)
- Access to a SQL Server instance (Windows authentication)
- A trusted ASP.NET dev certificate: `dotnet dev-certs https --trust`

## Folder layout

```
LT.ODM.sln
src/
  api/
    LT.ODM.Api/             Web API: controllers, Program.cs, SignalR hub
    LT.ODM.Application/     DTOs, service/repository interfaces, auth service and password policy
    LT.ODM.Infrastructure/  Dapper repositories calling stored procedures
  web/
    ltodm-web/              Angular app (standalone, strict, PWA, Spartan/ui + Tailwind, AG Grid)
      libs/ui/              Spartan "helm" components (copied into the repo; ours to edit)
      src/app/layout/       App shell: top bar, sidebar, theme panel, LayoutService
      src/app/core/         Auth, health, PWA, client config, AG Grid setup
tests/
  LT.ODM.Api.Tests/         xUnit tests
db/
  tables/  procedures/  seed/   SQL scripts in source control (deploy-auth.sql runs the auth set)
docs/
```

## Database

Run `db/procedures/usp_Health_Ping.sql` against your database. The API only calls named stored procedures; there is no generic "execute procedure" endpoint.

The sign-in schema needs **SQL Server 2025 with database compatibility level 170** (it uses `REGEXP_LIKE` check constraints and the native `json` type):

```powershell
# once, if the database is older than level 170:
#   ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
cd db
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i deploy-auth.sql   # -I: QUOTED_IDENTIFIER ON (sqlcmd defaults to OFF)
```

This creates the `auth` schema (`Users`, `Roles`, `UserRoles`, `PasswordHistory`, `RefreshTokens`, `PasswordResetTokens`, `LoginAudit`), the `nav` schema for the sidebar menu (`Groups`, `Items`, `ItemRoles`), their stored procedures, the base roles and the default menu. All scripts are idempotent; the menu seed only adds missing groups and pages, so changes made in Settings are kept.

### Style Library tables and import

```powershell
cd db
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i tables\style.tables.sql
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.import.procedures.sql
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.procedures.sql        # Styles screens
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\ref.procedures.sql          # Settings > Reference lists
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\style.ai.procedures.sql     # AI Studio (BOM check, renders)
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\mat.procedures.sql            # Materials (where used)
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\mat.spec.procedures.sql       # Materials > Description reader (creates mat.MaterialSpec)
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i tables\ai.tables.sql                 # Settings > AI connections
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\ai.procedures.sql             # Settings > AI connections
sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i seed\nav.menu.sql                     # adds Settings > Import and Reference lists
```

`style.tables.sql` creates the schemas `ref` (seasons, business units, product types, weave and material types, content classes, UOMs), `partner` (customers and suppliers), `mat` (material master), `style` and `staging`:

- `style.Style`: one per customer + season + style number. `BaseStyleNo` is the number without its reuse suffix (`S2508MR1212_SS27` -> `S2508MR1212`). `SketchUrl` and `ImageUrl` hold uploaded images.
- `style.Colorway`: a style in one colour (adidas calls it an article; `ColorwayCode` is the customer's number).
- `style.BomLine`: one distinct part + material + consumption + UOM + supplier on a style; `style.BomLineColorway` records which colorways use it and the material colour in each (the workbook repeats a line once per colorway).
- `style.StyleHistory`: a style reused from an earlier one: `CarryOver` (later season) or `Variant` (same season, e.g. `_B_GRADE`, `_H`). The import links styles with the same customer, base style number and model (`Import`). Versions that add letters to another style's number (`S2808MR0000A` from `S2808MR0000`) are linked by number whatever their model (`Auto`, `style.usp_StyleHistory_LinkByNumber`, run after each import and each style save). Links set by hand (`Manual`) are never replaced, and `Auto` links never replace another link.
- Audit columns (`CreatedBy`, `CreatedUtc`, `UpdatedBy`, `UpdatedUtc`, `RowVer`) on styles, colorways, BOM lines, materials and partners.

**Settings > Reference lists** (Admin, `/settings/reference-lists?list=...`; API `/api/v1/admin/ref-lists`) maintains every pick list on the Styles screens: customers, seasons, season terms, business units, product types, weave types, content classes (BOM sections), material types, UOMs and SAP suppliers (`ref.usp_RefList_*`; the API whitelists the lists in `ReferenceLists.cs`). Codes follow the import's case rules (seasons, terms, weave/material types and content classes upper-case, UOMs lower-case) and are fixed once created. The *Used by* column counts the styles, BOM lines, materials or seasons using a code; a used code cannot be deleted, but customers, suppliers, seasons, business units and product types can be made inactive to hide them from the pick lists.

**Settings > Import** (Admin) loads the Style Library workbook (`Style Header`, `Article`, `BOM Detail`; template in `public/assets/templates/`). The API (`/api/v1/style-library/imports`) reads the .xlsx (at most 50 MB), bulk-copies the rows into `staging.*` and checks them in SQL. The preview shows each style as **New**, **Changed** (differs from the library: header, colorways and/or BOM), **Unchanged** or **Blocked** (has errors), plus every error and warning with its sheet and row. Commit writes the New styles and the Changed styles the admin ticked; a replaced style keeps its id, and images uploaded in the app are kept when the file has none. Staged imports left for a day are cancelled automatically. The **New codes** tab lists the codes the import would add to the reference lists (customer, product type, weave, business unit, material type, unit, SAP supplier) and the values that block rows (content class, article status) or are blanked (gender), each with the existing code it most likely means: same spelling ignoring case, spaces, punctuation and a plural S (`staging.fn_CodeKey`: `JACKETS` = `JACKET`, `YDS` = `yd`, `PANTS (1/1)` = `PANTS1/1`), or, for the rest, an AI suggestion (*Suggest with AI*, Text job; only codes on the list are kept). *Use* rewrites those staged rows and checks the batch again (`staging.usp_StyleImport_NewCodes` / `_MapCode`); values left alone are added as new codes at commit, as before.

**Styles** (`/styles`, `/styles/:id`; API `/api/v1/styles`) lists styles with search (style number, model, description or colorway code) and filters kept in the address. A style's page shows its sketch and photo, colorways, BOM by content class (pick a colorway to see its material colours) and its family history. Admins and merchandisers can create, edit and delete styles, colorways and BOM lines, upload images (`/api/v1/styles/images`, stored under `Files:Root/style-library`), reuse a style in another season (copies colorways, BOM and images and records the carry-over), and set or remove history links by hand. Costing and Viewer users read only; factory users have no access (they see styles through Garment Quotation). Every edit sends the row's version; if someone else saved first the API answers 409 and the screen asks to reload. New customer, season, business unit, product type, material, supplier, material type and UOM codes typed in the forms are added to their lists.

**Materials** (`/materials`, `/materials/:id`; API `/api/v1/materials`; Styles readers; replaces the BOMs placeholder, and `/boms` redirects) shows the BOM by material (`mat.usp_Material_List` / `_Get`). The list has every material with how widely it is used (styles, lines, seasons, suppliers, latest season), most used first, with filters for section, material type, supplier, customer and season. A material's page shows where it is used (every style and line, newest season first, at most 1,000), consumption per product type (per-style totals: lowest, middle half, median, highest; uses under 0.6x or over 1.6x the median are flagged), suppliers, material colours, and materials that may be the same (same description, or same code before the `_` version suffix). Material codes on a style's BOM link to the material's page. Views beside the list (`?view=`, API `/api/v1/materials/insights/...`):
*Standard trims* (per product type: how many different materials do each job, how many are used once, and each trim's share of the styles: standard kit at 50%+, common at 20-50%, occasional below), *Suppliers* (share of styles per SAP supplier, single-source materials, lines without a supplier), *Duplicates* (groups of materials with identical descriptions), *Description reader* (the Text job reads material descriptions, 25 per call, into fibre composition with recycled share, construction, weight, width and a suggested section; the answer is checked - only materials sent, percentages adding up, plausible weight and width, sections on the list - and stored in `mat.MaterialSpec` as Pending until an Admin or Merchandiser accepts it; the description is never changed, and a reading whose description changed since shows as out of date; *Read all* works through the filter one call every ~8 seconds and can be stopped; a material's page shows its reading), *Recycled* (share of fabric lines whose description says recycled, by season and product type; `mat.fn_IsRecycled`) and *Colours* (most used material colours, and colours under several codes).

**AI Studio** (menu group *AI Studio*, `/ai/...`; API `/api/v1/ai`; Style Library readers, renders by Admin and Merchandiser). Rules do the work; AI writes filters, explanations and pictures, and every AI answer is checked before use:

- **Smart search** (`/ai/search`): a request in plain words becomes Styles filters. The model may only use the library's codes (customer, seasons, product types, business unit, weave, gender) plus a material keyword; anything else is dropped. *Open in Styles* shows the same filters (`style.usp_Style_List` takes several seasons / product types separated by commas, and `@Material` matches BOM material codes and descriptions).
- **Change summary** (`/ai/compare?styleId=`): the differences between a style and the one it was reused from (or any style picked), worked out in `StyleComparer` without AI: header fields, colorways (added, removed, renamed, re-coded) and BOM lines (paired by material and part; same section and part with another material = *swapped*). *Summarize with AI* sends only the differences.
- **BOM check** (`/ai/bom-check`): `style.usp_BomCheck_Run` flags lines with no consumption, LCO vs brand gaps over 15%, fabric use far from other styles of the same product type, main-fabric yardage far from the product type's median, consumption changes of more than 10% from the reused style, missing LCO consumption and missing suppliers (rules and thresholds in the procedure header). *Explain with AI* sends the first 80 findings and gets a summary, a fix-first list (only findings that were sent) and a note per rule.
- **Style render** (`/ai/render?styleId=`): a product picture drawn from the style's facts (garment, gender, main fabrics, the colorway's fabric colours, visible trims such as zippers, drawcords and heat transfers; labels, packaging, thread and brand words are left out), guided by the uploaded sketch when the image model accepts one. The prompt is shown and can be edited or rewritten by the text model. Renders are stored in `style.AiRender` and under `Files:Root/style-library`, marked as AI, and can be used as the style photo.

- **Concept to existing styles** (panel *Proven styles in the library* in Concept Studio; `POST /api/v1/ai/concept-matches`, Style Library readers): the Text job reads the concept (brief, products, fabrics, trend tags, customer) into library criteria (product types, gender, weave, customer, BOM fabric keywords, trim types; only codes on the lists are kept), `style.usp_ConceptMatch_Candidates` scores every style with a BOM and returns the best 25 with their main fabrics and trims, and the Text job picks up to 6 with a fit score, why, what can be reused and what differs (only candidate styles are accepted). Two AI calls per search; the panel marks results out of date when the concept changes.
- **AI Lab** (`/ai/lab`): capabilities planned for production (look-alike search, tech pack reader, colour reader, cost and lead-time forecasts, library assistant, material description reader, import fix suggestions, concept-to-style matching). Each shows a hand-written sample result (`ai-lab.data.ts`, style numbers `DEMO-...`), what it needs, and live readiness numbers from the library. No AI is called; when a capability is built it gets its own page and its sample is removed.

**Settings > AI connections** (Admin, `/settings/ai`; API `/api/v1/admin/ai`) chooses the AI service per job without a restart (cached for a minute, dropped on save). A *connection* is where to call: Gemini (Google cloud), an OpenAI-compatible server (vLLM, Ollama, LM Studio, LocalAI) or a custom HTTP service, with its address, optional API key, timeout and whether it runs on the LT network. A *job* points at a connection and model: Text (AI Studio, Concept Studio, Cost Optimization, Market Trends) and Image (AI Studio renders) are used today; Embedding, Vision, Document and Prediction are prepared for AI Lab capabilities. Jobs left on the app default use appsettings (`Ai:Provider`, `Ai:Gemini`, `Ai:OpenAiCompatible`). *Test connection* asks the service for its model list so models can be picked. API keys are encrypted with ASP.NET Data Protection before they are stored (`ai.Connections.ApiKeyProtected`) and never sent to the browser (only the last 4 characters); the key ring is in `DataProtection:KeysFolder` (default `App_Data/keys`, DPAPI-protected on Windows) - back it up with the database, or saved keys must be entered again after a restore.

Every AI Studio page says which AI service it uses and whether the data leaves the LT network (`GET /api/v1/ai/status`). Every AI feature in the app goes through these jobs, so Settings > AI connections is the one place to change the AI service.

On IIS, raise the request limit for uploads over 30 MB (`<requestLimits maxAllowedContentLength="57671680" />` under `system.webServer/security/requestFiltering` in web.config).

## Sign-in

- **Passwords** are hashed with ASP.NET Core Identity's hasher (PBKDF2-HMAC-SHA512, 100,000 iterations, per-user salt). Refresh and reset tokens are stored as SHA-256 hashes only.
- **Password policy** (`Auth:Password` in appsettings): at least 12 characters with upper case, lower case, a number and a symbol; no common passwords (including look-alikes such as `P@ssw0rd2026!` or `Summer2026!`), no keyboard/number sequences or repeated characters, nothing from the user's name or email, and none of the last 5 passwords.
- **Lockout:** 5 failed attempts lock the account for 15 minutes. Sign-in and password endpoints are also rate-limited to 10 requests per minute per IP. Every attempt is written to `auth.LoginAudit`.
- **Sessions:** a 15-minute JWT access token kept in memory by the app, plus a refresh token in an HttpOnly, Secure, SameSite=Strict cookie limited to `/api/v1/auth`. The refresh token rotates on every use, and replaying an old one revokes that sign-in everywhere. "Keep me signed in" lasts 14 days; otherwise 12 hours or until the browser closes.
- **Forgot / reset password:** the reset email link is valid for 30 minutes and works once, with at most 3 requests per hour per user. The forgot-password page answers the same whether or not the email exists. Resetting or changing a password signs the user out of every device.
- **Every API endpoint requires sign-in** unless marked `[AllowAnonymous]` (sign-in endpoints, `/health`). The SignalR hub takes the token as `?access_token=`.

### Secrets and settings

```powershell
# JWT signing key: 64+ random characters, e.g. generated with:
#   [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:SigningKey" "PASTE_RANDOM_KEY" --project src/api/LT.ODM.Api
```

| Setting | Development | IIS (environment variable) |
|---|---|---|
| `Jwt:SigningKey` | user secret | `Jwt__SigningKey` (different key per environment) |
| `Auth:PublicBaseUrl` | `http://localhost:4200` (appsettings.Development.json) | `Auth__PublicBaseUrl`, e.g. `https://ltodm.company.local` |
| `Email:Host`, `Email:Port`, `Email:FromAddress` | leave empty: reset emails are written to the API console log instead | `Email__Host`, `Email__Port`, `Email__FromAddress` (SMTP relay / Exchange) |
| `Ai:Gemini:ApiKey` | user secret (`dotnet user-secrets set "Ai:Gemini:ApiKey" "..."`); the default Gemini key for jobs not set in Settings > AI connections (a key typed there is used instead) | `Ai__Gemini__ApiKey`. Optional: `Ai__Gemini__Model` (default `gemini-2.5-flash`, as in TMS) |
| `Ai:Provider`, `Ai:ImageProvider` | `Gemini` (default: Google's cloud API with the key above; prompts leave the network) or `OpenAiCompatible` (an in-house server) - the default for jobs not set in Settings > AI connections | `Ai__Provider`, `Ai__ImageProvider`. Optional: `Ai__Gemini__ImageModel` (default `gemini-2.5-flash-image`) |
| `Ai:OpenAiCompatible:*` | in-house server with an OpenAI-style API: `Endpoint` (e.g. `http://ai-server.lt.local:8000/v1` for vLLM, `http://ai-server:11434/v1` for Ollama), `Model`; for pictures `ImageModel` and optional `ImageEndpoint` (e.g. LocalAI with Stable Diffusion / FLUX), `ImageSize`; optional `ApiKey`, `TimeoutSeconds` (120), `InHouse` (true; set false if it points outside LT) | `Ai__OpenAiCompatible__Endpoint` etc. |
| `DataProtection:KeysFolder` | default `src/api/LT.ODM.Api/App_Data/keys` | `DataProtection__KeysFolder`, e.g. `D:\LTODM\keys` (encrypts API keys saved in Settings > AI connections; the app pool identity needs modify rights; back it up) |
| `Files:Root` | default `src/api/LT.ODM.Api/App_Data/uploads` | `Files__Root`, e.g. `D:\LTODM\uploads` (the app pool identity needs modify rights; back it up) |

The API refuses to start without `Jwt:SigningKey` and `Auth:PublicBaseUrl`.

### Creating users

Add users in **Settings > User roles** (see below). The first administrator is created from the command line; the password is typed twice without echo and checked against the policy:

```powershell
dotnet run --project src/api/LT.ODM.Api -- create-user --username jdoe --email jane.doe@company.com --name "Jane Doe" --roles Admin
# --must-change  forces a new password at first sign-in (for temporary passwords)
```

Base roles: `Admin`, `Merchandiser`, `Factory`, `Costing`, `Viewer` (see `db/seed/auth.roles.sql`).

### Settings (Admin role)

Users with the `Admin` role see a **Settings** group at the bottom of the sidebar:

- **Menu** (`/settings/nav-config`): sidebar groups and pages, their icons and order, and which roles see each page (no roles = every signed-in user). Page names can be plain text or a translation key such as `nav.garmentQuotation`. To offer another icon, add it to `src/app/tms/shared/ui/menu-icons.ts`.
- **Roles** (`/settings/roles`): add roles and edit their display name and description. A role's name cannot change once created (it is in sign-in tokens and menu rules), and `Admin` cannot be deleted.
- **User roles** (`/settings/user-roles`): add users, and set each user's roles, user group and location. Administrators cannot remove their own `Admin` role.
  - **Add user** takes a name, user name, email, roles, user group and location, but no password. The user gets an email with a one-time link to set their own password (the reset-password page with welcome wording). The link expires after `Auth:InviteTokenHours` (default 72); until it is used the account cannot sign in.
  - **Send password link** (envelope icon) emails a new one-time link, for an expired invite, a forgotten password or a locked account. Earlier links stop working; the current password keeps working until the link is used.
  - Without SMTP settings (development), these emails are written to the API console log, so you can copy the link from there.

There is one role system: `auth.Roles` controls sign-in, the menu, the Settings API (`/api/v1/admin`, `[Authorize(Roles = "Admin")]`) and the role checks in ported TMS screens (which see the role names in lower case, e.g. `admin`). Role, user group and location changes reach a signed-in user at their next token refresh, within 15 minutes. Hiding a page from the menu does not block its address; screens that must be restricted also need a route guard (`roleGuard('Admin')`) and a role check in the API, as Settings has. Every access and role change is written to `auth.LoginAudit` (`AccessChanged`, `RoleChanged`).

> If the API runs behind a reverse proxy or load balancer, configure forwarded headers first. Otherwise every user shares the proxy's IP address, and the per-IP rate limits and audit IPs are wrong.

## Porting TMS modules

LT ODM replaces TMS. Tested TMS modules are ported as they are, still calling their stored procedures; new modules (starting with the style library) use typed API endpoints.

**Compatibility hub `/hubs/sp`** (`TmsProcedureHub`) keeps the TMS method and event names (`ExecuteStoredProcFlat` → `StoredProcResultFlat`, and `StreamStoredProcFlat`) and the same "flat" result format. Ported screens need only the new hub URL and the LT ODM sign-in token. Unlike the TMS ProcedureHub:

- signed-in users only;
- only exact procedure names listed per module in `src/api/LT.ODM.Api/tms-procedures.json` (with optional roles per module); no prefix wildcards;
- the client cannot pick the server, database or connection string: `iplex_srcdb01.dbo.web_rd_x` runs `dbo.web_rd_x` in the configured database;
- parameters the procedure does not declare are refused; `username`, `created_by`, `updated_by` and `modified_by` are always filled from the signed-in user (`BoundParameters`). A module can bind more, e.g. Concept Studio also binds `location` and `user_group` (`"location?"`: the `?` means users without that claim get `''`). `ProcedureBoundParameters` sets bindings for one procedure wherever it is used: `"factory:location"` limits factory users (user group `FTY`) to their own factory and gives everyone else NULL (no filter); `"role:Admin"` passes 1 for Admins and 0 for everyone else; `"-"` removes a binding for a parameter that is only a read filter;
- messages the procedures raise themselves (`RAISERROR`, error number 50000+) reach the screen; other SQL errors are logged and the user sees a reference id.

To finish a module: add typed endpoints, switch its screens to them, then delete the module from `tms-procedures.json`. When the file is empty, remove the hub.

**Front end.** Ported screens live in `src/web/ltodm-web/src/app/tms/` (`modules/`, `services/`, `shared/`), with the TMS path aliases `@modules/*`, `@services/*` and `@env/*`. The TMS services keep their names and APIs but run on LT ODM:

| TMS service | In LT ODM |
|---|---|
| `StoredProcSignalRService` (`spHub.service`) | connects to `/hubs/sp` with the LT ODM sign-in token |
| `UserInfoService`, `AuthService` (`@services/…`) | read the signed-in LT ODM user; roles come from the sign-in token, lower-cased (`admin`), and the menu from `GET /api/v1/navigation` |
| `NavConfigService` | feeds the sidebar from the `nav` tables (main groups, plus groups pinned to the bottom) |
| `SettingsAdminService` | calls `/api/v1/admin` instead of the TMS `tms_nav_*`, `tms_roles` and `tms_user_roles` procedures (those tables are not ported) |
| `ConfirmDialogService` | unchanged API; dialog restyled and mounted once in `App` |
| Transloco (`public/assets/i18n/en.json`, `zh-Hans.json`) | unchanged |

Users have two TMS attributes: `UserGroup` (`FTY` = factory user) and `Location` (factory code). Set them in Settings > User roles, or with `create-user --group FTY --location F001`.

Porting a module, step by step:

1. Copy its folder from `TMS/SignalR-UI5-App/src/app/main/modules/...` to `src/app/tms/modules/...` (spell `garment-quotation` correctly).
2. Fix imports: `src/environments/environment` becomes `@env/environment`; `src/app/shared/...` becomes `@tms/shared/...`.
3. (Optional, interim) Theme its old styles: `node tools/tms-port/port-styles.mjs <file>.scss`. This maps TMS colours to the theme and lists any colours it left alone. Then remove the screen's own page background and padding (the LT ODM shell provides them) and give grid children `min-width: 0` so nothing overflows on phones.
4. Move the markup to Tailwind and Spartan (`libs/ui`), then delete the module's SCSS:
   - Put Spartan directives on native elements (`hlmBtn`, `hlmInput` on inputs, selects and textareas, `hlmTable`/`hlmTh`/`hlmTd`). The existing bindings and `(input)`/`(change)` handlers then stay as they are.
   - Colour statuses with the shared tones in `src/app/tms/shared/ui/tones.ts` (`toneBadge`, `toneDot`, `toneText`), not hex values.
   - Keep SCSS only for print rules (see `garment-quotation.print.scss`).
   - Send module toasts through the Spartan toaster (`toast` from `@spartan-ng/brain/sonner`).
5. In `src/app/tms/tms.routes.ts`, switch the module's placeholder to `loadComponent`. If the module is limited to some roles, add `canActivate: [roleGuard(...)]` there and set the same roles on its page in Settings > Menu.
   - Forms in dialogs use `app-modal` (`src/app/tms/shared/ui/modal.ts`) instead of `ui5-dialog`.
6. Check the screen at phone, tablet and desktop size, in light and dark mode.

**Concept Studio** (`/concept-studio`) is ported with these server pieces besides its procedures:

- AI brief: `POST /api/v1/concept-studio/draft` calls the AI service set for the Text job (Settings > AI connections; default Gemini with `Ai:Gemini:ApiKey`). Signed-in users only, 10 calls per minute per user (`RateLimiting:AiPermitPerMinute`). The concept inputs are sent to that service as data (to Google when it is Gemini); the answer is checked against a JSON schema before it reaches the screen. TMS also generated AI images, but its screen never showed them, so that call is not ported.
- Inspiration images: `POST /api/v1/concept-studio/images` (signed-in; JPEG, PNG, GIF or WebP checked from the file content; at most 10 MB; random file names) stored under `Files:Root/concept-inspiration`, outside the web root. `GET /api/v1/concept-studio/images/{name}` serves them to signed-in users only; the screen loads them with the sign-in token (`appAuthSrc` directive). Removing an image from the board does not delete the file, because a saved concept may still use it.
- Saved concepts are shared with the team: office users see concepts from their own user group + location, Admins and factory users see every concept (factories submit SBU offers against them). Only the creator or an Admin can edit or delete one; loading a teammate's concept loads a copy. The screen only picks the scope (Mine / My team / All); the caller, team and Admin flag (`"role:Admin"` binding) come from the sign-in token, and `db/tms/concept-studio.team-visibility.sql` enforces the rules. In TMS each user saw only their own concepts, but anyone could change any concept by id. Trend tags and target markets are shared lists that every user can edit, as in TMS.

**SBU Submission** (`/order-management`, TMS manage-offering) uses its TMS procedures. The API now fills the submitter, location and user group of a new offer and the author of a factory proposal from the sign-in token, and limits factory users' product lists to their own factory.

**Collection Builder** (`/collection-builder`) uses its TMS procedures. Who added or removed an item comes from the sign-in token; an item keeps the original submitter as its creator, as in TMS. Screens that pick a concept use the shared `app-concept-picker` (`src/app/tms/shared/ui/concept-picker.ts`).

**Product Matching** (`/product-catalog`, TMS product-catalog) ranks SBU products and approved quotations against a concept with the TMS keyword and FOB rules (not an AI model). Its "Submit offer" records a real SBU submission (TMS only changed the label and saved nothing). The TMS `product-matching/:conceptId` prototype showed only mock data and was not ported; its address redirects to Product Matching.

**Cost Optimization** (`/cost-optimization`) keeps its TMS procedures for sessions, suggestions and reviews. AI savings ideas come from `POST /api/v1/cost-optimization/suggestions` (same Text job and per-user limit as Concept Studio; each idea is checked and its saving recomputed). The screen saves ideas in order (clear, one batch save, reload). Who submitted or recalled a review, and the creator's location and group, come from the sign-in token; the factory a plan is sent to comes from the screen. Known TMS limitation: a rejected plan cannot be resubmitted until `web_rd_co_load_session` starts a new session after a rejection.

**Customer Proposal** (`/collection/:id`, id = concept recid) is a printable proposal built from the collection (Generate PDF uses the browser's print; the app's top bar, sidebar and footer are not printed on any page). TMS's Supplier Recommendation, Production Timeline and supplier-metrics sections never had data and are not ported.

**Market Trends** (`/product-trends`, TMS product-trends) keeps its TMS `web_rd_ct_*` procedures for sessions and saved scores. The AI analysis comes from `POST /api/v1/market-trends/analysis` (same Text job and per-user limit as Concept Studio; unknown regions and styles are dropped and numbers are clamped). The screen clears old scores only after the AI answers, then saves them in order (clear, regions, styles, mark complete). Scores, growth figures and notes are labelled as AI estimates. TMS's world map (which downloaded map data from a CDN) is replaced by a ranked bar chart per region, and its invented "mock mode" dashboard, shown before a collection was chosen, is gone.

**SBU Products** (`/sbu-overview`, TMS business-unit "Products Offered") keeps its TMS procedures (hub module `business-unit`). The API limits factory users' lists to their own factory, fills a new product's location and user group from the sign-in token, and stops a factory user moving a product to another factory on edit. Photos go to `POST /api/v1/sbu-products/images` (stored under `Files:Root/sbu-products`, signed-in users only); TMS's upload and delete endpoints accepted anonymous requests. Removing a photo keeps the file. Approved quotations show no lead time: TMS showed `ttl_production`, which is the production cost. TMS's empty SBU Details / Team Members / Performance tabs are not ported (Performance is its own page).

**SBU Performance** (`/sbu-performance`) uses `web_rd_get_sbu_performance`, now limited to Admin and Merchandiser (it shows every factory's prices; TMS let any signed-in user call it). The procedure treats a quotation's `ttl_production` (a cost) as a lead time in days, so the screen re-scores cost and delivery with the procedure's own formula using catalog lead times only. TMS's "AI score" is a fixed weighted formula and is shown as "Overall"; its "Key focus" toggles changed nothing and are not ported. Fixing the procedure itself would let the screen drop that workaround.

**Still trusted from the browser:** `role` and `factory_id` / `factoryId` (sent by the dashboard, garment quotation and others). Fix these when each module gets typed endpoints, by reading them from the user's profile on the server.

**SQL for ported procedures:** deploy them unchanged to SQL Server 2025 (T-SQL written for 2005 still runs), then the LT ODM changes to them in `db/tms/` (run with `sqlcmd -I`). Rewrite a procedure in 2025 style only when its module gets typed endpoints. New procedures use 2025 syntax (`CREATE OR ALTER`, `STRING_AGG`, `THROW`, `json`) and the module's own schema. Procedures that reference `IPLEX_SRCDB01` or `GUIWEB` need those databases, or synonyms pointing at their replacements, on the new server.

## Connection string (user secret)

`appsettings.json` holds an empty `ConnectionStrings:StyleLibrary`. The API refuses to start if it is empty. For local development store it in user secrets (replace `YOUR_SERVER` and `YOUR_DATABASE`):

```powershell
dotnet user-secrets set "ConnectionStrings:StyleLibrary" "Server=YOUR_SERVER;Database=YOUR_DATABASE;Integrated Security=True;Encrypt=True;TrustServerCertificate=True" --project src/api/LT.ODM.Api
```

`TrustServerCertificate=True` is for local development only.

**On the IIS servers** the app pool runs as a domain service account that uses Windows authentication to SQL Server, so production needs no password in any file. Use `Integrated Security=True;Encrypt=True` with a trusted certificate. The fallback is the environment variable `ConnectionStrings__StyleLibrary`.

## AG Grid licence key

The front end uses AG Grid Enterprise (licensed; **Integrated Charts and Sparklines are not licensed and not registered**). The key is not stored in source control: the API reads `AgGrid:LicenseKey` from configuration and serves it to the app at `GET /api/v1/client-config`.

```powershell
dotnet user-secrets set "AgGrid:LicenseKey" "PASTE_YOUR_KEY" --project src/api/LT.ODM.Api
```

On the IIS servers set the environment variable `AgGrid__LicenseKey` on the site / app pool. Without a key the grids still work but show the AG Grid watermark. The key ends up in the browser either way (that is how AG Grid licensing works); this only keeps it out of the repository. The current key covers AG Grid versions released before 3 September 2027.

## Try the app without a database (mock API)

```powershell
cd src/web/ltodm-web
npm install
npm run start:mock      # then open http://localhost:4200 (signed in as an Admin)
```

Development only: invented sample data, any password works, listens on 127.0.0.1. To see the app as a merchandiser or a
factory user, and for what sample data exists, see `src/web/ltodm-web/mock-api/README.md`.

## Run the API and Angular together

Terminal 1 (API, https://localhost:7254):

```powershell
dotnet run --project src/api/LT.ODM.Api --launch-profile https
```

Terminal 2 (Angular, http://localhost:4200, proxies `/api`, `/health` and `/hubs` to the API):

```powershell
cd src/web/ltodm-web
npm install
ng serve
```

Development only: OpenAPI document at `/openapi/v1.json`, UI at `/swagger`.

Checks: `dotnet build`, `dotnet test`, `ng build`.

## API notes

- Routes live under `/api/v1/...`; `/health` includes a SQL Server check; SignalR hub at `/hubs/notifications` (no auth yet, see TODO in `NotificationsHub`).
- Errors are ProblemDetails; exception details are only shown in Development.
- All responses send `Cache-Control: no-store`.
- Rate limiting: default 100 requests per minute per client IP.
- CORS policy `AngularDev` (http://localhost:4200) is only enabled in Development. In production the API serves the Angular build from `wwwroot` (one IIS site, same origin; `apiBaseUrl` is empty): hashed bundles are cached for a year, `index.html` and service-worker files are `no-cache`, and app routes fall back to `index.html` (unknown `/api` and `/hubs` addresses stay 404). Build a release with `tools/build-release.ps1 -Output <folder>`; see [docs/installation/03-iis-deployment.md](docs/installation/03-iis-deployment.md).

## Front end: UI and theme

- **Components:** [Spartan/ui](https://www.spartan.ng) (shadcn/ui for Angular, MIT). Components are copied into `libs/ui` and imported as `@spartan-ng/helm/<name>`. Add more with `ng g @spartan-ng/cli:ui <name>` (settings in `components.json`). Spartan also has chat components (`message`, `bubble`, `message-scroller`, `attachment`) for the AI features.
- **Styling:** Tailwind CSS 4. Theme colours are CSS variables in `src/tailwind.css`. The LT brand primary is `#0d47a1` (placeholder); to change it, edit `--primary` / `--ring` / `--sidebar-primary` there, plus `theme_color` in `public/manifest.webmanifest` and `src/index.html`.
- **Theme panel (all users):** palette icon in the top bar. Light/dark/system mode, accent colour, corner radius and static/overlay menu, saved per browser (`localStorage` key `ltodm.layout`).
- **Breakpoints:** phone < 600px, tablet 600-1024px, desktop > 1024px. Tailwind `sm` = 600px, `lg` = 1025px; the same values are in `src/styles/_breakpoints.scss`. The sidebar is docked on desktop and becomes a slide-out menu below 1025px.
- **Icons:** [Lucide](https://lucide.dev) via `@ng-icons/lucide`; register each icon with `provideIcons` in the component.
- **Grids:** AG Grid Enterprise, themed from the same CSS variables (`src/app/core/grid/ag-grid.setup.ts`). AG Grid is loaded lazily: add `canActivate: [agGridGuard]` to any route that shows a grid. See **Developer > UI reference** in the app for examples.
- **Tables without AG Grid:** `class="responsive-table"` with `data-label` on each `<td>` turns rows into stacked cards on phones.

## Testing the PWA install

The service worker only runs over HTTPS (or on `localhost`) and is disabled in `ng serve`. To test it:

```powershell
cd src/web/ltodm-web
ng build
npx http-server dist/ltodm-web/browser -p 8080 -c-1
```

Open http://localhost:8080 in Chrome or Edge (the API is not proxied by a static server, so the home page will show "Unreachable"). Check DevTools > Application > Service Workers and Manifest. The "Install app" button appears when the browser fires `beforeinstallprompt`. On iPhone use Share > Add to Home Screen (the app shows this hint). To test the "New version available" banner, rebuild and reload while the old version is open.

The service worker caches only the app shell and static assets. `ngsw-config.json` deliberately has no `dataGroups`: API data, costs and client data are never cached on the device. `AuthService.signOut()` also clears Cache Storage.
