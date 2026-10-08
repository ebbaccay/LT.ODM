# Offering tools (Manage Offerings)

Ported from TMS; they use their TMS procedures through the secured hub `/hubs/sp`, with the user, location and group filled from the sign-in token.

## SBU Submission (`/order-management`)

**Who:** Admin, Merchandiser (factories submit offers from their side).

Factories (SBUs) offer products against concepts; office users review the offers and proposals. The submitter, location and user group of a new offer and the author of a factory proposal come from the sign-in token; factory users see only their own factory's products.

## Collection Builder (`/collection-builder`)

**Who:** Admin, Merchandiser.

Build a collection for a concept from submitted offers: add and remove items. Who added or removed an item is recorded; an item keeps its original submitter. Concepts are picked with the shared concept picker.

## Product Matching (`/product-catalog`)

**Who:** Admin, Merchandiser, Viewer.

Ranks SBU products and approved quotations against a concept with keyword and FOB rules (not AI). **Submit offer** records a real SBU submission.

## Cost Optimization (`/cost-optimization`)

**Who:** Admin, Merchandiser.

Work a style's costing towards a target FOB: sessions, cost breakdown, BOM and CMT, reviews sent to a factory.

- **AI suggestions** (Text job): 4-6 concrete savings ideas (fabric, trim, labour, overhead) with from/to price; each idea is checked and its saving recomputed. Ideas can be applied and saved with the session.
- **Sensitive data:** the style's FOB, target FOB, cost breakdown, margin, BOM and CMT summaries are sent to the AI service. With Gemini they leave LT. See [Data sent to AI services](../ai/data-sent.md#cost-optimization--ai-suggestions).
- Known TMS limitation: a rejected plan cannot be resubmitted until the session procedure is fixed.

## Customer Proposal (`/collection/:id`)

**Who:** Admin, Merchandiser, Viewer.

A printable customer proposal built from a concept's collection. **Generate PDF** uses the browser's print dialog; the app's bars are not printed.

## Market Trends (`/product-trends`)

**Who:** Admin, Merchandiser, Viewer.

AI read-out of a collection against market trends (Text job): strength and growth per region, a trend score and tags per style, three insights. Scores are labelled as AI estimates. Each style's FOB price is sent to the AI service ([details](../ai/data-sent.md#market-trends--ai-analysis)). Old scores are cleared only after the AI answers.
