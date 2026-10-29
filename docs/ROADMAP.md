# LT ODM roadmap and status

Status of every component, open fixes, and planned work. Last updated 2026-10-28.

**Legend:** ✅ Done · 🧪 Done, still to be tested (browser, real data or real AI model) · 🔧 Open fix · ⏳ Waiting on a decision, data or a server · 💡 Planned · ⏸️ Parked or skipped

## 1. Platform and infrastructure

| # | Task | Status | Notes |
|---|---|---|---|
| 1.1 | ASP.NET Core 10 API + Angular 22 web app, Tailwind/Spartan UI | ✅ | |
| 1.2 | Sign-in, users, roles, password-link emails | ✅ | Emails go to the API log until SMTP is set up (see 7.2) |
| 1.3 | Menu and roles managed from Settings | ✅ | API checks roles on every call |
| 1.4 | English + Simplified Chinese (language switcher) | ✅ | New text goes in both `en.json` and `zh-Hans.json` |
| 1.5 | Appearance panel (mode, colours, glass effect) | ✅ | |
| 1.6 | Local dev database LTODM (SQL Server 2025) with TMS tables copied read-only | ✅ | 8 Trimcard objects fail: they need linked server ACT-PROD |
| 1.7 | Deployment Layout A: API serves the web app; `tools/build-release.ps1` | ✅ | [IIS](installation/03-iis-deployment.md), [Linux/nginx](installation/09-linux-nginx.md) |
| 1.8 | Server-side image compression for every upload and AI render | ✅ | Files saved before this change were not re-compressed |
| 1.9 | Documentation set (installation, components, AI) | ✅ | Kept up to date with every change; `tools/check-docs.py` checks links |
| 1.10 | Production deployment of all SPs and schema in one go | ⏳ | Planned for the end of the port; procedures missing on production are migrated by hand |
| 1.11 | Forwarded headers (proxy / load balancer in front of the API) | 💡 | Only needed if a proxy or load balancer is added |

## 2. Ported TMS modules

| # | Module | Route | Status | Notes |
|---|---|---|---|---|
| 2.1 | Garment Quotation (incl. CMT-total fix) | `/garment-quotation` | ✅ | Confirmed by user |
| 2.2 | Quotation Dashboard | `/dashboard` | ✅ | |
| 2.3 | Concept Studio (AI brief, image upload, team visibility) | `/concept-studio` | ✅ | |
| 2.4 | SBU Submission | `/order-management` | ✅ | See open fixes 6.3 and 6.4 |
| 2.5 | Collection Builder | `/collection-builder` | ✅ | |
| 2.6 | Product Matching (Submit offer creates a real SBU submission) | `/product-catalog` | ✅ | |
| 2.7 | Cost Optimization (AI suggestions) | `/cost-optimization` | ✅ | See open fix 6.1 |
| 2.8 | Customer Proposal (printable) | `/collection/:id` | ✅ | |
| 2.9 | Market Trends (AI analysis, region chart) | `/product-trends` | ✅ | |
| 2.10 | SBU Overview (products + photos) | `/sbu-overview` | ✅ | See open fix 6.2 |
| 2.11 | SBU Performance | `/sbu-performance` | ✅ | App re-scores lead time until 6.5 is fixed |
| 2.12 | Trimcard | `/trimcard` | ⏸️ | Skipped at your request; route shows a "pending" page. Needs linked server ACT-PROD |

## 3. Style Library (new LT ODM tables)

