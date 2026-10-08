# Concept Studio

**Where:** Manage Offerings > Concept Studio (`/concept-studio`). **Who:** Admin, Merchandiser. **Data:** TMS concept tables and procedures, plus LT ODM endpoints for the AI brief, images and matching.

## Create a concept (New Concept tab)

1. **Inspiration Inputs**: concept name, customer, season, target market, target FOB price, trend tags (shared lists; anyone can add tags and markets). At least a name, a customer or one tag is needed.
2. **Generate AI Concept** (Text job): a summary, suggested products with prices, fabric direction, sustainability notes. Generate again until it fits.
3. **Inspiration Board**: upload images (JPEG, PNG, GIF, WebP up to 10 MB; stored on the server under `concept-inspiration`, shown to signed-in users only; see [File storage](../installation/08-file-storage.md)). Removing an image from the board keeps the file, because a saved concept may still use it.
4. **Matching Products from SBUs**: SBU products and approved quotations near the target FOB (rules, no AI).
5. **Proven styles in the library**: see below.
6. **Save Concept** (a name is required).

## Saved concepts

The **Saved Concepts** tab shows *Mine*, *My team* (same user group and location) or *All* (Admins and factory users see all; factories submit SBU offers against concepts). Only the creator or an Admin can edit or delete; loading a teammate's concept loads a copy.

## Proven styles in the library

**Find similar styles** finds existing styles with a proven BOM to start the concept from:

1. The AI reads the concept into library criteria: product types, gender, weave, customer, fabric keywords (e.g. RIPSTOP, FLEECE), trim types. Only real library codes are kept.
2. The database scores every style with a BOM (product type, keywords found on its fabric lines, trims on its BOM, gender, weave, customer) and takes the best 25.
3. The AI picks up to 6 with a **fit %** (green 85+, amber 60+), why it fits, what can be reused, what differs. Only scored candidates are accepted.

Each result opens the style. Changing the concept marks the results out of date (**Search again**). Results are not saved with the concept (yet): searching again makes two new AI calls.

## AI and data

The AI brief and the matching use the **Text** job (Settings > AI connections). With Gemini, the concept details (including the target FOB) and, for matching, summaries of 25 library styles go to Google. Details: [Data sent to AI services](../ai/data-sent.md).
