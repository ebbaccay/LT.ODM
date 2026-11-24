# Costing: prices per IO

A style has no single price. The ERP costs each **IO** (internal order) on its own, so the same style can have a different FOB and different costs on every IO. LT ODM keeps those prices per IO and shows the big picture per garment (roadmap 3.16).

## Who sees what

| Role | Sees |
|---|---|
| Admin, Costing | FOB, cost groups, net balance, percentage to FOB, plant cost per minute |
| Merchandiser | FOB only (FOB minus the costs would give the net balance); sets quotes |
| Designer | FOB only, to design to a style's price (since 2026-11-11); sees quotes but does not set them or send prices to clients |
| Viewer, others | Nothing: no Prices by IO tab, no Costing page |
| Admin | Also loads the ERP costing export |

The API enforces this. Who sees the menu item is set in Settings > Menu.

## Where it shows

- **Style Library > Costing** (`/costing`): each style with prices, most recently costed first: IOs, total quantity, latest FOB, range, average FOB weighted by quantity and, for Admin and Costing, average net balance and percentage to FOB (net balance / average FOB). Search by style or model; filter by customer and season. A style with IOs in two currencies has a row per currency; currencies are not converted.
- **Style page > Prices by IO**: the same figures for the style, **Where the FOB goes** (fabric, trims, making, other, net balance: average per piece, Admin and Costing only), and a line per IO: costing date, plant name, planned delivery, quantity, FOB at 5 decimals and the cost groups.

Cost groups: **Fabric** = main and sub fabric; **Trims** = trims and local trims; **Making** = basic CM and subcon; **Other** = L/C charge, material freight, garment wash, embroidery, VA cost, print, other cost and claim reserve. The group of each category is in `cost.Category`.

Hover over a column heading (or tab to it) for what the column means. **Range** shows one amount when every IO is at the same price.

### Which costing stands for an IO

An IO can have several costing versions. It is shown with its latest **final costing** whose main fabric is computed, otherwise its latest **pre-costing**. A final costing whose main fabric is 0 may simply not be computed yet when it was extracted: the IO keeps its pre-costing and, for Admin and Costing, is marked *Final not computed*. A costing without a FOB is not finished and is not shown; the page says how many such IOs there are.

### Small orders

