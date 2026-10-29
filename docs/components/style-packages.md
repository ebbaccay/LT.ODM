# Style Packages

**Where:** Style Packages > Packages (`/packages`, `/packages/:id`), What to offer (`/packages/offer`) and Hit rate (`/packages/hit-rate`); LT's own design ideas are worked up in [Concepts](concepts.md) (`/concepts`) and offered from there. **Who:** reading: Admin, Merchandiser, Costing, Viewer (the Style Library readers); changing: Admin, Merchandiser. **Status:** roadmap 5.8 (tables, procedures), 5.9 (API, pages, Add to package), 5.10 (card facts), 5.11 (line sheet) and 5.12 (copies of another brand's style) built 2026-10-28; not browser-tested. See [Roadmap section 5](../ROADMAP.md#5-odm-pitch-workflow-business-goal).

A style package is a set of proven library styles offered to one client: what LT pitches to a brand for a season. It replaces the old TMS Collection Builder / Customer Proposal for library styles; the old pages stay as they are for reference. There are no prices yet: costs come with the Costing module (roadmap 5.19).

## Packages list

Packages as cards (first style's picture, name, customer and season, number of styles, status, owner, last change), latest change first. Search finds package names and descriptions, customers and the style numbers and model names in packages. Filters: customer, season, status and **My packages** (the ones you own); they are kept in the address. **New package** asks for a name, customer and season (optional) and opens the new package.

## Adding styles

Admins and merchandisers add styles from:

- **Styles list:** **Pick for a package** shows a tick box beside each style (and one for all styles shown). Picks are kept while you search, filter and page; the bar at the bottom shows how many are picked and **Add to package**.
- **A style's page:** **Add to package** next to Edit and Reuse. The page also lists the packages the style is in, with their status (everyone who can read the style sees them).
- **Similar tab:** **Add to package** on every similar style; another brand's style is offered as a copy (below).

- **Image search:** **Add to package** on each style found, and **Add N styles to a package** for all of them.
- **Library assistant:** an answer that lists styles has **Add N styles to a package** (page and chat bubble).
- **A concept** (roadmap 5.7): **Offer in a package** on a [concept](concepts.md) adds its inspiration styles; each item keeps the concept (*For concept: ...* on its card, a link back).
- **A fabric or recycled program** (roadmap 5.15): **Pick styles for a package** on a material's page opens the Styles list with the styles built on it and picking turned on; in Materials > Insights > Recycled, each season and product type opens its styles with recycled fabric the same way. The Styles list's **Recycled fabric** filter (some / only recycled) does it for any other filter too.

**Add to package** picks an open package (latest first, searchable; Closed ones are not offered; when exactly one package is for the styles' customer it is chosen for you) or starts a new one (customer and season start from the picked styles when they share them). Afterwards it says what happened to each style: added, already in the package, another brand's (offered as a copy, below), or no longer in the library; **Open package** goes to it.

## What to offer

Style Packages > **What to offer** (also from the package list and, with the package's customer, from a package page) suggests what to put in front of a client, from the library alone (rules, no AI; about 1 s). The client is the customer chosen and every customer of its brand. The page shows how many styles the brand has with LT, its seasons and the garments it buys (from product types, or the description when a style has none), then three groups of styles:

- **Re-offer their styles:** the brand's own active styles from earlier seasons than its latest that were not carried over since (no later version linked to them), in the garments it buys; with a picture, more colourways and a BOM first. Each says its colourways and how many seasons have passed.
- **Garments they rarely buy:** garments LT made at least 10 times for other brands that are under 3% of this brand's styles, with up to 3 of LT's best examples each (picture and BOM first, newest first).
- **On fabrics they already buy:** other brands' styles built on at least 2 fabrics this brand already uses.

**It learns from packages sent to this client** (hit rate): per garment, styles they won (outcome Sampling or Ordered, counted double) or mostly liked (more like than pass votes) move that garment's suggestions up, styles they dropped or only passed on move them down (at most a few places either way, so pictures and colourways still count). Styles they dropped or only passed on are not suggested again (unless won or liked through another answer), nor another brand's style already copied for them. The garment chips show *won / offered* and each suggestion *With this client: N of M offered went to sampling or order*.

Styles of other brands are marked *offered as a copy*: **Add to package** makes them copies (see below). A style is suggested once; variants and styles already in the package (when opened from one) are left out, and each group keeps one style per family (same customer and base style number). Editors tick styles and **Add to package** (the package the page was opened from is chosen); styles added this way are recorded as coming from *Offer* for the hit rate later.

## Package page

- **Header:** name, status, customer (and brand), season, description, owner, who created and changed it, when it was first sent. **Edit** changes all of these, including status and owner. **Add styles** opens the Styles list filtered to the package's customer. **Delete** only on a draft that was never sent.
- **Line sheet (PDF)** and **Excel** download the line sheet for the client (everyone who can read the package; see below).
- **Styles,** in pitch order: picture or sketch (opens the style), style number, customer and season, model or description, product type, gender, weave, lead time (*not known* when the library has none), a green *Recycled* tag, the main fabrics (up to 3, largest consumption first; the fibre composition when the Materials description reader has read it, else the BOM description; a leaf marks recycled ones) and trims (up to 4), the colourways offered, the note for the client and the internal note (lock icon, LT only). A style without fabric lines says *No fabric on the BOM*: nothing is guessed.
- **Notes and colourways** (per style): note for the client, internal note, and *all colourways* or the ones ticked. A colourway chosen earlier that is no longer on the style shows as *No longer on the style* (red) until it is unticked; dropped colourways are marked.
- **Move up / Move down** set the pitch order; the trash icon takes the style out of the package (the style stays in the library).

## Share with client

A link the client opens **without signing in** (roadmap 5.16), on the package page under **Share with client**:

- **Make link:** who it is for (e.g. *Skechers men's buying team*; empty = the customer's name) and how many days it works (1-90, default 30). Make one link per recipient, so the answers say who gave them. Making a link turns a draft package into **Sent**.
- **Copying a link:** the new link shows with **Copy** and **Open**; later, **Copy link** on an active link copies it again to re-send it. LT ODM looks links up by a fingerprint (SHA-256) and keeps the link itself encrypted with the web server's keys (ASP.NET Data Protection, like AI API keys), so a copy of the database alone cannot open a client's package. Links made before 2026-10-28 have only the fingerprint and say *Cannot be copied again*: make a new one to re-send.
- **The list** shows each link: who made it and when, until when it works (or *expired* / *revoked*), how often it was opened and when last, and the client's likes, passes and comments. **Revoke** stops a link at once; the answers stay.
- **The client's page** (`/share/...`, no app menu, English or the browser's chosen language): the package name, customer, season and description, then each style as on the line sheet (picture, model, product facts, fabrics with recycled marks, trims, the offered colourways with their pictures, lead time, the note for the client). **Several people can use one link:** each types their name at the top (remembered in that browser) and their **Like** / **Pass** (again to take it back) and comment per style are kept separately (the name in any case; no name = one shared answer for the link); answers are saved as they go. **Request sample** (roadmap 5.23) asks for a sample of a style, with a note (sizes, colours, quantity, delivery address); pressed again it takes the request back. **The story** (roadmap 5.22) comes first: for each [concept](concepts.md#story-for-the-client) of the package's styles that has a story for the client, its name, trend tags, story, up to 4 pictures marked for the client and the styles it covers. **Download PDF** gives the line sheet. Never on it: internal notes, colourways not offered, another brand's styles, concept pictures not marked for the client, the concept's own idea text.
- **Answers on the package page:** each style shows the tally (likes and passes, *mixed* when both) and each person's answer (name and link, Like / Pass, comment).
- **Notices (the bell):** when a client answers, the package's **owner** and **whoever made the link** get a notice in the top bar's bell: package, client name (or the link), likes, passes and comments. Answers given within 10 minutes of each other add to one unread notice, so a client going through 20 styles makes one notice; a later visit makes a new one. Repeating the same answer adds nothing. Notices are kept on the server (they wait while you are away; read ones go after 90 days), pushed live to open app pages, and marked read when the bell is opened; clicking one opens the package. An open package page reloads its answers and link counts when a notice about it arrives. Email notices wait for SMTP (roadmap 7.2).
- **Sample requests** show under the style's answers on the package page (*Sample requested*, with the note) and in the bell (*N sample requests*). The first request for a style sets its outcome to **Sampling** (by *client: name*) when none was set, and a package that was Sent or Viewed becomes **Sampling**; taking the request back leaves them, since LT may already be making the sample.
- **Status:** the client's first visit turns a Sent package into **Viewed**; a sample request turns it into **Sampling**. Ordered and Closed are set by hand.
- **Safety:** an unknown, revoked or expired link (and a style or picture not in the package) gets the same *this link no longer works*, so a guessed link learns nothing. Pictures are served only when they belong to the package. Every opening is logged (time, IP, browser: `pack.ShareAccess`). Page loads, answers and PDFs are limited to 60 per minute per IP (`RateLimiting:SharePermitPerMinute`). The page is never cached by browsers or proxies.

## Client profiles

Style Packages > **Client profiles** (`/packages/clients`, roadmap 5.13) lists every brand, and every customer without a brand, that has styles or packages: one profile per client, shared by all accounts of a brand (the profile's key is the brand code, else the customer code). Clients with a profile come first. **Set up profile** / **Edit profile** (Admin, Merchandiser; readers see it):

- **FOB price band** (lowest, highest, currency): kept for when costs are on packages (roadmap 5.19); shown on What to offer.
- **Minimum recycled %:** the share of a style's fabric BOM lines written as recycled (`mat.fn_IsRecycled`) must be at least this; a style without fabric lines is pointed out too (nothing to judge).
- **Restricted materials and substances:** words, comma separated (e.g. *PVC, PFAS, angora, fur*); any BOM line whose description holds one is pointed out, with the material.
- **Preferred mills:** supplier codes from the supplier list; fabric lines from other suppliers are pointed out (lines without a supplier are not counted). Empty = any mill.
- **Pitch dates:** season (optional), date and note: when the client looks at offers. The next one shows on the list.
- **LT account manager** (a user name): gets the pitch reminders (below). **Notes.**

Where the profile is used:

- **Package page:** an amber line counts the styles that do not meet the client's rules, and each style card lists what it breaks (*Recycled fabric: 40% (needs 60%)*, *Restricted: PVC: ...*, *Not a preferred mill: ...*). Nothing stops sending: the merchandiser decides.
- **What to offer:** the client's rules above the suggestions, each suggestion with what it breaks, and **Hide styles that break them** (on by default; the count says how many are hidden). Without a profile the page says so.

## Follow-up reminders

The bell (top bar) also lists **follow-ups** (roadmap 5.24), worked out from the packages each time it is opened and every 30 minutes; they count in the bell's number until **Done** (gone for good) or **Later** (back in 3 days). Clicking one opens the package (or Client profiles for a pitch date).

| Follow-up | When | For |
|---|---|---|
| Link not opened | an active share link made 5+ days ago and never opened | package owner and link maker |
| No answers | a link first opened 7+ days ago with no like, pass, comment or sample request through it | package owner and link maker |
| Link ending | an active link that stops working within 3 days | package owner and link maker |
| Sample request | a client's sample request 3+ days old whose style still has the outcome the client set (LT has not set it by hand since) | package owner and link maker |
| Pitch date | a client's pitch date within 14 days with no package of that client (for that season, when given) sent yet | the client's account manager |

Packages that are Ordered or Closed are left out. The days are settings (`FollowUps:*`, [configuration](../installation/04-configuration.md#follow-up-reminders)). Nothing is emailed until SMTP is set up (roadmap 5.18).

## Hit rate

Each style card on the package page has an **Outcome** (editors): *not yet*, **Sampling** (the client asked for samples), **Ordered** or **Dropped**, with who set it and when. Everyone sees it as a badge.

Style Packages > **Hit rate** (roadmap 5.17) counts every style in packages that went to the client (status Sent or later), by the day the package was first sent (default: the last 12 months), for all clients or one customer's brand:

The page is laid out as a dashboard (same numbers as before, no extra query):

- **Hit rate** (the headline): the share of styles offered that went to sampling or an order (Sampling or Ordered), with four figures under it: styles offered (in how many packages), answered (styles with any vote), positive votes (likes of all votes, one vote per person per link per style) and ordered (of all offered), each share with a small bar.
- **From offer to order:** offered, answered, liked by someone (all liked or mixed), to sampling and ordered, each as a bar and a share of the styles offered. Outcomes are set by hand, so the steps are not strictly nested (a style can reach sampling without an answer through a link).
- **Client answers:** the styles offered split into *all liked*, *mixed*, *all passed* and *no answer*, and the votes split into likes and passes. **Outcome:** the styles offered split into ordered, sampling, dropped and not decided. Every piece has its count and share in the legend.
- **By garment, by client (brand) and by where the style came from** (Styles list, style page, Similar, image search, Library assistant, What to offer, copy of another brand's style): a bar per row, its length the styles offered and split by outcome, with the hit rate on the right; the first 8 rows, then **Show all**. Hovering or tabbing to a row shows offered, outcomes, answered and votes. **Table** shows the same as a table (offered, votes and share of likes, answered styles split, to sampling, ordered, dropped). The last breakdown shows whether What to offer and copies win more than styles picked by hand.

Colours: blue = good (darker steps further along), red = passed or dropped, grey = neutral or not decided; light and dark mode each have their own steps.

Several people answering one style no longer hide each other: a style with likes and passes shows as *mixed*. Outcomes are set by hand, except that a client's sample request sets *Sampling* on that style: a package status (Sampling, Ordered) does not set them, because a client seldom takes every style of a package.

## Brand closeness

The ODM pitch guardrail (roadmap 5.2): a brand's own styles go straight in; another brand's style comes in only as a **copy** (know-how, not the look, see below). [LT designs](concepts.md#develop-as-style-lt-designs) developed from a concept (marked **LT design** on their card) are checked too. Once a copy gets a picture (copies start without one: the original's pictures stay behind), the package page compares it with every picture of other brands' styles, its original included, using the image search look-alike vectors (no AI call):

- each copied style shows **Too close** (similarity at or above `BrandCloseness:Close`, default 0.90) or **Check** (at or above `BrandCloseness:Watch`, default 0.85) with the style it looks like and the similarity, or **Clear**;
- a red banner above the styles counts the copies that are too close or need a check: change their design (a new sketch) before sending, or remove them;
- a copy without a picture says its look is not checked yet; one whose picture image search has not read yet says so (Settings > AI connections > Image search reads it).

Concepts are checked the same way, picture by picture ([Concepts](concepts.md#inspiration-pictures-and-the-brand-closeness-check)). Thresholds: [configuration](../installation/04-configuration.md#brand-closeness-check).

## Line sheet

What the client receives, in English (it goes to brands), from the package page:

- **PDF** (A4 landscape): a header with the package name, customer, season, number of styles and description, then the styles as cards, two per row in pitch order: picture (photo, else sketch), style number, model, product type · gender · weave, main fabrics (marked *recycled*), the recycled summary, the offered colourways, lead time and the note for the client.
- **Excel:** the package in the first rows, then one row per style (No., **picture**, style number, season, model, description, product type, gender, weave, main fabric, other fabrics, trims, recycled, colourways, lead time, note, **Price**) with the headings frozen and filtered. The picture (photo, else sketch) is shrunk to its cell and anchored to it, so it moves with sorting and filtering; rows are taller when the sheet has pictures. No. and lead time are numbers. Price is empty until costing comes (roadmap 5.19).
- **Story pages** (roadmap 5.22): before the cards, one per concept of the package's styles with a story for the client (styles offered for the concept, or LT designs developed from it): *The story*, the concept's name, trend tags, the story text, up to 4 pictures marked for the client, and the styles it covers. The Excel has them on a second sheet, **Stories** (concept, story, trends, styles; no pictures).
- Never on it: internal notes, colourways not offered (or no longer on the style), and styles that are no longer of the package's brand.

## Copying another brand's style

Another brand's style is never offered as it is. When **Add to package** reports styles of another brand, each gets a small form: **Season** (the package's season to start with) and **New style number** (yours to give), then **Copy and add**. The copy:

- is a new style of the package's customer, linked to **exactly the style picked** (`style.Style.OriginStyleId`): a suffixed version such as `S2508MR1212_SS27` stays the reference, and its earlier versions are followed through that style's own history;
- keeps the know-how: description, product type, weave, gender, lead time, business unit and the fabric, trim and accessory BOM lines (materials, consumption, suppliers);
- leaves the brand's identity behind: pictures, colourways (their codes and names are the brand's), model code and name, labels / packaging / artwork lines and BOM line pictures;
- shows **Copied from** on its page, and the original shows **Copied to** (both link to the other style). Style history (carry-over, variant) stays for reuse within one customer.

A style of the package's own customer is not copied here: add it as it is, or use **Reuse** on its page. Deleting the original keeps the copy (its link moves to the original's own origin, or goes). An import rollback stops when a style it added was copied afterwards.

## Rules

- **One customer per package**, optionally one season. A new customer or season code typed for a package is added to its list, as on the style form.
- **Only the package's brand.** A style goes in directly when it belongs to the package's customer or to a customer of the same brand (Settings > Reference lists > Brands; e.g. the Skechers accounts). Another brand's style is offered as a **copy** (above). The package's customer can only change to one of the same brand while the package holds styles; a style that is no longer of the package's brand is flagged on the package page.
- **A style is in a package once.** Adding it again reports *already in*.
- **Colourway choices** are kept by colourway code, so an import that re-creates the style's colourways keeps them.
- **Status:** Draft, Sent, Viewed, Sampling, Ordered, Closed, set on Edit; making a share link sets Sent and the client's first visit Viewed. The first status after Draft records when it was sent. Only a draft that was never sent can be deleted; a sent package is closed instead, so it still counts for the hit rate (roadmap 5.17).
- **Who:** every Admin and Merchandiser can edit any package; the owner (the creator unless changed) is for *My packages*. Edits made at the same time are refused with *reload and try again*, as elsewhere. Item edits do not clash with header edits.
- **Costs come later.** Each style in a package keeps the same id while it is in the package; costs and quotes will attach to that id in their own table (roadmap 5.19), so the package tables do not change.

## Effect on styles and imports

- A style in a package cannot be deleted: the delete names the packages to take it out of first.
- An [import rollback](settings.md#import-settingsimport) stops when a style the import added was put in a package afterwards.
- Import overwrites are not affected (styles keep their id; colourway choices follow the code).

## Code map

| Piece | Location |
|---|---|
| Tables | `db/tables/pack.tables.sql`: client profiles `pack.ClientProfile`, `ClientProfileMill`, `ClientPitchDate`, follow-ups done / put off `pack.FollowUpDismissal`; `pack.Package`, `pack.PackageItem` (`ConceptId`: the [concept](concepts.md) it was offered for), `pack.PackageItemColorway`, share links `pack.PackageShare` (`TokenHash`, `TokenProtected`), `pack.ShareFeedback`, `pack.ShareAccess`; column `style.Style.OriginStyleId` in `db/tables/style.tables.sql` |
| SQL | `db/procedures/pack.procedures.sql`: `usp_Package_List`, `_Get` (incl. key materials, `mat.fn_IsRecycled`), `_ForStyle`, `_Save`, `_Delete`, `usp_Offer_Suggest` (What to offer), `usp_Share_Create` / `_List` / `_Token` / `_Revoke` / `_Open` / `_Feedback` / `_ImageAllowed` (share links), `usp_PackageItem_SetOutcome`, `usp_HitRate` (hit rate; column `pack.PackageItem.Outcome`), `usp_Notice_List` / `_Read` (table `pack.PackageNotice`, made by `usp_Share_Feedback`), `usp_PackageItem_Add` (results Added / AlreadyIn / OtherBrand / NotFound; `@ConceptId`), `_AddCopy` (`@ConceptId`), `usp_Package_Closeness` (copies against other brands' pictures), `usp_ClientProfile_List` / `_Get` / `_Save` / `_Check` and `fn_ClientKey` (client profiles), `usp_FollowUp_List` / `_Dismiss` (follow-ups), `_Save`, `_Remove`, `_Reorder`; copy links in `style.usp_Style_Get`; package and copy checks in `style.usp_Style_Delete` and `staging.usp_StyleImport_Rollback`; menu entry in `db/seed/nav.menu.sql` |
| API | `StylePackagesController` (`/api/v1/style-packages`: list, `{id}`, `for-style/{styleId}`, `{id}/line-sheet.pdf` / `.xlsx`, `offer?customer=&packageId=`, `{id}/shares` (list, make), `{id}/shares/{shareId}/link` (copy again, editors), `{id}/shares/{shareId}/revoke`, `{id}/items/{itemId}/outcome`, `hit-rate?customer=&from=&to=`, `{id}/closeness` (`BrandClosenessService`); `ClientProfilesController` (`/api/v1/client-profiles`, `{key}`; `/api/v1/style-packages/{id}/client-check`; `/api/v1/follow-ups`, `follow-ups/dismiss`; `ClientProfileRepository`, contracts and `FollowUpOptions` in `ClientProfiles.cs`; tests `ClientProfilesTests.cs`); `PackageNoticesController` (`/api/v1/package-notices`, `{id}/read`, `read-all`), `HubPackageNotifier` (event `PackageNotice` on the notifications hub, to the group `user:{name}`); without sign-in `SharedPackagesController` (`/api/v1/share/{token}`: page, `items/{itemId}/feedback`, `images/{file}`, `line-sheet.pdf`; `ShareTokens`, `SharedPackageView` in `PackageShares.cs`), create, update, delete, `{id}/items` add, `{id}/items/copy`, `{id}/items/{itemId}` save, `{id}/items/remove`, `{id}/items/order`), `PackageRepository`, `IPackageRepository`, contracts in `PackageContracts.cs`; line sheet `IPackageLineSheetWriter` / `LineSheetContent` (`PackageLineSheet.cs`) and `PackageLineSheetWriter` (`.cs` PDF, `.Excel.cs`), PDF helpers shared with the tech pack in `Pdf.cs`, workbook writer shared with the Excel tech pack in `Xlsx.cs`; tests `StylePackagesTests.cs` |
| Screens | `src/web/ltodm-web/src/app/features/style-packages/` (`packages-list`, `package-detail`, `offer-page`, `hit-rate-page`, `client-profiles-page`, `client-profile-dialog`, `client-profiles.service.ts`, `share-panel`, `package-notice.service.ts` with the bell `layout/component/notification-bell.ts`, `shared-package-page` (the client's page, route `share/:token` outside the app layout), `package-form-dialog`, `package-item-dialog`, `add-to-package-dialog`, `packages.service.ts`); picking in `style-library/styles-list.*`, the button and package links in `style-detail.*`, `similar-styles.ts` |
