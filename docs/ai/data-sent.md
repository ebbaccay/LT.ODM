# Data sent to AI services

Every AI feature in LT ODM goes through one of two **jobs**: **Text** or **Image**. Settings > AI connections decides which service each job uses ([AI overview](overview.md)).

- While a job uses **Gemini**, everything listed below for that job is sent over HTTPS to Google (`generativelanguage.googleapis.com`), **outside the LT network**.
- While a job uses the **in-house AI server**, the same data goes only to that server and stays inside LT.
- Each AI page shows a banner saying which applies. The Concept Studio, Cost Optimization and Market Trends screens do not show the banner yet.

> **Google free tier.** With a free-tier Gemini key, Google may use the content you send to improve its products, and human reviewers may read it. With a paid key, Google's paid-service terms apply (content is not used to improve products). Check the current Gemini API terms before sending customer data. Style numbers, model names, BOM descriptions and costs of adidas and Skechers styles are customer information.

## What is never sent

- Users' names, user names, emails, roles and passwords; sign-in tokens.
- Who is using the feature. Google sees only the web server's address and LT's API key.
- Images, unless listed below (only the style sketch for renders).
- Anything the feature does not need: each feature sends a fixed set of fields, built on the server. The browser cannot add fields.

The API key goes in an HTTP header (`x-goog-api-key` for Gemini, `Authorization: Bearer` for in-house servers), never in the address. Prompts and answers are **not written to the logs**; failures are logged with the provider name and HTTP status only.

## Per feature

Sensitivity is LT's view of the data: **Low** (reference lists and generic text), **Medium** (customer style and material details), **High** (costs, prices and margins).

### AI Studio > Smart search

| | |
|---|---|
| Job / calls | Text, 1 per search |
| Sent | The request as typed; today's date; the library's code lists: customers (code and name), season codes, business units, product types, weave types (code and name) |
| Not sent | Any style data. The AI only fills filters; the results come from the database |
| Sensitivity | Low (the typed request may contain whatever the user writes) |

### AI Studio > Change summary and Styles > Compare styles ("Summarize with AI")

| | |
|---|---|
| Job / calls | Text, 1 per click |
| Sent | For both styles: style number, season, model name, colorway and BOM line counts; the relation (carry-over or variant); changed header fields (customer, description, model, product type, weave, gender, business unit, lead time) with old and new values; changed colorways (code, name, status); up to 120 changed BOM lines: section, part number, material code, description (first 120 characters), replaced material, and each changed field with old and new value (description, material type, LCO and brand consumption, unit, supplier name, nominated supplier name) |
| Not sent | Unchanged lines and colorways, images, material colours, costs or prices |
| Sensitivity | Medium (consumption figures and supplier names) |

### AI Studio > BOM check ("Explain with AI")

| | |
|---|---|
| Job / calls | Text, 1 per click |
| Sent | Number of styles, lines and findings; counts per rule; the first 80 findings (most severe first): rule, severity, style number, season, line number, material code, description (first 100 characters), section, unit, LCO and brand consumption, the compared value and median, number of peer styles, the earlier style number |
| Not sent | The rest of the BOM, suppliers, costs |
| Sensitivity | Medium |

### AI Studio > Style render

| | |
|---|---|
| Job / calls | **Rewrite with AI**: Text, 1 call. **Generate render**: Image, 1 call |
| Sent (Rewrite) | The facts shown on the page: garment type, gender, construction, model name, colorway code and name, up to 3 shortened fabric descriptions, colours, visible trims; whether a sketch exists |
| Sent (Generate) | The description as shown in the prompt box (brand names such as adidas, Skechers, Parley and Primegreen are removed before sending); **the style's uploaded sketch image** when "Use the sketch" is ticked and the image service is Gemini |
| Not sent | Photos, BOM figures, suppliers, costs |
| Sensitivity | Medium (the sketch is a customer design) |

### AI Studio (from Concept Studio) > Proven styles in the library

