# Concepts

**Where:** Style Packages > Concepts (`/concepts`, `/concepts/:id`). **Who:** reading: Admin, Merchandiser, Costing, Viewer (the Style Library readers); changing: Admin, Merchandiser. **Status:** roadmap 5.6 (pick lists), 5.7 (concepts), 5.1 (design provenance) and 5.2 (brand closeness check) built 2026-10-28; not browser-tested. See [Roadmap section 5](../ROADMAP.md#5-odm-pitch-workflow-business-goal).

A concept is one of LT's own design ideas, worked up before it is offered to a brand: inspiration pictures, the library styles it takes from, and an AI brief. Each picture is checked for closeness to other brands' designs (the ODM pitch guardrail: a brand's past styles may inspire pitches to that same brand; across brands only know-how goes, never the look). The concept's styles are then offered in a [style package](style-packages.md), and each package item remembers the concept. The old TMS [Concept Studio](concept-studio.md) stays as it is for reference (decision 1: nothing is copied from it).

## Concepts list

Concepts as cards (first picture, else the first inspiration style's picture; name, status, customer, season, product category, trend tags, pictures, inspiration styles, packages it is in, owner, last change), latest change first. Search finds names, ideas, customers, trend tags and the style numbers of inspiration styles. Filters: customer, season, product category, status and **My concepts**; they are kept in the address. **New concept** asks for the details below and opens the new concept.

## Details

Name; **customer** (optional: an idea need not have a client yet; a new code typed is added to the customer list, as on packages); season; **product category**, **target market** and **trend tags** from Settings > [Reference lists](settings.md#reference-lists-settingsreference-lists); gender; target price as typed (e.g. *USD 12-14 FOB*); the idea in words. Status: **Draft**, **Ready** (worked up, can be offered) or **Dropped**; owner. **Edit** changes all of these. **Delete** removes a concept with its pictures and inspiration styles (the styles stay in the library); a concept with styles offered in a package is kept for the record: set it to *Dropped* instead.

Product categories use the same codes as the garment groups of What to offer and the hit rate (`style.fn_GarmentCategory`: JACKET, HOODIE, PANTS, ...), so concepts and offers count garments alike.

## Inspiration pictures and the brand closeness check

**Add pictures** (or dragging pictures from the computer onto the pictures box) uploads mood pictures or sketches (JPEG, PNG, GIF or WebP, up to 10 MB each; made smaller to at most 1200 px; at most 24 per concept). Each picture is read by the image model built into the API, the same one image search uses (Marqo-FashionSigLIP: nothing leaves the server), and compared with every library picture (photos, sketches, colourway pictures, renders) that image search has read.

Per picture the page lists the library styles that look closest (up to 6), with their similarity:

| Level | When | Meaning |
|---|---|---|
| **Too close** | another brand's style at or above `BrandCloseness:Close` (default 0.90) | Looks like that brand's design: change it before offering |
| **Check** | another brand's style at or above `BrandCloseness:Watch` (default 0.85) | Similar: have a look |
| **Clear** | below Watch, or the concept customer's own brand (*own brand*) | Fine |

The line above the pictures sums it up for the whole concept. Without a customer, every brand counts as another brand. The thresholds were set on the laptop library (143 pictures, 2026-10-28): different garments scored at most 0.93 and rarely above 0.90, while copies and colour variants of one design scored about 1.00. Re-check them when more pictures are in the library ([configuration](../installation/04-configuration.md#brand-closeness-check)).

The check runs when the page opens and with **Check again**. A picture added while the image model was off (not installed, or AI switched off in Settings > AI connections) is read the next time the check runs. A library picture counts only once image search indexing has read it (Settings > AI connections > Image search). A look-alike can be recorded as an inspiration style (**Add as inspiration**).

## AI brief

**Draft with AI** writes a summary, 4-8 products with indicative prices, fabric direction and sourcing notes from the saved details (name, customer, season, category, gender, market, target price, trend tags, the idea), with the Text job (Settings > AI connections), like Concept Studio. The brief is kept on the concept with who drafted it and when; **Draft again** replaces it. Check it before using it.

## Inspiration styles (design provenance)

The library styles the concept takes from, each with a note on what it takes (fabric, construction, fit, trims) and where it was found: **picked** (typed style number), **match** (*Proven styles in the library*, below, each with **Add as inspiration**) or **look-alike** (from a picture's closeness check). The customer's own styles are marked *own brand*; another brand's style is marked *Other brand: know-how only*. A style's page lists the concepts it inspired.

## Proven styles in the library

Library styles to start the concept from, shown **at once, without AI** (changed 2026-10-29: the first version waited minutes for two AI calls on an in-house CPU server). The concept's own fields are already library codes: the **product category** gives its product types (`style.fn_GarmentCategory`), plus **gender** and the **customer**; fabric words come from the brief's fabric lines, the idea and the trend tag names, cut to a stem (*RECYCLED* → *RECYCL*, *COATING* → *COAT*) and kept only when some fabric BOM line holds them (up to 6). A woven word (ripstop, twill, ...) or knit word (jersey, fleece, ...) adds the weave. SQL scores every style with a BOM (`style.usp_ConceptMatch_Candidates`, as Concept Studio's matcher) and the best 12 are listed with their fabrics, the words they matched and *own brand* / *Other brand: know-how only*. They are found again when the concept is saved or its brief drafted.

**Explain with AI** (optional) has the Text job rank the best 25 and say for the best few why each fits, what can be reused and what differs: one AI call instead of two (minutes on an in-house CPU server, seconds on a cloud one). Explained styles move to the top with a fit %; the list says when the concept changed after the explanation. A concept with no category and no fabric words says there is nothing to look for yet.

## Develop as style (LT designs)

**Develop as style** (roadmap 5.21, decided 2026-10-29) turns the concept into an **LT original design**: a new style of the **concept's customer** from the start, since the offered product becomes the brand's design once they adopt it (a concept needs a customer first). The dialog asks for:

- **Season** (the concept's to start with) and **style number** (yours to give; unique for the customer and season);
- **Start the BOM from:** one of the concept's inspiration styles, or *No BOM yet*. The customer's own brand gives its **whole BOM** (with its product type, weave, lead time and business unit); another brand's style gives **know-how only**, as a package copy (fabric, trim and accessory lines; no labels, artwork, colourways or pictures) and is recorded as its origin (`style.Style.OriginStyleId`, *BOM know-how from* on the concept);
- **Sketch:** one of the concept's pictures (or none). It is copied into the style library, so it is the style's sketch on the style page, line sheets, the client's page and image search, and its look-alike vector is read at once (when the image model can run), so the closeness check covers it without waiting for indexing.

The concept's name becomes the model name, its idea the description, and its gender the style's. The new style is marked as developed from the concept (`style.Style.ConceptId`): the style page says *LT design from concept*, package cards show **LT design**, and the concept lists its **LT designs** with **Add to package** each. After creating, Add to package opens for it. A concept with LT designs cannot be deleted (set it to *Dropped*).

## Story for the client

Clients buy a story, not a list of style numbers (roadmap 5.22). **Story for the client** (in the concept's details) is the text the buyer reads; the idea above it stays LT's own. On each inspiration picture, **Show to the client** puts it on the story page (up to 4 are shown, in picture order). A picture goes to the client only when the brand closeness check has read it and found no other brand's design **too close** (*Check* is allowed: the editor has looked); otherwise the page says which style it looks like and leaves it off.

The story page appears on the [line sheet](style-packages.md#line-sheet) (PDF; a Stories sheet in the Excel) and the client's page of a share link, before the styles, for every package that holds a style offered for the concept or an LT design developed from it. A concept without a story has no story page. Pictures reach the client only through the link of a package the concept belongs to (`pack.usp_Share_StoryImageAllowed`).

## Offering a concept

**Offer in a package** opens Add to package with the concept's LT designs, or, while it has none, with its inspiration styles: an open package of the concept's customer (or a new one with the concept's customer and season). The customer's own styles go in as they are; another brand's style is offered as a copy with its own style number (know-how only: no pictures, colourways, model, labels or artwork). Each package item keeps the concept (`pack.PackageItem.ConceptId`, added from *Concept*; copies stay *Copy* for the hit rate) and the package page links back to it. The concept lists the package items offered for it, with their package status and outcome.

On the package page, every **copied** style (another brand's style offered as a new style) and every **LT design** is checked the same way against other brands' pictures once it has a picture (copies start without one): a red banner counts the copies that look *too close* or need a *check*. See [Style Packages](style-packages.md#brand-closeness).

## Data and code

| Part | Where |
|---|---|
| Tables | `db/tables/concept.tables.sql`: `concept.Concept` (`ClientStory`), `ConceptTag`, `ConceptImage` (look-alike `Vector`, `Model`, `ForClient`), `ConceptStyle` (removed with the style); pick lists `ref.TargetMarket`, `ref.TrendTag`, `ref.ProductCategory` in `db/tables/style.tables.sql` (a first set added when empty); `pack.PackageItem.ConceptId` in `db/tables/pack.tables.sql`; `style.Style.ConceptId` (LT designs) in `db/tables/concept.tables.sql` |
| Procedures | `db/procedures/concept.procedures.sql` (`usp_Concept_List` / `_Get` / `_ForStyle` / `_Lookups` / `_MatchContext` / `_DevelopStyle` / `_Save` / `_SaveBrief` / `_Delete`, `usp_ConceptImage_Add` / `_Remove` / `_Save` / `_ToRead` / `_SetVector`, `usp_ConceptStyle_Add` / `_Save` / `_Remove`, `usp_Concept_Closeness`); `pack.usp_Package_Closeness`; the three lists in `ref.usp_RefList_*` |
| API | `ConceptsController` (`/api/v1/concepts`: list, `lookups`, `{id}`, `for-style/{styleId}`, `{id}/closeness`, `{id}/library-matches` (no AI), `{id}/library-matches/explain` (AI), `{id}/develop`, create, update, delete, `{id}/brief`, `{id}/images` (upload), `{id}/images/{imageId}` (show to the client and caption; remove), `images/{file}` (serve), `{id}/styles`, `{id}/styles/{styleId}` (note, remove)); `BrandClosenessService` and `BrandClosenessOptions` (`Application/Concepts/BrandCloseness.cs`); `ConceptStyleDeveloper` (`ConceptStyleDeveloper.cs`), `ConceptLibraryMatcher` (`ConceptLibraryMatches.cs`) with `ConceptMatcher.ExplainAsync`; `ConceptRepository`, `IConceptRepository`, contracts in `Application/Concepts/ConceptContracts.cs`; pictures in the concept image store (`Files:Root/concept-inspiration`, shared with Concept Studio); tests `ConceptsTests.cs` |
| Screens | `src/web/ltodm-web/src/app/features/concepts/` (`concepts-list`, `concept-detail`, `concept-library-matches`, `develop-style-dialog`, `concept-form-dialog`, `concepts.service.ts`); Add to package with the concept in `style-packages/add-to-package-dialog.ts` |