| # | Task | Status | Notes |
|---|---|---|---|
| 3.1 | New schema: ref / partner / mat / style / staging; colourways as rows; history links | ✅ | |
| 3.2 | Settings > Import (Excel template, row validation, preview, overwrite per style, Admin only) | ✅ | 1,003 styles / 28,808 BOM lines imported |
| 3.3 | Import "New codes" tab (spot near-duplicate codes, map them, AI hints) | ✅ | |
| 3.4 | Styles list + detail, full manual edit, copy to season, history links (incl. letter-suffix auto-links) | 🧪 | Not browser-tested by Claude |
| 3.5 | Active / inactive flag, list defaults to active | ✅ | |
| 3.6 | Scale-safe list paging (index, page ids first) | ✅ | Next step if the library passes ~100k styles: stored season sort key |
| 3.7 | Home dashboard on style library data | ✅ | |
| 3.8 | Settings > Reference lists (10 pick lists) | ✅ | |
| 3.9 | Materials (where used, consumption, suppliers, colours, duplicates, recycled, standard trims) | ✅ | |
| 3.10 | Design prompt (copy text for StyTrix / Style3D), image download, drag and drop | ✅ | |
| 3.11 | Settings > Import pictures (crop finder, upscaler, detect sketch vs photo) | 🧪 | Not browser-tested by Claude; you import the pictures yourself |
| 3.12 | "Check existing pictures" + swap picture/sketch | ✅ | 42 sketches moved. Still to check by hand: RC2701OW000A_H, RC2702OM001A (+-H), OJS27250477 |
| 3.13 | "With pictures only" filter on Styles list | ✅ | |
| 3.14 | Re-point Garment Quotation procedures from iPLEX tables to the new tables | 💡 | Then retire the copied iPLEX tables module by module |
| 3.15 | Workmanship page | 💡 | Route `/workmanship` shows "Coming soon". Next after Fill the gaps (your priority, 2026-10-16) |
| 3.16 | Costing page | 💡 | Route `/costing` shows "Coming soon"; cost tools need a role check. Next after Fill the gaps (your priority, 2026-10-16) |
| 3.17 | More pictures in the library | ⏳ | Few styles have pictures, which limits image search, tags and the planned AI features |
| 3.18 | [Fill the gaps](components/fill-the-gaps.md): suggested genders and product types (rules, then AI for the rest), season merges remembered for imports, LCO from brand consumption marked as estimates | 🧪 | Not browser-tested by Claude; SQL checked on the local copy (writes in a rolled-back transaction). Accepting is up to you: 1,033 high-confidence genders ready |
| 3.19 | Similar styles tab on the style page (shared materials, garment, weave, gender; same brand first, other brands know-how only) | 🧪 | Not browser-tested by Claude; about 0.3 s per style |
| 3.20 | Dashboard: gender, product type, season and LCO coverage with **Fix** links to Fill the gaps | 🧪 | Not browser-tested by Claude |
| 3.21 | Lead time 0 on 1,507 styles and SAP supplier missing on 13,684 lines | ⏳ | Not guessed: fill from the source system on the next import |

## 4. AI

| # | Task | Status | Notes |
|---|---|---|---|
| 4.1 | Settings > AI connections (jobs, providers, encrypted keys) | ✅ | Every AI call goes through it |
| 4.2 | Central switches: AI on/off, allow cloud, per-job off | ✅ | |
| 4.3 | AI Studio: Smart search (phrase list first, AI for the rest) | ✅ | Falls back to the phrase list when AI fails or finds nothing |
| 4.4 | AI Studio: Compare, BOM check, Render | 🧪 | Live Gemini calls not tested (would send data to the cloud) |
| 4.5 | Image search with built-in Marqo-FashionSigLIP model (photo, words, more-like-this) | ✅ | Tuned on only 8 pictures |
| 4.6 | Picture tags (hood, colour, …) and `looks` filter | ✅ | Thresholds set on 8 pictures; re-check when more pictures arrive |
| 4.7 | Library assistant (tools, saved questions, chat bubble) | ✅ | Tested on laptop Ollama; slow on CPU |
| 4.7c | AI call limits: calls at once per AI server (per connection, shared by address), quick questions first, users in turn, AI tasks per user, on/off switch | 🧪 | 2026-10-22; unit-tested, SQL on the laptop DB, not browser-tested; dev server needs `ai.tables.sql` + `ai.procedures.sql` and a release |
| 4.7b | AI tasks: slow AI work in the background (ai.Job, AiJobQueue, tray restored after reload, Admin list and Stop, keep 7 days) | 🧪 | 2026-10-22; unit-tested, SQL smoke-tested on the laptop DB, not browser-tested; dev server needs `ai.tables.sql` + `ai.jobs.procedures.sql` and a release |
| 4.7a | Library assistant question patterns (137 seeded wordings over 11 look-ups, filters and codes as slots) | 🧪 | 2026-10-22; unit-tested and seeded on the laptop DB, not browser-tested; dev server needs the 3 SQL scripts |
| 4.8 | Concept to existing styles, material description reader | ✅ | |
| 4.9 | AI Lab demo page | ✅ | Remove the "importFix" sample card once you confirm |
| 4.10 | In-house AI server LTODMSVR (OpenVINO Model Server, text + embedding) | ⏳ | Running, but text answers take ~115 s. Measure the warm time; if still slow, add CPU/RAM or keep the Text job off |
| 4.11 | Vision model on the server | ⏸️ | Removed: 16 GB RAM holds only two models; the built-in model covers image search |
| 4.12 | AI Lab: tech pack PDF → BOM | 💡 | Needs sample tech packs |
| 4.13 | AI Lab: colourway suggestions | 💡 | Needs more colourway pictures |
| 4.14 | AI Lab: cost prediction | 💡 | Needs costing data on the new tables |
| 4.15 | AI Lab: lead-time prediction | 💡 | Needs reliable lead-time data (see 6.5) |
| 4.16 | Library assistant: costing and workmanship tools | 💡 | Guide in [Library assistant](components/library-assistant.md); add a role check |
| 4.17 | Fine-tune Marqo on LT photos | 💡 | Needs a GPU and thousands of photo/text pairs |

