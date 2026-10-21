# LT ODM roadmap and status

Status of every component, open fixes, and planned work. Last updated 2026-10-16.

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

LT pitches its own designs to brands, using the style library and AI.

| # | Task | Status | Notes |
|---|---|---|---|
| 5.1 | Design provenance: record which library styles inspired a concept | 💡 | |
| 5.2 | Cross-brand guardrail: check new concepts for closeness to another brand's styles | 💡 | A brand's own styles may inspire pitches to that brand; across brands use only know-how and aggregates |
| 5.3 | Pitch pack: concept + render + cost + lead time in one customer-ready proposal | 💡 | Builds on Customer Proposal and AI Render |
| 5.4 | Virtual try-on / image-to-video (Kling AI) or 2D design (StyTrix) | 💡 | Explored only; cloud services, would send designs outside LT |
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
