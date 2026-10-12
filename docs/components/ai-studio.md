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
- **Styles that look like this** (panel under the results): the same request is searched by picture with [Image search](#image-search-aiimage-search)'s built-in model (`POST /api/v1/ai/image-search/words`, about 30 ms, nothing sent anywhere), limited to the customer the phrase list read from the request. It finds styles whose *pictures* match, even when their data does not use your words. Up to 6 are shown; **Open in Image search** (`/ai/image-search?q=...`) shows them all. The panel appears only when the model and its text part are installed, at least one picture is indexed, and the best match is close; it never asks the AI server. Each search counts once more towards the per-user AI limit.

## Image search (`/ai/image-search`)

Find library styles that look like a photo, a description, or another style. **Everything runs inside LT**: library pictures never go to a cloud service.

**How it works.** Every picture (style photo, sketch, colorway picture, AI render) gets two vectors, kept in two indexes:

| Index | Made by | Used for | Speed (CPU server) |
|---|---|---|---|
| **Look-alike** (`style.ImageVector`, `vector(768)`) | The image model **built into the API** (Marqo-FashionSigLIP, trained on fashion product photos): pixels straight to 768 numbers; its text part puts typed words in the same space. No AI server, nothing sent | Search by photo, by words, More like this | About 0.1-0.3 s per picture; words about 30 ms |
| **Descriptions** (`style.ImageIndex`, `vector(1024)`) | The **Vision** job writes a garment description (garment type, main colours, visible details such as hood, zips, panels, stripes and logo *placement*; brand names and logo text are never written down; fabric look; two sentences), and the **Embedding** job turns it into 1024 numbers. Both must point at an OpenAI-compatible in-house server (Ollama, vLLM, LM Studio); a Gemini connection is refused | Searches with **Use the AI server** ticked (or when the built-in model is not installed) | 1-2 minutes per picture |

SQL Server returns the styles whose pictures are closest (cosine similarity, best picture per style, up to 24). The look-alike model must be installed on the web server ([File storage](../installation/08-file-storage.md#image-search-model-files)); without it (or without its text part, for words), searches fall back to the descriptions.

**Use the AI server** (tickbox under the search). Off by default: searches run on the built-in model. Ticked, the photo or words are matched against the written descriptions instead (the vision model reads the photo; the Embedding job embeds the words), which can catch details the look misses but is much slower on a CPU server. The tickbox is only enabled when both ways can run; otherwise its hint says which one is used.

**Picture index** (top card). One line per index with its progress, what it is for, and why it cannot run (model not installed, jobs not set, AI switched off). **Index N pictures** (Admin, Merchandiser) does the look-alike vectors first (all pictures in seconds), then the descriptions, in rounds of about 45 seconds, until all are done or **Stop** is pressed; leaving the page also stops it. Work already done is kept if something fails half-way.

- New, replaced and removed pictures are picked up the next time someone indexes. A style copied to another season shares its picture files, so it is indexed without another model call.
- Changing the **vision model** means every description is written again. Changing only the **embedding model** re-uses the stored descriptions (embedding only, much faster). Changing the look-alike model's file or name re-does the look-alike vectors (seconds). Vectors of different models are never compared.
- A picture whose **file is missing** on the server (e.g. styles copied from another server before their picture files) is not a failure: it stays to do, is tried again at every index, and the page says how many files are missing. Copy the files into `<Files:Root>\style-library` and index again.
- A file that is not a picture, or that the model cannot describe, counts as *could not be read* and is not retried until the picture or a model changes.
- Only pictures stored by LT ODM are indexed (outside `https://` addresses from imports are not fetched).

**Searching** (everyone who can read the Style Library):

| Search | Uses | Notes |
|---|---|---|
| **By photo** | Look-alike model (about a second); without it, Vision 1 + Embedding 1 | Drop or choose a JPEG, PNG, GIF or WebP up to 10 MB (e.g. a competitor sample or a buyer's reference). Read inside LT and **not stored**. When the descriptions are used, the page shows how the photo was read (garment, colours, details) |
| **By words** | Look-alike model's text part (about 30 ms); with the tickbox or without it, Embedding 1 | e.g. "navy hooded track jacket with white side stripes" (3 to 300 characters). English works best (the model's vocabulary is English). Matched by look, results are in order but show **no percentage**: word-to-picture scores are low even for right matches. When even the best is far, the page says **no close match** (threshold `ImageSearchRules.WeakWordMatch`, 0.03, set on 8 pictures; to revisit as the library grows) |
| **More like this** | Stored vectors only | On any result, or open `/ai/image-search?styleId=<id>`. Look-alike vector of the style (photo first, then colorway, sketch, render), else its description vector |

A line above the results says how they were matched: by how the pictures look, or by their descriptions.

The **customer** filter limits results to one brand. Results of other brands are for know-how (construction, materials, costs): pitch a brand only its own designs or new LT designs, never another brand's style (the page repeats this).

Each result shows the matched picture with its source, the similarity, style number, customer, season and model, and the garment description; the picture and number open the style. Similarity is relative: compare results with each other rather than reading the percentage as a probability.

**Speed** (measured on a laptop CPU, i7-1270P). Look-alike: about 0.1 s per picture, all 8 library pictures indexed in about a second, search by photo about 0.1 s (the first call after a restart also loads the model, about a second). Descriptions with `qwen2.5vl:7b` on Ollama: about 3-5 minutes per picture; search by words about a second. Set the AI connection's timeout to 300 seconds or more for a CPU server ([in-house server](../ai/in-house-server.md)).

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
