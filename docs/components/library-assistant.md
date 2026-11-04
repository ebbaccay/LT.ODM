# Library assistant

**Where:** AI Studio > Library assistant (`/ai/assistant`). **Who:** Admin, Merchandiser, Costing, Viewer (the Style Library readers). **AI service:** the Text job in Settings > AI connections (for example qwen2.5:7b on the in-house Ollama server). What is sent: [Data sent to AI services](../ai/data-sent.md#ai-studio--library-assistant).

Ask questions about the library in your own words: *"How many jackets per season?"*, *"What fabric does S2808MR0000 use?"*, *"Who are our main suppliers for SKE?"*. Answers come from the database. Every answer shows how it was found, the rows behind it, and links to the styles, materials or screens. Questions about how to do something in LT ODM (*"How do I create a style package for ADI?"*) get a [how-to guide](#how-to-guides-fixed-processes): numbered steps with links to the screens.

## How a question is answered

```
question ──► 1. saved question (Rule) for the same question, or a question pattern it fits? ──► its look-up on today's data, no AI
                    │ no                                                                         (a how-to guide's question: its steps)
                    ▼
             1a. "how do I ...?" whose words clearly point to one how-to guide? ──► its steps, no AI
                    │ no
                    ▼
             1b. phrase list (StyleQueryParser) ── understood every word? ──► style search, no AI
                    │ no
                    ▼
             1c. "... look(s) like X"? ──► look_alike: X searched against the pictures by the built-in image model, no AI
                    │ no
                    ▼
             1d. one style picture clearly stands out for the question (same gate as Smart search)? ──► look-alikes, no AI
                 (also tried when the phrase list understood everything but found no style, without its product types)
                    │ no
                    ▼
             2. Text job picks ONE tool + its arguments (answer forced to a schema; tool names and codes are lists)
                    │  arguments checked: only library codes; dropped unless the question (or an earlier turn) supports them
                    ▼
             3. the tool runs its read-only stored procedures ──► rows + a sentence written by code
                    │
                    ▼
             4. Text job words a 1-3 sentence answer from the rows ── every number must be in the rows and
                                                                       the answer short and about the library,
                                                                       else the code's sentence is shown
```

- **The AI never writes SQL** and no tool changes data. The AI can only choose among the tools below and fill their arguments.
- **Arguments are checked twice.** Codes not on the library's lists are dropped. A customer, season, product type, business unit, gender, weave, material or style number that the question does not mention (and an earlier turn did not use) is dropped too, because a small model sometimes adds filters nobody asked for.
- **What the model leaves out, the phrase list fills in.** If the phrase list read a criterion from the same question ("jackets", "SKE", "SS27") and the model left it out, it is added for the tools that take it. (Seen in testing: qwen2.5:7b answered "main suppliers for SKE" with every customer's suppliers.)
- **Follow-ups work**: the screen sends the last 4 questions with their tool and arguments, so *"and only for women?"* keeps the previous search. A follow-up (a question starting with *and*, *also*, *only*, *what about* ...) always goes to the AI when it can run, so the earlier turn is taken into account. Nothing is stored on the server; **New conversation** clears the page.
- **Without AI** (not set up, turned off, or cloud not allowed for a cloud connection): phrase-list questions still work; anything else says that AI is not available.
- Questions outside the tools (*"what's the weather?"*, *"change this BOM"*) get "I can only look things up in the style library" with examples. The assistant never changes data: a how-to guide links to the screens, and the user does the steps.

## Saved questions: common questions answered at once

Questions the AI answered are kept (table `ai.SavedQuestion`) with the **look-up and filters** the AI chose, never the answer. When the same question is asked again, a saved **Rule** runs that look-up on today's data straight away: no AI call, answer in under a second, and it also works while AI is off.

- **Same question** = same text after normalising: case, punctuation, possessives and plurals are ignored (*"Men's jackets per season?"* = *"mens jacket per season"*). Different words are a different question (*men's* ≠ *women's*).
- **Thumbs up** under an AI answer (*Right*) makes it a Rule, with exactly the look-up and filters shown with the answer. **Thumbs down** (*Wrong*) on any answer, including one from a Rule, removes it: the AI answers it next time (*Ask the AI now* asks again at once).
- **Never saved:** follow-ups (*"and for women?"*, which depend on the earlier question) and questions with relative dates (*this year, next season, latest*), whose meaning changes.
- The most used Rules are shown as **Answered at once** suggestions on the empty page (question patterns only once someone has asked them, and never one written with `{styleNo}`).
- No user names are stored with questions; only who confirmed or removed a Rule (*rated by* / *changed by*).
- Saved questions are looked up **before** the phrase list, so a pattern such as *show {styleNo}* gives the style's details instead of a one-row style search.

### Question patterns (seeded)

The app comes with **137 question patterns** for 11 look-ups (`db/seed/ai.saved-questions.sql`, reviewed 2026-10-22), plus **70 wordings** that open the 12 [how-to guides](#how-to-guides-fixed-processes) (tool `how_to`, added 2026-11-04): different wordings of the common questions, such as *main suppliers*, *who are our main suppliers*, *how many styles per season*, *BOM problems*, *what fabric does {styleNo} use*. *Find styles* and *Styles that look like…* are not seeded: the phrase list and the "looks like" rule already answer them without AI.

A pattern is a question with its **filter words taken out**, so one pattern answers every customer and season:

1. **Codes first.** Words as typed with a digit and 5+ characters (not a season or a year) are checked against the library: a style number (exact, or the only match) fills `{styleNo}`, a second one `{fromStyleNo}`, a material code (exact) `{materialCode}`. This happens before the phrase list, which would split *26FA_M_JK01* and read *26fa* as a season. Codes the library does not have stay ordinary words.
2. **Filters out.** The words the phrase list reads as a customer, season, product type, gender, business unit or weave are taken out, with the words that join them (*for, in, of, on, from, at, with, by, our, we, do, the*). The rest, normalised as above, is the pattern: *"Who are our main suppliers for ADI in 2027-SS?"* → *who are main supplier*.
3. **Product type words** are also tried as ordinary words (*top suppliers*: TOP is a product type too), when the first reading finds no pattern.
4. **The look-up runs** with the pattern's own arguments (slots filled in) and the filters taken out, but only those it takes. A filter it does not take means a different question, so the AI answers it: *main suppliers for women* (suppliers have no gender). Counting *by* a dimension ignores a filter on it: *men vs women styles* counts every gender.

Material words (*recycled, zipper*) and picture looks (*navy, hooded*) stay part of the pattern: *how much recycled fabric do we use* is its own pattern. A pattern is listed with a **Pattern** badge on the Admin page. A thumbs down removes it (it then goes to the AI); a thumbs up keeps its own arguments, not the customer or season of that one question. The seed adds only patterns that are missing, so one an Admin removed or changed stays as it is.

**Adding or changing a pattern:** the `QuestionKey` must be the key `QuestionPatterns` computes for the wording (typed with sample codes, `jackets` for `{productType}`). `LibraryAssistantSeedTests` checks every row, its tool and its arguments.

**Admins: AI Studio > Library assistant > Saved questions.** Each question with its look-up, filters, status (*Rule (instant)*, *Recorded (AI)*, *Removed*), how often the AI answered it, how often it was answered at once, and who rated or changed it. Admins can make or remove a Rule, **correct the filters** (codes not on the library's lists are dropped), and delete a question.

## How-to guides: fixed processes

Some questions are not about data but about how to do something, and the answer is always the same process: *"How do I create a style package?"*, *"How do I share a package with a client?"*. These are written once as **how-to guides** (table `ai.Guide`): a title, who can do it, a short introduction, and numbered **steps**, each with a link to the screen where it is done. The answer shows the steps as a table (a row with a link opens its screen) and a **Start** button for the first link. The source badge says *How-to guide, no AI* (or *Saved question, no AI* when one of its questions matched).

**How a guide is found** (no AI in either case):

1. **Its questions.** Each guide has wordings that open it at once (*how do I create a package*, *new package*, *how do I make a pitch*). They are saved question patterns (tool `how_to`, argument `guide` = the guide's code), so customer and season words are taken out first: *"How do I create a package for ADI in 2027-SS?"* fits *how do I create a package*.
2. **Its words.** A question that starts like a how-to (*how do I / how can we / how to / steps to / what is the process / where do I / I want to ...*) but fits no wording is compared with every active guide's title and questions. Words that mean the same are read alike (*make, start, set up* = create; *pitch, proposal, collection* = package; *send, link* = share; *client, brand, buyer* = customer; *copy, carry over* = reuse). The guide wins when it shares more words with the question than any other guide, and at least half of the question's words. *"What are the steps to share a package with my client?"* → *Share a package with a client*.
3. Otherwise the question goes to the AI as before. The AI can also pick the `how_to` look-up; the guide it names is matched the same way, its steps are shown **as written** (never reworded by the AI), and a thumbs up makes that wording a Rule for the guide.

**Customer and season fill the links.** A step's link can hold `{customer}` and `{season}`, filled from the customer and season in the question (the phrase list reads them); a link part left empty is dropped. *"How do I create a package for ADI SS27?"* → step 1 opens `/packages?new=1&customer=ADI&season=2027-SS`: the Packages list opens **New package** with ADI and 2027-SS filled in (`?new=1` works for Admins and Merchandisers; the address then keeps only the customer filter).

**Seeded guides** (`db/seed/ai.guides.sql`, 2026-11-04; the steps follow the screens' documentation):

| Code | Guide | Who |
|---|---|---|
| `create_package` | Create a style package | Admin, Merchandiser |
| `add_styles_to_package` | Add styles to a package | Admin, Merchandiser |
| `share_package` | Share a package with a client | Admin, Merchandiser |
| `client_profile` | Set up a client profile | Admin, Merchandiser |
| `what_to_offer` | Find what to offer a client | Everyone; adding: Admin, Merchandiser |
| `create_concept` | Work up a concept (LT design idea) | Admin, Merchandiser |
| `reuse_style` | Reuse a style in another season | Admin, Merchandiser |
| `new_style` | Add a style | Admin, Merchandiser |
| `compare_styles` | Compare two styles | Everyone |
| `import_styles` | Import styles from Excel | Admin |
| `bom_check` | Check BOMs before costing | Everyone |
| `image_search` | Find styles by a picture | Everyone |

*Who* is shown with the guide; every reader can see every guide (the screens themselves check the roles). The seed adds only guides that are missing, so one an Admin changed stays as it is. **When a screen changes, change its guide too** (Admin page, or the seed for new installations).

**Admins: AI Studio > Library assistant > How-to guides.** Each guide with its steps, links and questions. **New guide** / **Edit**:

- **Code** (new guides only, e.g. `send_sample`: lower-case letters, digits, `_`), **title**, **who can do it**, **introduction**.
- **Steps**, one per line; to link a step, end it with ` | ` and the address: `Click New package. | /packages?new=1&customer={customer}&season={season}`. Only addresses inside LT ODM (starting with `/`) are allowed; 1-20 steps.
- **Questions that open it**, one wording per line (up to 30). Customer and season words are taken out, so one wording answers every customer. A wording that another look-up already answers as a Rule is refused (change that saved question first); one that was only recorded, removed, or belonged to another guide moves to this guide. Teaching the assistant a new phrasing of a process = adding a line here.
- **Active**: an inactive guide is not offered (its questions then go to the AI).
- **Delete** removes the guide and its questions.

## Chat bubble, minimising and cancelling

The conversation belongs to the browser tab, not to the page:

- **Leaving the page** (or **Minimize** on the page, which goes back to the previous page) keeps the conversation, and a running question keeps running. A **chat bubble** appears in the bottom-right corner of every page: a turning ring while a question runs, and a red count of answers that arrived while it was closed.
- **Clicking the bubble** opens a small chat window over the current page (full width at the bottom on a phone): read the answers (5 rows each, *Show all* for more), ask more, Cancel, thumbs up / down. Following a link in an answer minimises the window. **Minimize** (–, or Esc) folds it back into the bubble; **Open full page** goes back to AI Studio > Library assistant; **End conversation** (✕) cancels and clears it.
- **Cancel**, **New conversation**, **End conversation** and signing out stop a running question: the browser drops the request, the API cancels its call to the AI server, and Ollama stops generating (its log shows *aborting completion request due to client closing the connection*). Without this, a CPU server would keep working for minutes on an answer nobody sees.
- The conversation is kept in the browser's memory only: a reload, closing the tab or signing out ends it. Nothing of it is stored on the server (saved questions are separate, above).

## What it can look up (tools)

| Tool | Answers | Reads (procedures) | Arguments |
|---|---|---|---|
| `style_search` Find styles | which styles / do we have / how many (a total) | `style.usp_Style_List` | customer, seasons, product types, business units, gender, weave, material, looks (picture tags), keywords |
| `style_counts` Count styles | how many per season / customer / product type / business unit / gender / weave | `style.usp_Style_Counts` | the same + groupBy |
| `style_detail` Style details and BOM | what fabric, trims, suppliers a style uses | `style.usp_Style_List`, `style.usp_Style_Get` | styleNo |
| `compare_styles` Compare two styles | what changed between two styles (or since the style it was reused from) | `style.usp_Style_Get` ×2 (Change summary rules) | styleNo, fromStyleNo |
| `library_totals` Library totals | how big is the library; styles per season and customer | `style.usp_Dashboard_Get` | - |
| `material_lookup` Materials and where used | which styles use a material, its suppliers and consumption; materials matching words | `mat.usp_Material_List`, `mat.usp_Material_Get` | materialCode or material, customer, season |
| `supplier_share` Main suppliers | top suppliers, single-source materials | `mat.usp_Material_Suppliers` | customer, season |
| `recycled_share` Recycled fabric | recycled share per season and product type | `mat.usp_Material_Recycled` | customer, season |
| `standard_trims` Standard trims | the trims most styles of a product type share | `mat.usp_Material_StandardTrims` | product type, customer, season |
| `duplicate_materials` Duplicate materials | materials that are probably the same | `mat.usp_Material_Duplicates` | customer, season |
| `colour_usage` Colours used | most used material colours | `mat.usp_Material_Colours` | customer, season |
| `bom_check` BOM check | BOM problems for a style or a customer / season | `style.usp_BomCheck_Run` | styleNo, customer, season |
| `how_to` How-to guides | how do I / how to / steps to do something in LT ODM (not data) | `ai.usp_Guide_List` | guide (words or code), customer, seasons (fill the links) |
| `look_alike` Styles that look like… | styles whose pictures look like a description or like another style | the built-in image model (Marqo-FashionSigLIP) + `style.usp_ImageVectorSearch`; no AI server | look, styleNo, customer |

The Materials views take one season: when a question names several, the first is used.

Answers about styles show a **thumbnail** per row when there is a picture: the matched picture (*Styles that look like…*), the style photo or else its sketch (*Find styles*), and BOM line pictures (*Style details and BOM*).

## Speed

Each AI question makes two calls to the Text job (pick the tool, word the answer); phrase-list questions make none and answer at once.

| AI server | Time per AI question (qwen2.5:7b) |
|---|---|
| Laptop CPU (tested: MX550 2 GB GPU, model runs on the CPU) | 15 seconds to 2.5 minutes (picking the tool 12-40 s once the instructions are cached, wording 15-130 s depending on the rows); usable for trying it out |
| GPU server (16 GB or more, e.g. RTX 4000 Ada / L4) | a few seconds |

On a CPU server, raise the connection's timeout to 300 seconds or more. Ollama's default context window (4096 tokens) is enough for today's prompts; set `OLLAMA_CONTEXT_LENGTH=8192` on the AI server once the library has many more product types or tools.

## Adding a capability (costing, workmanship, ...)

The assistant's abilities are its tools. When new data arrives (costing, workmanship/SMV, quotations, lead times), adding it to the assistant is a code change of a few files; the AI's instructions are generated from the list of tools, so nothing else changes:

1. **Data and a read-only procedure.** The tables for the new data, and a stored procedure that returns what a question needs (e.g. `cost.usp_StyleCost_Summary @StyleId`, or cost per product type and season). Most of the time this procedure already exists for the new screen (the Costing or Workmanship page): reuse it.
2. **A tool class** in `src/api/LT.ODM.Application/LibraryAssistant/Tools/` implementing `ILibraryTool`:
   - `Name` (snake_case, e.g. `style_cost`), `Description` (which questions it answers, in plain words: the AI chooses tools by this text), `Example` (a question for the screen), `Parameters` (argument names).
   - `RunAsync` calls the repository and returns `LibraryToolResult`: a sentence written by code (`Facts`), a table, links, and `ForModel` (only the fields the AI needs to word the answer).
3. **New kinds of arguments** (e.g. a cost range, an operation name): add them to `LibraryToolArgs` (property, `Catalog` line, schema, `Read`, and the grounding check in `Ground`).
4. **Register** the tool in `src/api/LT.ODM.Infrastructure/DependencyInjection.cs` (`services.AddScoped<ILibraryTool, StyleCostTool>()`).
5. **Screen texts**: the tool's title (`ai.assistant.tool.<name>`) and any new column (`ai.assistant.col.*`) or link (`ai.assistant.link.*`) keys in `en.json` and `zh-Hans.json`.
6. **Tests**: a tool test with the fake repositories (see `tests/LT.ODM.Api.Tests/LibraryAssistantTests.cs`); the existing test checks every tool's arguments are in the catalog.
7. **Documentation** (required): this page's tool table, and [Data sent to AI services](../ai/data-sent.md) with what `ForModel` holds.

**Before adding cost data, decide who may see it.** Today every Style Library reader can use the assistant. Costs (FOB, CM, margins) are more sensitive: either give the tool its own role check (only Costing and Admin get the cost tool; `RunAsync` receives the user's roles through a small extension of `LibraryToolArgs`), or keep cost questions on the Costing screens. Cost data sent to a **cloud** Text job leaves LT: keep **Allow cloud AI services** off and use the in-house server for these tools.

Good candidates once the data exists:

| Data | Tool idea | Example question |
|---|---|---|
| Costing (FOB, fabric cost, CM) | `style_cost`, `cost_by_group` | "What drives the FOB of S2808MR0000?", "Average FOB of jackets per season" |
| Workmanship (operations, SMV) | `style_operations`, `smv_by_group` | "Which jackets have the highest SMV?", "Operations on S2808MR0000" |
| Quotation history | `quote_history` | "What did we quote ADI for this jacket last season?" |
| Lead times | `lead_time_stats` | "Average lead time for knit pants with recycled fabric" |

## Code map

| Piece | Location |
|---|---|
| Orchestration (phrase list, plan, grounding, answer check) | `src/api/LT.ODM.Application/LibraryAssistant/LibraryAssistantService.cs` |
| Tool contract, arguments, results | `LibraryAssistantContracts.cs`, `LibraryToolArgs.cs` |
| Tools | `LibraryAssistant/Tools/StyleTools.cs`, `MaterialTools.cs`, `LookTools.cs` |
| Endpoint | `LibraryAssistantController` (`GET /api/v1/ai/library-assistant/tools`, `POST /api/v1/ai/library-assistant/ask`, AI rate limit) |
| SQL | `db/procedures/style.assistant.procedures.sql` (`style.usp_Style_Counts`); `db/tables/ai.tables.sql` (`ai.SavedQuestion`) and `db/procedures/ai.assistant.procedures.sql` (saved questions); the rest are the Styles, Materials and BOM check procedures |
| Saved questions | `SavedQuestions.cs` (contracts), `QuestionPatterns.cs` (pattern keys, slots, arguments), seed `db/seed/ai.saved-questions.sql`, `src/api/LT.ODM.Infrastructure/StyleLibrary/SavedQuestionRepository.cs`; endpoints `GET popular`, `POST feedback` (readers), `GET/PUT/DELETE saved` (Admin) |
| How-to guides | `HowToGuides.cs` (contracts, how-to wording, matching by words, link filling), `Tools/HowToTool.cs`, `src/api/LT.ODM.Infrastructure/StyleLibrary/GuideRepository.cs`; table `ai.Guide` in `db/tables/ai.tables.sql`, procedures `ai.usp_Guide_List / _Save / _Delete` in `db/procedures/ai.assistant.procedures.sql`, seed `db/seed/ai.guides.sql` (plain ASCII) and the `how_to` rows of `db/seed/ai.saved-questions.sql`; endpoints `GET guides` (readers), `PUT/DELETE guides/{code}` (Admin); tests `HowToGuideTests.cs` |
| Screen | `src/web/ltodm-web/src/app/features/ai-studio/library-assistant.ts` (page), `library-assistant.session.ts` (the conversation, app-wide), `library-turn.ts` (one question and answer), `library-assistant-bubble.ts` (chat bubble, in the layout), `library-saved-questions.ts` (Admin list), `library-guides.ts` (Admin: how-to guides) |