## 5. ODM pitch workflow (business goal)

LT pitches its own designs to brands, using the style library and AI. Planned 2026-10-28: a new **Style Packages** menu that sends packages of proven library styles to clients.

**Approach (decided 2026-10-28):** new tables and stored procedures on the library standards (`ref`, `partner`, `style`, `mat`) and new components under a new menu. No setting that switches the app between the old TMS tables and the new ones. The old TMS offering pages (Concept Studio, Collection Builder, Customer Proposal, Product Matching, Market Trends) stay as they are, as a reference. They are hidden or retired per role from Settings > Menu once the new pages replace them. Why: the two data sets use different codes (iPLEX customer codes vs `partner.Partner`; free-text customer and season in concepts), only 5 of 38,066 iPLEX styles are in the library, and a switch would double every procedure, endpoint and test.

**Decisions (2026-10-28):**

1. **Start clean.** Old `tms_*` concepts and collections are not copied; the old pages stay for reference. No customer code map is needed.
2. **Packages first, costs later.** Costing is not available yet, so packages hold styles only. Cost must plug in later without changing the package tables: costs will go in their own table keyed by the package item (one item can have several quotes over time), not in columns on the item.
3. **Material prices:** open; only needed for 5.19.
4. **Offering another brand's style = a new copy** with a new column on `style.Style` pointing at the original style (`OriginStyleId`), so the copy's history can be followed back. This is separate from StyleHistory, which links reuse within the same brand (carry-over, variant).

**Order:** 5a (packages) first, then 5b (concepts), 5c, 5d, 5e.

### 5a. Style Package (core, first)

