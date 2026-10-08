# LT ODM documentation

LT ODM is LT's garment style library and offering platform. It replaces TMS: the TMS screens were ported one by one, and new features (Style Library, Materials, AI Studio) were built on new tables.

**Stack:** ASP.NET Core 10 Web API, Angular 22 (installable web app), Microsoft SQL Server 2025, hosted on premises on IIS.

## For IT: install and run

| Document | Contents |
|---|---|
| [Server requirements](installation/01-server-requirements.md) | Servers, software, accounts, certificates, network and firewall |
| [Database](installation/02-database.md) | Creating the database, deploying the SQL scripts in order, permissions |
| [IIS deployment](installation/03-iis-deployment.md) | Building a release (`tools/build-release.ps1`), the IIS site and app pool, web.config, HTTPS |
| [Configuration](installation/04-configuration.md) | Every setting, where it goes, and which ones are secrets |
| [First start](installation/05-first-start.md) | First administrator, menu, reference data, first import, AI connection |
| [Backup, updates and monitoring](installation/06-operations.md) | What to back up, how to release a new version, health checks, logs |
| [File storage](installation/08-file-storage.md) | Where uploaded images are stored, file names, checks, deleting, moving the folder |
| [Troubleshooting](installation/07-troubleshooting.md) | Common problems and their causes |

## For users and administrators: components

| Area | Document |
|---|---|
| Sign-in, users and roles | [Authentication and users](components/authentication-and-users.md) |
| Home | [Dashboard](components/dashboard.md) |
| Style Library | [Styles](components/styles.md) · [Materials](components/materials.md) |
| AI Studio | [AI Studio](components/ai-studio.md) · [AI Lab](components/ai-lab.md) |
| Manage Offerings | [Garment Quotation](components/garment-quotation.md) · [Concept Studio](components/concept-studio.md) · [Offering tools](components/manage-offerings.md) (SBU Submission, Collection Builder, Product Matching, Cost Optimization, Customer Proposal, Market Trends) |
| SBU Module | [SBU Products and Performance](components/sbu-module.md) |
| Settings | [Settings](components/settings.md) (menu, roles, users, reference lists, import, AI connections) |

## AI: how it works and what leaves LT

| Document | Contents |
|---|---|
| [AI overview](ai/overview.md) | Architecture: jobs, connections, providers, safeguards, limits |
| [Data sent to AI services](ai/data-sent.md) | **For every AI feature: exactly what is sent, what is not, how many calls** |
| [Moving to the in-house AI server](ai/in-house-server.md) | What the server needs and how to switch the app over |

## For developers

The repository [README](../README.md) covers the code layout, local development (user secrets, mock API, `ng serve`), porting rules for TMS modules and the UI conventions.

## Roles at a glance

| Role | Typical user | Can use |
|---|---|---|
| `Admin` | IT / system owner | Everything, including Settings |
| `Merchandiser` | Office merchandisers | Style Library (edit), AI Studio (incl. renders and reviews), Manage Offerings, SBU module |
| `Costing` | Costing team | Style Library and AI Studio (read only) |
| `Viewer` | Read-only office users | Style Library and AI Studio (read only), Product Matching, Customer Proposal, Market Trends |
| `Factory` | Factory users (user group `FTY`) | Garment Quotation, Quotation Dashboard, SBU offers for their own factory |

The menu shows each user only the pages their roles allow. Settings > Menu changes what is shown; the API checks roles on every call regardless.
