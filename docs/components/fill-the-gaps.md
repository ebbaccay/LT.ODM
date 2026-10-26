# Fill the gaps

**Where:** Style Library > Fill the gaps (`/styles/gaps`; tabs `?tab=gender|productType|seasons|lco`), and the **Fix** links on the home [Dashboard](dashboard.md). **Who:** read: Admin, Merchandiser, Costing, Viewer. Accept, dismiss, merge, fill and ask AI: Admin, Merchandiser. **AI:** only *Ask AI for the rest* (Text job); everything else is rules.

What the library is missing, with suggestions a person accepts. Search, filters, the BOM check's product-type comparisons, Similar styles and the dashboard all group by gender, product type and season, so styles without them are invisible to those features. **Nothing changes a style until someone accepts.**

The four tiles at the top show what is missing and how many suggestions are ready; a line under them counts what has no suggestion (lead time, SAP supplier: fill them from the source system on the next import) and how many LCO estimates are in use.

## Gender and product type

A list of the styles missing the field, filtered by customer and season, showing the suggestion, its confidence and why.

| Confidence | Gender | Product type |
|---|---|---|
| **High** | The description or model says it (*Women's*, *Men's*, *kids*, *unisex*); the style number shows it (Skechers `P226M177`, `SL127W208`, `26FA_M_PT08`: M = men, W = women; checked on 290 styles that had both, 288 agreed); other seasons of the same style (same customer and base number) all have one gender | Other seasons of the same style all have one product type |
| **Medium** | The customer account's name (*TJ Skechers Women Domestic*, *POLO MEN'S WOVEN SHIRT*; not when it names both); a women-only garment (bra, dress, skirt); the AI's answer | The garment the description names (*WOVENDOWNJACKET1* is a jacket, *Men's … KNIT SHORT SLEEVE TEE* a tee), as the product type the customer uses most for it, else its brand, then one whose name matches the weave (KNIT / WOVEN), then the most used; the AI's answer |
| **Check** | Clues disagree (e.g. `27SM_W_SK01` described as *Men's … half skirt*): the first clue is shown, look before accepting | - |

Garments are read by `style.fn_GarmentCategory`: JACKET, VEST, HOODIE, PULLOVER, POLO, TEE, SHIRT, TANK, TOP, PANTS, SHORTS, TIGHTS, SKIRT, DRESS, BRA, UNDERWEAR, SLEEPWEAR, SET, ACCESSORY. Words run together are read too; sleeve lengths are ignored (a short sleeve tee is a tee, not shorts); a hooded jacket is a jacket; short pants are shorts. On the 1,970 styles that had a product type in October 2026 the description and the product type name gave the same garment for 85%; most of the rest were labelled differently in the source (*Women's woven trousers* filed as DRESS) or are close calls (placket shirt).

October 2026, 2,502 styles: 1,334 of 1,577 missing genders had a suggestion (1,033 High); 372 of 376 missing product types (all Medium).

- **Show**: *With a suggestion* (default, filter by confidence), *No suggestion* (choose by hand or ask AI), *All missing*.
- Each row has a pick list prefilled with the suggestion; change it to choose by hand. High and Medium rows start ticked, Check rows do not.
- **Accept ticked** writes the chosen values. **Accept all high (N)** accepts every High suggestion in the customer / season filter (not only the page shown), after a confirmation. **Dismiss ticked** turns suggestions down; the same value is not offered again for that style (`style.GapDismissed`).
- Only styles still missing the field change; a style someone filled in the meantime is left alone. Accepting records who and when on the style like an edit.
- **Ask AI for the rest** (styles with no suggestion): sends the next 25 to the Text job, again and again until none are left or **Stop**. Each batch runs as an [AI task](../ai/overview.md#ai-tasks-slow-ai-work-in-the-background), so leaving the page keeps the batch in progress (the next one starts only while the page is open). Its answers appear as Medium suggestions marked **AI**; a style the AI could not read is not asked again. What is sent: [Data sent to AI services](../ai/data-sent.md#style-library--fill-the-gaps-ask-ai-for-the-rest).

## Seasons

Seasons imported with the source system's names (`FA26`, `26FW`, `27SS`, `SS27`, `SU27`, `26Q2`, `CR26`, `999`). **Merge into** is prefilled with the year-term season the name shows (`FA26` -> `2026-FA`, `27SS` -> `2027-SS`); quarters, `CR26`, `HO26` and `999` need you to type one (pick from the list or type e.g. `2026-SU`).

**Merge** (after a confirmation) moves the season's styles into the year-term season (created when needed), **remembers the name as an import alias** (`staging.ImportCodeAlias`, list `season`) so later uploads of the same workbook use the new season, and removes the old season when no style is left in it. A style whose customer already has the same style number in the target season stays where it was (shown as conflicts before you merge).

## LCO consumption

BOM lines without an LCO consumption, per customer account and season. Every one of them (36,296 in October 2026, all from the GO export) has a brand consumption; on lines that have both, LCO equals the brand consumption on 89% (accessories) to 99.9% (labels, trims, fabric) of lines. So the suggestion is:

1. The **brand consumption**; with *Add the brand allowance*, brand consumption x (1 + allowance) (`ASAD_Consump_Allowance`, 0.03 = 3%).
2. A line without a brand figure: the median LCO of at least 3 other styles using the same material and unit, same product type first (estimated LCOs are not used as examples).

**Fill** (one customer and season) or **Fill all** (after a confirmation) writes the figures and marks them **estimate** (`style.BomLine.LcoIsEstimate`). The style page shows *estimate* under the LCO; saving the line with another figure clears the mark (saving it unchanged keeps it). The style page's BOM tab has the same fill for one style (**Fill LCO**, see [Styles](styles.md#bom-tab-fill-lco)). Filling the whole library took 1.6 s on 36,000 lines.

## Speed

Suggestions are worked out when asked for, not stored (only AI answers and dismissals are): about 1 s for the gender list, 0.7 s for product types, 0.4 s for the LCO groups and 2.5 s for the page's tiles on 2,500 styles. The home dashboard shows only what is missing, not the suggestion counts, so it stays fast.

## Import rollback

Accepting, merging and filling change styles like an edit, so a later [import rollback](settings.md#import-settingsimport) lists those styles as changed after the import and stops, as for any other change.

## Code map

| Piece | Location |
|---|---|
| SQL | `db/procedures/style.gaps.procedures.sql` (`fn_GarmentCategory`, `usp_Gaps_Suggest`, `_Summary`, `_List`, `_Accept`, `_Dismiss`, `_ToAsk`, `_SaveAi`, `_Seasons`, `_MergeSeason`, `usp_Lco_Suggest`, `_ForStyle`, `_Summary`, `_Apply`, `usp_Style_Similar`); tables `style.GapSuggestion`, `style.GapDismissed`, column `style.BomLine.LcoIsEstimate` in `db/tables/style.tables.sql` |
| API | `StyleGapsController` (`/api/v1/style-gaps`), `StyleGapRepository`, `GapAdvisor` (AI step), contracts in `StyleGapContracts.cs` |
| Screens | `src/web/ltodm-web/src/app/features/style-library/gaps-page.*`, `lco-fill-dialog.ts`, `style-gaps.service.ts` |