| # | Task | Status | Notes |
|---|---|---|---|
| 5.8 | New `pack` schema: Package (customer, season, status, owner) and PackageItem (StyleId, order, notes, chosen colourways) with a stable PackageItemId | 🧪 | 2026-10-28: [Style Packages](components/style-packages.md). Tables + procedures on the laptop DB, smoke-tested in rolled-back transactions; no pages yet. No cost columns: costs attach later by PackageItemId (decision 2). Dev server needs `pack.tables.sql`, `pack.procedures.sql`, `style.procedures.sql`, `style.import.procedures.sql` |
| 5.9 | Style Packages list and package editor: add styles from Styles list, Similar styles, image search and the Library assistant | 🧪 | 2026-10-28: menu Style Packages > Packages, list and package page; Add to package from the Styles list (pick), style page, Similar tab, image search (each style or all found) and Library assistant answers; from concepts (Offer in a package, 5.7). API tests pass, repository checked on the laptop DB; not browser-tested. Dev server needs `nav.menu.sql` too |
| 5.10 | Package cards: picture or sketch, key fabrics and trims, colourways, recycled content, lead time where known | 🧪 | 2026-10-28: up to 3 fabrics (composition when read, else BOM description) and 4 trims, recycled from the BOM description (`mat.fn_IsRecycled`); package read about 0.2 s. Not browser-tested. Missing data is shown as missing, not guessed. FOB added by 5.19 |
| 5.11 | Line sheet export: PDF (reuse tech pack PDF / Customer Proposal layout) and Excel | 🧪 | 2026-10-28: PDF cards two per row (tech pack drawing helpers moved to `Pdf.cs`), Excel one row per style with its picture in the row and an empty Price column for 5.19 (opened in Excel 2026-10-28: pictures in place, no repair prompt); internal notes, colourways not offered and other brands' styles left out. Checked on a real package from the laptop DB (few pictures on the laptop); not browser-tested |
| 5.12 | Offer to brand: another brand's style enters a package as a new style of the package's customer; new column `style.Style.OriginStyleId` points at the original | 🧪 | 2026-10-28. Decision 4: any style can be copied; `OriginStyleId` is exactly the style the user picked (a suffixed version such as `S2508MR1212_SS27` stays the reference; earlier versions through its StyleHistory); season and style number typed by the user. Copies know-how only: no pictures, colourways, model code / name, labels / packaging / artwork lines; fabrics, trims and accessories kept. Copied from / Copied to on the style pages; StyleHistory stays for same-brand reuse. Checked on the laptop DB, not browser-tested. Dev server needs `style.tables.sql` too |
| 5.2 | Cross-brand guardrail: check package items and concepts for closeness to another brand's styles | 🧪 | 2026-10-28: [brand closeness check](components/concepts.md#inspiration-pictures-and-the-brand-closeness-check) by the image search look-alike vectors (built-in model, no AI server): each concept picture against every library picture (`concept.usp_Concept_Closeness`), each copied style in a package against other brands' pictures once it has one (`pack.usp_Package_Closeness`, red banner on the package page); Too close from 0.90, Check from 0.85 (`BrandCloseness:*`, set on 143 laptop pictures); a brand's own styles go straight in. Copies already leave the brand's identity behind (5.12). API tests pass, SQL checked on the laptop DB; not browser-tested |

### 5b. Concepts (after packages)

| # | Task | Status | Notes |
|---|---|---|---|
| 5.6 | Pick lists: target markets, trend tags and product categories as `ref` lists (Settings > Reference lists) | 🧪 | 2026-10-28: `ref.TargetMarket`, `ref.TrendTag`, `ref.ProductCategory` (codes of the hit rate garment groups), a first set added when empty; in Settings > Reference lists. No old code map (decision 1). Dev server needs `style.tables.sql`, `ref.procedures.sql` |
| 5.7 | New `concept` schema: concepts on library codes (customer, season), inspiration images, AI brief; a concept can feed a package | 🧪 | 2026-10-28: Style Packages > [Concepts](components/concepts.md) (list, concept page): details on library codes (customer optional), trend tags, inspiration pictures, AI brief (Text job, kept on the concept), AI matches; Offer in a package adds its inspiration styles (other brands' as copies) and records the concept on each item (`pack.PackageItem.ConceptId`). Old Concept Studio stays for reference. API tests pass, repositories checked on the laptop DB; not browser-tested. Dev server needs `concept.tables.sql`, `concept.procedures.sql`, `pack.tables.sql`, `pack.procedures.sql`, `nav.menu.sql` and a release |
| 5.21 | Develop as style: an LT original design from a concept becomes a style of the concept's customer, offered like any style | 🧪 | 2026-10-29 (user: the offered product becomes the brand's design, so the target customer owns it from the start). `concept.usp_Concept_DevelopStyle`, `style.Style.ConceptId`; sketch copied from a concept picture into the style library and read for image search at once; BOM whole from an own-brand inspiration style or know-how only from another brand's (`OriginStyleId`); LT designs listed on the concept, badge on package cards, included in the package closeness check. API tests pass, SQL checked on the laptop DB; not browser-tested. Dev server needs `concept.tables.sql`, `concept.procedures.sql`, `pack.procedures.sql` and a release |
| 5.1 | Design provenance: record which library styles inspired a concept (`ConceptStyle`) | 🧪 | 2026-10-28: inspiration styles per concept with a note and where found (picked, AI match, look-alike); another brand's marked *know-how only*; the style page lists the concepts it inspired. Package items record their source style through 5.12 and their concept through 5.7 |