| | |
|---|---|
| Job / calls | Text, **2 per search** |
| Sent (call 1) | The concept: name, customer, season, target market, target FOB, trend tags, brief, suggested products, fabric direction, sustainability notes; the code lists: customers, product types, weave types, material types (code and name) |
| Sent (call 2) | The concept again; the criteria from call 1; for the 25 best-scoring library styles: style number, season, customer code, model name, description, product type, gender, weave, up to 3 main fabric descriptions (shortened), trim type names, matched keywords, colorway count |
| Not sent | Consumption, suppliers, costs and prices of library styles, images |
| Sensitivity | Medium (the target FOB is a price) |

### Concept Studio > Generate AI Concept

| | |
|---|---|
| Job / calls | Text, 1 per click |
| Sent | Concept name, customer, season, target market, target FOB, trend tags |
| Not sent | Inspiration images, saved concepts |
| Sensitivity | Low to medium |

### Cost Optimization > AI suggestions

| | |
|---|---|
| Job / calls | Text, 1 per click |
| Sent | Style name; **current FOB, target FOB and the gap**; **fabric cost and description, trim cost and description, labour, overhead and margin**; the BOM summary and CMT breakdown as shown on the screen |
| Not sent | Other styles, factory names beyond what the summaries contain |
| Sensitivity | **High**: costs and margins. Use a paid key or the in-house server |

### Market Trends > AI analysis

| | |
|---|---|
| Job / calls | Text, 1 per click |
| Sent | Collection name, season, target market, target FOB, active trend tags, fabric direction, sustainability notes; for each style in the collection: id, name, code, category, SBU, country, FOB price |
| Not sent | Costs behind the FOB, suppliers |
| Sensitivity | **High** (FOB prices per style) |

### Settings > Import > New codes ("Suggest with AI")

| | |
|---|---|
| Job / calls | Text, 1 per click |
| Sent | Up to 60 values the spelling rule could not match: which list, the value, the file's own name for it, row count; the existing codes and names of those lists (for suppliers: every SAP supplier code and name) |
| Not sent | The workbook rows, styles, BOM lines |
| Sensitivity | Low to medium (supplier list) |

### Materials > Description reader

| | |
|---|---|
| Job / calls | Text, 1 call per 25 materials ("Read all" on fabrics: about 29 calls) |
| Sent | For each material: id, material code, description (first 600 characters), section |
| Not sent | Which styles use it, consumption, suppliers, costs |
| Sensitivity | Medium (descriptions name the customer's materials and programmes) |

### Settings > AI connections ("Test connection")

| | |
|---|---|
| Calls | 1 request for the service's model list |
| Sent | The API key (header) only; no LT data |

### Styles > Design prompt (not an AI call)

The **Design prompt** dialog on a style page only builds text from the style's data ([Styles](../components/styles.md#design-prompt)). LT ODM sends nothing to any service. What reaches an outside design tool (StyTrix, Style3D, …) is whatever the user pastes or attaches there, so the same care applies as for any customer information: the prompt leaves out customer, model and style numbers, suppliers and costs, but fabric, colour and trim details still describe a customer's style.

## Keeping data inside LT

**Straight away:** in Settings > AI connections > Switches, turn **Allow cloud AI services** off. From the next call, no job sends anything to Gemini or to any connection not marked "runs on the LT network", whatever the jobs or the appsettings fallback say; those features say that cloud AI is not allowed and that no data was sent. To stop all AI, turn **AI features** off; to stop one job, set it to **Off**. New installs start with cloud AI not allowed.

**For good:**

1. Set up the in-house AI server ([Moving to the in-house AI server](in-house-server.md)).
2. In Settings > AI connections, point **both** jobs (Text and Image) at it. Check that no job is left on "App default", or that the appsettings defaults (`Ai:Provider`, `Ai:ImageProvider`) also point in-house.
3. Keep **Allow cloud AI services** off.
4. Block `generativelanguage.googleapis.com` at the firewall for the web server, as a second line of defence. Any feature that still tries Gemini then fails with "could not be reached" instead of sending data.
