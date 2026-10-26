# AI Studio

**Where:** AI Studio menu group (`/ai/...`). **Who:** Admin, Merchandiser, Costing, Viewer; renders and render prompt rewrite: Admin, Merchandiser. **AI service:** the Text and Image jobs in Settings > AI connections. Every page shows whether data leaves LT (amber banner, cloud), stays (green, in-house), or is blocked by a switch in Settings > AI connections (red). Style render uses both jobs: one line per job, merged into one when both are blocked for the same reason. What each feature sends: [Data sent to AI services](../ai/data-sent.md).

## Smart search (`/ai/search`)

Describe the styles you need in your own words, e.g. *"men's woven jackets for 2027 SS with recycled fabric"*.

**Three steps, fastest first: phrase list (SQL), then the pictures (built-in image model), then AI.** The request is first read by a phrase list (`StyleQueryParser`), with no AI call:

| Criterion | Words understood | Example |
|---|---|---|
| Gender | men, men's, women, ladies, kids, youth, boys, girls, unisex | *women's* → FEMALE |
| Season | SS27, FW26, 2027 SS, 2027-SS, a year (every season of it), spring, summer, fall/winter (the library's seasons with that term) | *SS27* → 2027-SS |
| Product type | Any product type name in Settings > Reference lists, singular or plural, matched on the end of the name; plus tee, hoodie, sweatshirt (also the HOODED SWEAT types), sweater, leggings | *jackets* → JACKET, JACKETS, JACKET (MIDWEIGHT), TRACKSUIT JACKET |
| Detail | long sleeve, short sleeve, sleeveless, midweight, 1/2, 3/4, 7/8 (narrows the product types) | *long sleeve t-shirts* → T-SHIRT (LONG SLEEVE) |
| Weave | knit, woven | |
| Business unit | Code, full name, or the name's first word; a code that is also a filler word (TO) only by its name (*tomwell*) | *running* → RUA, RUB, RUX |
| Customer | Code or name; a longer word starting with the code | *adidas* → ADI |
| Material | recycled, fleece, polyester, elastane, zipper, drawcord, ... (several words must all be on one BOM line) | *recycled fleece* |
| Look (pictures) | hooded, full zip, half zip, striped, printed, graphic, solid, colour names ([picture tags](#picture-tags-look-filters)); only styles with tagged pictures | *navy hooded jackets* |
| Style number | A word with letters and digits | *S2808MR0000* |

Fillers (for, with, that, styles, ...) are skipped. Words that change the meaning (*without*, *not*) and two different values for one criterion (*men and women*) are left to AI.

- **Every word understood**: the library is searched at once, marked *Understood with the phrase list. No AI was used.* Nothing is sent anywhere. If **no style** meets all of it (often a product type filed under another name), the pictures are searched next as below, without the product types as a filter, and the page says so.
- **Some words not understood, but one picture clearly matches**: the request is compared with every style picture by the image model built into the API (Marqo-FashionSigLIP, milliseconds, nothing sent; `LookSearch`). Its matches are used only when the best picture **stands out**: at least 2.8 standard deviations above the average picture (`style.usp_ImageVectorStats`) and a similarity of at least 0.05, with at least 20 pictures indexed. The model always returns a nearest picture, even for "styles from vendor ABC", so the raw score cannot tell a request about looks from one that is not; how far the best picture stands out can. Measured on 139 pictures (October 2026): requests about looks stood out 2.9-3.9 with similarity 0.09-0.13 (*navy hooded sweatshirt with a print*, *black running shorts with white side stripes*, *sleeveless vest with full zip*); others 2.6 or less, or below 0.03 (*styles with recycled polyester for ADI*, *styles from vendor ABC*, *which styles changed consumption last season*, *jackets with contrast zips*, where the nearest picture was a dress). The gap is small: the thresholds (`LookSearch`) must be checked again as more pictures are indexed. The styles shown are those standing out (2 or more), kept to the firm criteria the phrase list read (customer, season, product type, business unit, gender, weave; not material or look words, which describe the appearance searched for). Marked *Matched by how the pictures look. No AI server was used.*, with **Search with AI instead** (skips this step) and **Open in Image search**. Only styles with pictures can be found this way.
- **Some words not understood, no picture stands out**: the request goes to AI (Text job), when AI can run. If it cannot (not set up, turned off, cloud not allowed), the words understood are searched and the rest are listed as ignored. If the AI is asked but fails (e.g. it takes longer than the connection's timeout), the page says so with the reason and shows the phrase list's result.
- **The phrase list stays in charge of what it understood.** The AI's reading is merged with it (`StyleAiAssistant.KeepPhraseList`): criteria the phrase list read (e.g. *SS27*) are kept as read, and **gender, weave and picture looks come only from the phrase list** (it knows every word for them, so an AI value there was not in the request). The AI adds product types, customer, business unit, material, keywords and seasons the phrase list left open; e.g. *"windbreakers for SS27"* becomes SS27 + jacket types. Small in-house models need this: tested with `qwen2.5-7b` (October 2026), the AI alone dropped the season and added "unisex" and "knit", finding nothing. If the merged criteria still find no style while the phrase list's own do, the phrase list's result is shown with *"Read as: ..."* and a note that the AI's reading found nothing.
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
| **Descriptions** (`style.ImageIndex`, `vector(1024)`), **optional** | Not needed for search: the built-in model already searches by photo and by words, and Smart search and the Library assistant use it. Kept for a GPU AI server later (readable descriptions, a second way to search words); until then leave the Vision job unset (the page shows the row as optional, without a warning). When set, the **Vision** job writes a garment description (garment type, main colours, visible details such as hood, zips, panels, stripes and logo *placement*; brand names and logo text are never written down; fabric look; two sentences), and the **Embedding** job turns it into 1024 numbers. Both must point at an OpenAI-compatible in-house server (Ollama, vLLM, LM Studio); a Gemini connection is refused | Searches with **Use the AI server** ticked (or when the built-in model is not installed) | 1-2 minutes per picture |

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

### Picture tags (look filters)

After the look-alike vectors, **Index pictures** also tags each picture with what it looks like, using the same built-in model (Marqo-FashionSigLIP): each picture's vector is compared with short descriptions of every tag value ("a hooded top", "a navy blue garment"), and a value is kept only when it clearly leads the next one of the same kind. No AI server, milliseconds per picture.

| Tag (`attribute:value`) | Values | Words the phrase list reads |
|---|---|---|
| `hood` (tops only) | hooded, noHood | hooded, hood |
| `closure` (tops only) | fullZip, halfZip, buttons, noZip | full zip, zip-up, half zip, quarter zip |
| `pattern` | solid, striped, print, colourBlock | solid, plain, striped, stripes, printed, graphic, print, colour-block |
| `colour` | black, white, grey, navy, blue, red, green, yellow, pink, beige (two when they are equally close: navy and blue) | the colour names (gray, olive, khaki, tan too) |

- Tags are searched by **Smart search**, the **Styles list** (`looks` filter, shown as a *Looks (pictures)* chip) and the **Library assistant**: *"navy hooded jackets with a full zip"*, *"black printed t-shirts"*. A style needs all the tags asked for, on any of its current pictures. **Styles without pictures never match a look**, so a look narrows the search to pictured styles.
- Bottoms (trousers, shorts, tights, skirts) get no hood or closure tags.
- Checked by eye on the 8 library pictures (October 2026): every tag stored was right; close calls (a hoodie as full zip by 0.006) were left untagged. The thresholds (`PictureTagVocabulary`) should be checked again with more pictures; changing the vocabulary or thresholds means changing its `Version`, and every picture is tagged again on the next **Index pictures**.
- Tags are stored in `style.ImageTag`; a picture whose vector changes is tagged again.

## Library assistant (`/ai/assistant`)

Questions about the library in your own words, answered from the database: phrase list first, then the Text job picks one of 12 read-only look-ups and words the answer from its rows. See [Library assistant](library-assistant.md).

## Change summary (`/ai/compare?styleId=...`)

What changed between a style and the one it was reused from (or any style you pick).

- Worked out by rules, without AI: header fields; colorways (added, removed, renamed, re-coded: same colour under a new article number); BOM lines paired by material and part. *Swapped* = same section and part with another material. Each change shows old → new values.
- **Summarize with AI** writes a headline, a short summary, highlights by area, and things to check before quoting. Only the differences are sent: at most 60 BOM changes (`SummaryBomLines`; the rest are only counted), and the answer is capped at 900 tokens (`SummaryMaxTokens`). On an in-house CPU server reading the differences takes most of the time: about 1-3 minutes for a style with dozens of BOM changes. The summary card is hidden when no Text job is set up; the differences still show.
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

**Explain with AI** sends the 40 most severe findings (`BomCheckRules.MaxExplained`; the rest are only counted), asks for at most 1,500 tokens (`ExplainMaxTokens`) and returns a summary, up to 10 findings to fix first (numbered, shown first) and a note per rule. The findings themselves always come from the rules.

## Style render (`/ai/render?styleId=...`)

A product picture drawn from the style's data.

- The page shows exactly what goes to the image model: garment, fabrics, the colorway's colours, visible trims (zippers, drawcords, heat transfers...). Labels, packaging, thread and brand names are left out.
- Fabric descriptions are put in plain words first: yarn specs are dropped and factory shorthand is spelled out (`100%Recycle PA` becomes *100% recycled polyamide*, `rec. pes` *recycled polyester*, `ea` *elastane*). The [design prompt](styles.md#design-prompt) uses the same wording.
- Edit the description, or **Rewrite with AI** (Text job). Tick **Use the sketch** to guide the silhouette: Gemini, and in-house servers with `images/edits` (OpenVINO Model Server with FLUX.1-schnell: the sketch is redrawn as a photo; slow without a GPU, see [OpenVINO image model](../ai/in-house-server.md#openvino-image-model-renders)). The default description and **Rewrite with AI** are written from the style's BOM: fabrics, colours and trims.
- **Generate render** (up to a minute with a cloud service; many minutes on an in-house server without a GPU; see [Slow AI jobs](#slow-ai-jobs-minimise)). The picture is saved on the LT server (`style-library` folder, [File storage](../installation/08-file-storage.md)), not at the AI provider. Renders are marked **AI**, kept apart from real photos, and can be set as the style photo (**Use as style photo**) or deleted.
- **Shape from the sketch:** the BOM says nothing about sleeves, neckline or length, so the image model built into the API (the one image search uses; nothing is sent) reads them from the uploaded sketch: garment type, sleeves (long / short / none), neckline (high, hood, crew, V-neck, shirt collar), leg length for bottoms, length for dresses and skirts, the kind of print (floral, stripes, checks, animal; read on 81 library sketches, camouflage, tie-dye, geometric and graphic were too unreliable to use, and plain garments get no print words), and whether the sketch has front and back drawn apart (counted from the pixels). The print is only its kind: for a closer result describe its colours and motifs in the description ("light blue with large blue rose flowers and wavy vertical vine trails"). A value is used only when the model is clearly sure (`SketchShapeReader`, margins set on 81 library sketches). Reading takes about 0.6 s per sketch on a laptop (then kept until the API restarts); the model and its descriptions are loaded in the background when the API starts, so the first render page does not wait the 3-4 s that takes. While an in-house server renders on the same machine, every page that uses the CPU is slower. The page shows it as **Shape and print**; it goes into the default description ("a women's dress with long sleeves, a high neck and maxi length") and into **Rewrite with AI**; when it names the sleeves, a sleeve note in the product type's name ("jersey (sleeveless)") is left out of the description. Check it: a few odd sketches read wrongly.
- **In-house image server (image-to-image):** the model redraws the sketch and reads no instructions, so the description does not mention the sketch and asks for a **flat lay**: "a real garment, not a drawing, laid flat on a plain light-grey surface, seen from directly above". On mannequins FLUX kept adding heads (it does not follow "headless" or other "no" words), and with front and back in one picture it merged them or drew one view twice. So a sketch with **front and back** drawn apart is cut into its two drawings, each is rendered on its own ("Front side of the garment." / "Back side of the garment, seen from behind." added to the description), and the two are joined side by side: two pictures, about twice the time (the page says so under **Use the sketch**, and the estimate counts both). Gemini is still told to follow the attached sketch, on a ghost mannequin.
- **The selected colorway** gives the colours (its BOM lines), so a zebra colorway asks for a zebra print on the sketch's shape. The **main fabric's** colour (the fabric line with the largest consumption) comes first, marked *(main)* when other fabrics add colours: *Colours: rich blue (main), black*. When the BOM gives the main fabric only a colour code and no colour name (an adidas shell with `AJ9Q`), the colorway's name stands in for it, without season and colour codes (*RICH BLUE F27* → *rich blue*); before this, a lining colour could take over the render.
- **Colorway picture first:** when the selected colorway has its own uploaded picture, that picture is sent instead of the style sketch (and the shape and front/back views are read from it). It is already cut to that colorway and shows its colours and print, whereas a sketch with several colorways side by side could have its drawings mixed up. The hint under **Use the sketch** says which picture is sent.
- **Small renders are enlarged:** a render under 600 px (an in-house CPU server draws 256x256, far faster than 1024) is enlarged 4x by the upscaler of Settings > Import pictures (Real-ESRGAN, inside the API, about 1.3 s) before it is saved.
- **Use this description** (on each saved render and in its preview) puts that render's description back in the box: descriptions you type or rewrite are not kept between visits, but every render keeps the one it was made with.
- Renders are visualisations, not photos of a sample: colours, trims and proportions may differ.

## Slow AI jobs: Minimise

Style render, **Rewrite with AI** (render description), the BOM check's **Explain with AI** and the change summary's **Summarize with AI** can take minutes on an in-house server. They run in the browser tab's AI task list (`ai-tasks.session.ts`), not in the page, so the user can keep working:

- While a job runs, its page shows how far it has got, with **Minimise** (back to the previous page) and **Cancel**:
  - **Text jobs** (rewrite, summary, explanation): *Reading the request…* until the first words arrive, then *Writing… N words so far* (words streamed by an in-house server, field names included; a sign of life, not a percentage). Cloud services show the minutes only.
  - **Renders**: an estimated bar, *About N min left*, from the median of the last 5 renders with the same image model, with or without the sketch (`RenderTimings`, memory only: none after the API restarts). Past the usual time: *Taking longer than usual*. Image servers report no progress, so the bar never reaches 100% before the picture arrives. Leaving the page any other way does the same as Minimise.
- Away from the page, a chip in the bottom-right corner (beside the Library assistant bubble) shows each job: spinner with the estimated %, words written or minutes, then *Ready* or *Failed*. Clicking it opens the page with the answer; **X** cancels a running job or clears a finished one. A notice with **Open** also appears when a job finishes.
- Back on the page the answer is shown again: the last 10 answers are kept per style, style pair or BOM check (renders are saved on the server anyway). One job per style, pair or check at a time.
- They run on the server as [AI tasks](../ai/overview.md#ai-tasks-slow-ai-work-in-the-background): reloading the page, closing the tab, a dropped connection or signing out do not stop them. At the next sign-in (on any PC) the tray shows again the tasks still running and the answers not yet seen. **Cancel** (or **X** on a running chip) stops the task on the server; an OpenVINO server may still finish computing that picture for a while. While [AI call limits](../ai/overview.md#ai-call-limits-taking-turns-at-the-ai-server) are on, tasks take turns at the AI server (a second render waits for the first); the chip then shows *Waiting for the AI server: number N in line*.

## Proven styles in the library (in Concept Studio)

See [Concept Studio](concept-studio.md#proven-styles-in-the-library).

## AI Lab

See [AI Lab](ai-lab.md).