### 5c. Choosing what to offer

| # | Task | Status | Notes |
|---|---|---|---|
| 5.13 | Client profile: price bands, compliance rules (restricted substances, minimum recycled %), preferred mills, season pitch dates | 🧪 | 2026-10-29: Style Packages > [Client profiles](components/style-packages.md#client-profiles), one per brand (else customer); `pack.ClientProfile`, `ClientProfileMill`, `ClientPitchDate`, check `pack.usp_ClientProfile_Check` (recycled share, restricted words in BOM descriptions, non-preferred mills). Package page lists what each style breaks; What to offer marks them and hides them by default. Price band kept for 5.19. API tests pass, SQL and repository checked on the laptop DB; not browser-tested. Dev server needs `pack.tables.sql`, `pack.procedures.sql`, `nav.menu.sql` and a release |
| 5.24 | Follow-up reminders in the bell: links not opened / not answered / ending, sample requests waiting, pitch dates coming | 🧪 | 2026-10-29: `pack.usp_FollowUp_List` (worked out when asked, nothing stored but Done / Later in `pack.FollowUpDismissal`), days in `FollowUps:*`; links and samples to the package owner and link maker, pitch dates to the client's account manager. API tests pass, SQL checked on the laptop DB; not browser-tested. Email waits for SMTP (5.18) |
| 5.14 | "What to offer this client": client's history, gaps in their range, re-pitch candidates from their older styles, other brands' know-how matched to their price and fabric range | 🧪 | 2026-10-28: Style Packages > What to offer (`pack.usp_Offer_Suggest`, rules only, about 1 s): re-offer their styles not carried over, garments they rarely buy (LT's examples, as copies), styles on fabrics they buy (as copies); tick and Add to package (AddedFrom Offer). Price range not used yet (no prices, 5.19). Not browser-tested. Dev server needs `pack.tables.sql`, `pack.procedures.sql`, `nav.menu.sql` |
| 5.15 | Fabric-story packages: start a package from a material or recycled program and list the proven styles built on it | 🧪 | 2026-10-28: Pick styles for a package on the material page; Materials > Insights > Recycled links per season / product type; Styles list filter Recycled fabric (some / only recycled, `style.usp_Style_List @Recycled`, about 0.5 s). Not browser-tested. Dev server needs `style.procedures.sql` |

### 5d. Sending and feedback

| # | Task | Status | Notes |
|---|---|---|---|
| 5.16 | Shareable read-only link with expiry; client marks each style like / pass / comment | 🧪 | 2026-10-28: Share with client on the package page; links per recipient, 1-90 days, shown once (SHA-256 kept), revoke; client page `/share/...` without sign-in (line sheet content, Like / Pass, comments, PDF); only the package's pictures served; openings logged; 60 requests per minute per IP. API tests pass, repository checked on the laptop DB; not browser-tested. Dev server needs `pack.tables.sql`, `pack.procedures.sql` and a release |
| 5.17 | Package status: draft, sent, viewed, sampling, order; hit rate per category and per client | 🧪 | 2026-10-28: Outcome per style on the package page (Sampling / Ordered / Dropped); Style Packages > Hit rate (`pack.usp_HitRate`, about 0.3 s): totals and by garment, client and where the style came from, laid out as a dashboard 2026-10-29 (headline hit rate, offer-to-order steps, answer and outcome bars, bar per row with a table view); share link sets Sent, first visit Viewed (5.16). What to offer learns from it: garments won / liked move up, dropped / passed move down, styles passed on or dropped (or already copied) are not suggested again. Not browser-tested. Dev server needs `pack.tables.sql`, `pack.procedures.sql`, `nav.menu.sql` |
| 5.16b | Notices when clients answer: the bell for the package owner and the link maker, grouped per visit (10 minutes), live push, package page refreshes | 🧪 | 2026-10-28: `pack.PackageNotice`, `PackageNoticesController`, event `PackageNotice` on the notifications hub. Not browser-tested. Dev server needs `pack.tables.sql`, `pack.procedures.sql` and a release |
| 5.22 | Concept story for the client: a story page per concept before the styles on the line sheet and the client's page | 🧪 | 2026-10-29: `concept.Concept.ClientStory`, `ConceptImage.ForClient` (only pictures the closeness check found clear of other brands), story pages from `pack.usp_Package_Get` (PDF page, Excel Stories sheet, share link with its own picture check `usp_Share_StoryImageAllowed`). API tests pass, SQL checked on the laptop DB; not browser-tested. Dev server needs `concept.tables.sql`, `concept.procedures.sql`, `pack.procedures.sql` and a release |
| 5.23 | Request sample on the client's page, with a note | 🧪 | 2026-10-29: `pack.ShareFeedback.Sample` / `SampleNote`, `PackageNotice.Samples`; the first request sets the style's outcome to Sampling (by *client: name*) and a Sent / Viewed package to Sampling, so the hit rate counts it; shown on the package page and in the bell. API tests pass, SQL checked on the laptop DB; not browser-tested. Dev server needs `pack.tables.sql`, `pack.procedures.sql` and a release |
| 5.18 | Email the package link | ⏳ | Needs SMTP (7.2) |

### 5e. Cost, lead time and visuals

| # | Task | Status | Notes |
|---|---|---|---|
| 5.19 | Costs on packages: a cost table keyed by PackageItemId (quote versions), indicative FOB and lead time on cards and line sheet | ⏳ | Needs Costing / Workmanship (3.15, 3.16), material prices (decision 3) and lead-time data (3.21) |
| 5.3 | Pitch pack: concept + render + cost + lead time in one customer-ready proposal | 💡 | Now the package line sheet (5.11) plus 5.19 and renders |
| 5.20 | Retire the old TMS offering pages once each has a replacement (hide per role in Settings > Menu) | 💡 | Last step; tables stay read-only for reference |
| 5.4 | Virtual try-on / image-to-video (Kling AI) or 2D design (StyTrix) | ⏳ | Cloud services explored only (would send designs outside LT). 2026-10-29: in-house instead, **when the GPU server is available**: virtual try-on with **FASHN VTON 1.5** (Apache 2.0, commercial use allowed; github.com/fashn-AI/fashn-vton-1.5), an LT style's picture on a model photo LT has rights to, for the client story and line sheet. Later options on the same server: Wan 2.2 TI2V-5B picture-to-video (Apache 2.0) and TRELLIS picture-to-3D (MIT, a turnable view, not patterns). Not usable on the current CPU-only laptop and server |
| 5.5 | 3D (Style3D) | ⏸️ | Needs DXF patterns, which LT does not have |

## 6. Open fixes (mostly SQL in TMS procedures)

| # | Fix | Status | Notes |
|---|---|---|---|
| 6.1 | Rejected cost plan cannot be resubmitted (`web_rd_co_load_session` returns the rejected session) | 🔧 | |
| 6.2 | Factory users can edit or delete other factories' SBU products (no ownership check) | 🔧 | `web_rd_upd_sbu_product`, `web_rd_del_sbu_products_bulk` |
| 6.3 | SBU Submission: factories can see competitor prices | 🔧 | Review deferred until you ask |
| 6.4 | SBU Submission: anyone can approve | 🔧 | Review deferred until you ask |
| 6.5 | `web_rd_get_sbu_performance` uses production cost as lead days | 🔧 | |
| 6.6 | Move remaining TMS hub calls to typed endpoints | 💡 | |

## 7. Parked

| # | Task | Status | Notes |
|---|---|---|---|
| 7.1 | Trimcard | ⏸️ | See 2.12 |
| 7.2 | SMTP email | ⏸️ | Emails go to the API log until `Email__Host` is set |
