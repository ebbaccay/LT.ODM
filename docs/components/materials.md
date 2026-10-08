# Materials

**Where:** Style Library > Materials (`/materials`, `/materials/:id`; the old `/boms` address redirects here). **Who:** Admin, Merchandiser, Costing, Viewer. The Description reader's reading and reviewing: Admin, Merchandiser.

The BOM seen by material instead of by style.

## Views (tabs)

| View | What it shows | Typical use |
|---|---|---|
| **All materials** | Every material with styles, BOM lines, seasons, main supplier, latest season; most used first. Filters: section, material type, supplier, customer, season (customer / season narrow the counts to those styles) | Find a material, see how widely it is used |
| **Standard trims** | For one product type: how many different materials do each job (e.g. 51 zippers on 83 jackets), how many are used once ("Worth standardising"), and each trim's share of the styles: *Standard kit* (50%+), *Common* (20-50%), *Occasional* | Default trims for new styles; consolidating one-offs to cut minimums |
| **Suppliers** | Share of styles per SAP supplier, materials only one supplier provides, lines without a supplier | Sourcing risk, negotiation |
| **Duplicates** | Groups of materials with identical descriptions | Cleaning the material master. Generic descriptions ("ADDITIONAL LABEL") group different items, so check before merging |
| **Recycled** | Share of fabric lines whose description says recycled, by season and product type; styles whose fabrics are all recycled | Sustainability reporting. Seasons without the word in descriptions show 0% |
| **Colours** | Most used material colours; colours under several codes | Dye-lot planning; colour code clean-up |
| **Description reader** (AI) | See below | Structured fibre, weight and width data |

## Material page

Tabs:

- **Where used**: every style and BOM line (newest season first, up to 1,000), with consumption, supplier, colours. A warning icon marks consumption under 0.6× or over 1.6× the usual (median) for that product type; **Unusual consumption** filters to those.
- **Consumption**: per product type, the range across styles (lowest, middle half, median, highest). Use it to sanity-check a new BOM.
- **Suppliers**: share of styles per supplier; flags materials bought from several sources.
- **Colours**: colours used, with colorway and style counts.
- **Possibly the same**: materials with the same description, or the same code before the `_` version suffix.
- **Specification (AI reading)**: the Description reader's result, if any.

## Description reader (AI)

The Text job reads material descriptions into: fibre composition (with recycled share), construction, weight (g/m²), width (cm), a suggested section, and a confidence.

1. Pick the section (opens on **Fabric**: 716 materials at the time of writing).
2. **Read next 25** (one AI call) or **Read all** (one call every ~8 seconds; **Stop** any time, what was read is kept).
3. Review: readings start **To review**. **Accept** or **Reject** per row or for a selection; **Select the sure ones** picks 90%+ confidence without warnings; **Back to review** undoes.
4. If a material's description changes later, its reading shows **Description changed**, and *Read next* reads it again.

Checks before a reading is stored: only materials that were sent; fibre percentages must add up to about 100% (else a warning and confidence capped at 50%); weight 10-1500 g/m² and width 20-400 cm or left out with a note; sections must exist. The material description and master are never changed.

What is sent to the AI service: [Data sent to AI services](../ai/data-sent.md#materials--description-reader).