IOs below `Costing:SmallOrderQty` pieces (default 50, [configuration](../installation/04-configuration.md#costing)) are samples or one-offs. They stay listed, marked *Small order*, but are left out of the latest price, range and averages, unless the style has nothing else.

## Cost per minute by plant

Costing > **Cost per minute** tab (Admin and Costing; merchandisers do not see it): the plant rates from the ERP's Cost Per Minute table, kept by hand for now. Basic CM = the plant's cost per minute x TC SMV.

- A rate has a plant (from Settings > Reference lists > Plants), an **effective date**, a **minimum order quantity**, a **sales term** (FOB), a **currency** and the **cost** (5 decimals).
- The rate used for a plant on a date is the latest effective date on or before it, then the highest minimum quantity the order reaches (`cost.fn_PlantRate`). **In use** marks today's rate.
- **Delete** marks a rate deleted: it stays listed under **Show deleted rates** and is never used again, like the ERP's Deleted flag.
- Starting data: CM35 (XTIC YuenThai Cambodia), USD 0.14 from 2026-01-01, the rate every IO of the first costing export was costed at. Change the date or cost to the ERP's.

## Using the prices

Admin, Merchandiser, Designer and Costing see this; merchandisers and designers see FOB only, without the cost parts.

**A style's best price.** Its own IOs; else the same style in another season of the customer (same base style number); else the style it was copied from. The price shown says which, and IO figures follow the rules above.

**Quotes on packages** (roadmap 5.19, [Style Packages](style-packages.md#quotes)). Each style card suggests the latest FOB, with what it is based on.

**Estimate for a garment with no IO prices.** From the priced styles of the same garment (`cost.fn_Estimate`): the client's own (brand, else customer) when it has two or more, else every client's, in the currency most of them use.

| Part | How |
|---|---|
| Fabric, trims, other | Their average per piece |
| Making | The plant's cost per minute in force today (the plant most of them were made at) x their average TC SMV; their average making cost when the plant has no rate |
| Net balance | Their average share of FOB |
| Estimated FOB | The costs / (1 - that share) |

It is a first guide from today's data, always labelled *Estimate* with the styles it comes from. Merchandisers see the estimated FOB only; Admin and Costing also see the parts. When the IO material list arrives, fabric and trims can come from the design's own BOM.

- On a package, a style with no IO prices anywhere is offered the estimate (*Estimated*, **Use** quotes it with basis Estimate).
- On a concept with a product category, **Estimated cost** shows it against the target price: within, below (room), or over by how much, and for Admin and Costing how many minutes of making, or how much on fabric and trims, would close the gap (the estimate keeps the garment's net balance share, so a gap of G needs G x (1 - share) off the costs).

**Similar styles and Concepts.** Each style on the Similar tab and each proven style on a concept shows its latest FOB. Above them: the low, high and typical (middle) latest FOB, in the currency most of them use. On the Similar tab this counts styles of the same garment only. A concept's target price is judged by its **Estimated cost** (below), not by this line.

## Loading the ERP costing export

Style Library > Costing > **Load ERP costing** (Admin). **Download template** (beside it) gives the layout, with a real example row and a Read me sheet explaining each column. An Excel workbook: the first sheet, one row per costing version, columns found by heading:

`CUST`, `SEASON`, `STYLE ID`, `IO NO`, `COST VER#`, `COST TYPE` (PCOSTING or FCOSTING), `CURRENCY`, `FOB` are required. Used when present: `PLANT CODE`, `PLANT NAME`, `ORDER QTY`, `LCO DATE`, `PLAN DELDT`, `MERCH SMV`, `TC SMV`, `STATUS`, `COST DT`, `NET BAT`, `PERC TO FOB`, and the costs `MAIN FAB`, `SUB FAB`, `TRIMS`, `LC CHARGE`, `MAT FRT`, `LTRIMS`, `CM`, `SUBCON` (or `SUBSON`), `GARM WASH`, `EMBRO`, `VA COST`, `PRINT COST`, `OTHER COST`, `CLAIM`.

- A plant code seen for the first time is added to **Settings > Reference lists > Plants** with the name in the file. After that the list owns the name: correct it there and the export will not overwrite it.
- A row is matched to its library style by customer, season and style number. Rows without a style are **not saved**: the page lists those styles, so they can be imported first and the file loaded again.
- `COST VER#` is the key: loading a file again replaces those versions, it does not add them twice.
- Money and SMV are kept at **5 decimals**, as the ERP calculates them.
- Net balance and percentage to FOB are kept as the ERP sent them. A row whose net balance is not FOB minus the costs is kept and pointed out.

## How the ERP works out a costing

From the ERP team, checked on a worked example and on the first export.

| Category | How |
|---|---|
| FOB | Total document amount / total order quantity (so it can have 5 decimals) |
| Main fabric | Sum of (garment order qty x consumption x quote price x numerator / denominator), divided by total garment order qty |
| Trims | The same over all items except fabric, plus 1% |
| Basic CM | The plant's cost per minute x TC SMV |
| Material freight, local trims, claim reserve | Pre-costing defaults (ERP > Enterprise > Production) |
| Embroidery, VA cost, print, other cost | Uploaded VAS cost (External Upload T1) |
| L/C charge, subcon, garment wash | Typed by hand |
| Net balance | By the plus or minus of each cost category: FOB minus all the others |
| Percentage to FOB | Net balance / FOB x 100 |

Garment test, outbound freight, provision and material surcharge are not used in costing and are not in the export. Destination is not kept: an IO can cover several customer POs, each with its own destination.

## Where it lives

| Part | Location |
|---|---|
| Tables | `db/tables/cost.tables.sql`: `cost.Category`, `Import`, `Io`, `IoCosting`, `IoCostingLine`, `PlantCostPerMinute`; plants in `ref.Plant` (`db/tables/style.tables.sql`) |
| Procedures | `db/procedures/cost.procedures.sql`: `cost.fn_IoPrice` (the costing that stands for an IO), `fn_StylePrices`, `fn_PriceFor` (a style's best price), `usp_Costing_List`, `usp_StylePrices_Get`, `usp_Import_Save`, `usp_Price_Hints`, `usp_Quote_List` / `_Save` / `_Prices` / `_Check` |
| API | `CostingController` (`/api/v1/costing/styles`, `styles/{id}`, `imports`, `prices`, `estimate`, `rates`, dashboard panels `guide`, `my-concepts`, `overview`; `/api/v1/style-packages/{id}/quotes`, `items/{itemId}/quote`), `CostingRepository`, `CostingWorkbookReader`, contracts in `Application/Costing`; tests `CostingTests.cs` |
| Web | `features/costing` (`costing-page`, `style-prices`, `price-summary.ts`, `costing.service.ts`); `style-packages/package-item-quote` |
