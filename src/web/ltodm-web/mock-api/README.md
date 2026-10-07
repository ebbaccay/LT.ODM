# Mock API (development only)

Runs the LT ODM web app on a laptop **without** SQL Server, the .NET API, an AI key or SMTP. All data is invented sample
data, kept in memory, and reset when the mock restarts. The mock accepts any user name and password and listens on
`127.0.0.1` only. **Never deploy it.**

## Run

```powershell
cd src/web/ltodm-web
npm install            # first time only
npm run start:mock     # mock API + ng serve with live reload (Ctrl+C stops both)
```

Open http://localhost:4200. You are already signed in as **Jane Doe (Admin)**. Sign out from the user menu to see the
sign-in page; any user name and password sign you back in.

To run them separately: `npm run mock:api` (mock on http://127.0.0.1:4300) and `npx ng serve --proxy-config proxy.mock.json`.

## Sign in as someone else

Set these before `npm run start:mock`:

| What you want to see | PowerShell |
|---|---|
| A merchandiser (no Settings) | `$env:MOCK_ROLES='Merchandiser'` |
| A factory user of factory SH02 | `$env:MOCK_ROLES='Factory'; $env:MOCK_GROUP='FTY'; $env:MOCK_LOCATION='SH02'` |
| A read-only viewer | `$env:MOCK_ROLES='Viewer'` |
| Back to admin | `Remove-Item Env:MOCK_ROLES, Env:MOCK_GROUP, Env:MOCK_LOCATION -ErrorAction Ignore` |

Other settings: `MOCK_PORT` (default 4300; change `proxy.mock.json` too), `MOCK_QUIET=1` (no request log),
`MOCK_AGGRID_KEY` (AG Grid licence key for the UI reference page; without it the grid shows the trial watermark).

## What has sample data

| Screen | Sample data |
|---|---|
| Quotation Dashboard, Garment Quotation | 4 styles in "SS27 Denim Pack", quotations from several factories, remarks, history |
| Concept Studio | 2 saved concepts, customers, seasons, trend tags, markets; AI brief and image upload work (uploads stay in memory) |
| SBU Submission | 6 offers for "SS27 Eco Denim Capsule" in every status |
| Collection Builder | collection with 3 items and 2 approved products to add |
| Product Matching | SBU products and an approved quotation, ranked against the concept |
| Customer Proposal | the same collection as a printable proposal (/collection/101) |
| Cost Optimization | the collection's 3 styles; AI ideas, send for review, recall; one rejected plan |
| Market Trends | two collections: one analyzed (with a declining region), one ready to analyze |
| SBU Products / Performance | 9 SBU products in 4 countries, 2 approved quotations, factories ranked (add, edit, delete and photos work) |
| Settings | menu, 5 roles, 5 users; add user, edit access and menu changes work until restart |

The first page load after starting can be blank while the dev server prepares its dependencies; refresh once.
Screens not ported yet open their "being ported" page. Procedures without sample data return an empty result (the mock
logs `no sample data for ...`).

## Files

- `server.mjs` - HTTP endpoints and the SignalR JSON-protocol hub (`/hubs/sp`, `/hubs/notifications`).
- `start.mjs` - starts the mock, then `ng serve --proxy-config proxy.mock.json`.
- `data/*` - sample data per screen. A procedure's data is a list of result sets (`{ columns, rows }`), or a function
  of the call's parameters for screens that change data (Cost Optimization, Settings).
