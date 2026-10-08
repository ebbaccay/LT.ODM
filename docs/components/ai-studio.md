# AI Studio

**Where:** AI Studio menu group (`/ai/...`). **Who:** Admin, Merchandiser, Costing, Viewer; renders and render prompt rewrite: Admin, Merchandiser. **AI service:** the Text and Image jobs in Settings > AI connections. Every page shows whether data leaves LT (amber banner, cloud), stays (green, in-house), or is blocked by a switch in Settings > AI connections (red). Style render uses both jobs: one line per job, merged into one when both are blocked for the same reason. What each feature sends: [Data sent to AI services](../ai/data-sent.md).

## Smart search (`/ai/search`)

Describe the styles you need in your own words, e.g. *"men's woven jackets for 2027 SS with recycled fabric"*.

**Phrase list first, AI second.** The request is first read by a phrase list (`StyleQueryParser`), with no AI call:

| Criterion | Words understood | Example |
|---|---|---|
| Gender | men, men's, women, ladies, kids, youth, boys, girls, unisex | *women's* → FEMALE |
| Season | SS27, FW26, 2027 SS, 2027-SS, a year (every season of it), spring, summer, fall/winter (the library's seasons with that term) | *SS27* → 2027-SS |
| Product type | Any product type name in Settings > Reference lists, singular or plural, matched on the end of the name; plus tee, hoodie, sweater, leggings | *jackets* → JACKET, JACKETS, JACKET (MIDWEIGHT), TRACKSUIT JACKET |
| Detail | long sleeve, short sleeve, sleeveless, midweight, 1/2, 3/4, 7/8 (narrows the product types) | *long sleeve t-shirts* → T-SHIRT (LONG SLEEVE) |
| Weave | knit, woven | |
| Business unit | Code, full name, or the name's first word | *running* → RUA, RUB, RUX |
| Customer | Code or name; a longer word starting with the code | *adidas* → ADI |
| Material | recycled, fleece, polyester, elastane, zipper, drawcord, ... (several words must all be on one BOM line) | *recycled fleece* |
| Style number | A word with letters and digits | *S2808MR0000* |

Fillers (for, with, that, styles, ...) are skipped. Words that change the meaning (*without*, *not*) and two different values for one criterion (*men and women*) are left to AI.

- **Every word understood**: the library is searched at once, marked *Understood with the phrase list. No AI was used.* Nothing is sent anywhere.
- **Some words not understood**: the request goes to AI (Text job), when AI can run. If it cannot (not set up, turned off, cloud not allowed), the words understood are searched and the rest are listed as ignored.
- The example pills are requests the phrase list understands that find styles in today's library (`GET /api/v1/ai/style-search/phrases`). **Words understood without AI** lists the whole phrase list; click a word to add it.
- Results are real styles from the database (first 50; **Open in Styles** shows the full list with the same filters). With AI, "Read as" shows how the AI understood the request; only codes on the library's lists are kept.
- Endpoints: `POST /api/v1/ai/style-search/rules` (phrase list only, never AI, no AI rate limit; the screen calls it first) and `POST /api/v1/ai/style-search` (phrase list, then AI; AI rate limit).

## Change summary (`/ai/compare?styleId=...`)

What changed between a style and the one it was reused from (or any style you pick).

- Worked out by rules, without AI: header fields; colorways (added, removed, renamed, re-coded: same colour under a new article number); BOM lines paired by material and part. *Swapped* = same section and part with another material. Each change shows old → new values.
- **Summarize with AI** writes a headline, a short summary, highlights by area, and things to check before quoting. Only the differences are sent. The summary card is hidden when no Text job is set up; the differences still show.
- From a style's page: **What changed**. The style's **History** tab opens the same comparison inside the Style Library ([Compare styles](styles.md#compare-styles-stylesidcomparefrom)).

## BOM check (`/ai/bom-check`)

Rules flag BOM lines to look at before costing, for one style or a customer / season (or the whole library):

| Rule | Flags | Severity |
|---|---|---|
| No consumption | Fabric or thread line without any consumption | High |
| LCO vs brand gap | LCO and brand consumption more than 15% apart (50%: High) | Medium / High |
| Unusual fabric use | Fabric use far from other styles of the same product type using it (at least 3) | Medium / High |
| Unusual main fabric | Main fabric yardage far from the product type's median (at least 5 styles) | Medium / High |
| Changed from reused style | Consumption changed more than 10% from the earlier style | Medium |
| LCO not entered | Brand consumption given, LCO missing | Low |
| No supplier | Fabric, trim or accessory without an SAP supplier | Low |

Narrow the findings list by rule (click a rule card), severity, or **search** (style number, season, customer, material code or description, section, part, earlier style or rule name; every word must match). The search looks only through the findings shown on the page; when the check found more than are shown, narrow the check by customer or season to search the rest.

**Explain with AI** sends the first 80 findings and returns a summary, up to 10 findings to fix first (numbered, shown first) and a note per rule. The findings themselves always come from the rules.

## Style render (`/ai/render?styleId=...`)

A product picture drawn from the style's data.

- The page shows exactly what goes to the image model: garment, fabrics, the colorway's colours, visible trims (zippers, drawcords, heat transfers...). Labels, packaging, thread and brand names are left out.
- Fabric descriptions are put in plain words first: yarn specs are dropped and factory shorthand is spelled out (`100%Recycle PA` becomes *100% recycled polyamide*, `rec. pes` *recycled polyester*, `ea` *elastane*). The [design prompt](styles.md#design-prompt) uses the same wording.
- Edit the description, or **Rewrite with AI** (Text job). Tick **Use the sketch** to guide the silhouette (Gemini only).
- **Generate render** (takes up to a minute). The picture is saved on the LT server (`style-library` folder, [File storage](../installation/08-file-storage.md)), not at the AI provider. Renders are marked **AI**, kept apart from real photos, and can be set as the style photo (**Use as style photo**) or deleted.
- Renders are visualisations, not photos of a sample: colours, trims and proportions may differ.

## Proven styles in the library (in Concept Studio)

See [Concept Studio](concept-studio.md#proven-styles-in-the-library).

## AI Lab

See [AI Lab](ai-lab.md).
