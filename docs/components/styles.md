# Styles (Style Library)

**Where:** Style Library > Styles (`/styles`, `/styles/:id`, `/styles/:id/compare`). **Who:** read: Admin, Merchandiser, Costing, Viewer. Change: Admin, Merchandiser. Factory users have no access (they see styles through Garment Quotation).

## Concepts

| Term | Meaning |
|---|---|
| Style | One per customer + season + style number (`S2508MR1212_SS27`). *Base style number* drops the reuse suffix (`S2508MR1212`) |
| Colorway | The style in one colour (adidas calls it an *article*; its code is the customer's number) |
| BOM line | One part + material + description + consumption + unit + supplier on the style, grouped by section (content class): Fabric, Trims, Accessories, Labels & packaging, Artwork |
| Material colour | Which colorways use a BOM line, and the material's colour in each |
| Style history | A style reused from an earlier one: *Carry-over* (later season) or *Variant* (same season, e.g. `_B_GRADE`) |
| LCO / brand consumption | LT's own consumption vs the customer's (brand) figure |
| Active / Inactive | Inactive styles stay in the library with their colorways, BOM and history, but are marked as no longer in use (`style.Style.IsActive`; new and imported styles are active) |

## List

Search by style number, model or description, by a colorway (code or name), or by anything on a BOM line: material code and description, nominated or SAP supplier (code or name), material colour. Every word must be in the same place (the style itself, one colorway or one BOM line): *navy zip* finds styles with a zip line coloured navy, not every style with a navy colorway and some zip. Under each style the list shows up to 3 colorways and 3 BOM lines the search was found on (words highlighted, *+N more* for the rest); opening the style then shows its BOM tab already filtered by the search (or its Colorways tab when only colorways matched). Filter by customer, season, business unit, product type, gender and status. The list shows **active styles with a picture or sketch** by default; pick *Inactive styles* or *All statuses* to see the rest (inactive rows are tagged), and untick **With pictures only** to see styles without pictures too (address `pictures=all`; **Clear** leaves this tickbox as it is). When nothing with a picture matches, the list offers to show styles without pictures. Links from the dashboard and other pages open with the tickbox on too. Each row's thumbnail is the style photo, or the sketch when there is no photo (or the photo file is missing). Filters are kept in the address, so a filtered list can be bookmarked or shared. AI Studio's smart search can also set weave, material, picture looks (`looks`, e.g. hooded, navy: only styles with tagged pictures, see [picture tags](ai-studio.md#picture-tags-look-filters)) and several seasons, business units or product types at once; these show as removable chips. A material filter with several words finds styles with a BOM line containing all of them.

## Style page

- Header: sketch and photo, customer, season (an **Inactive** tag when the style is inactive), model, product type, weave, gender, business unit, lead time, who created and changed it.
- Click a sketch, photo, colorway or BOM line image to see it large; **Download** saves it (named after the style, e.g. `S2808MR0000_sketch.jpg`).
- **Colorways** tab, **BOM** tab (by section; pick a colorway to see its material colours; search by part, material code or description, material type, section, supplier, material colour or colorway code, every word must match; an LCO filled from a suggestion shows *estimate*), **History** tab (the style's family: same base number and model, plus linked styles), **Similar** tab (below).
- Material codes on the BOM open the material's page ([Materials](materials.md)).
- AI shortcuts: **What changed** (if reused), **Check BOM**, **AI render** ([AI Studio](ai-studio.md)), **Design prompt** (below).

## Similar styles (Similar tab)

"Have we made this before, and what did we use?" Styles like the one open, worked out from the library when the tab opens (no AI; about 0.3 s). Every reader can use it.

- **Score** (0-100%): shared materials 65 (each material weighted by section, fabric 3, artwork 1.5, trims and accessories 1, labels and packaging 0.2, and by how rare it is: a fabric on 5 styles says more than a sewing thread on 2,000; the overlap of the two BOMs), the same garment 20 (product type read as JACKET, PANTS, TEE...; see [Fill the gaps](fill-the-gaps.md#gender-and-product-type)), the same weave 10, the same gender 5. Styles below 15% are not shown.
- **Same customer or brand** first (up to 8), then **Other brands** (up to 8) with the rule shown: know-how only, never pitch another brand's style. Customers of one brand count as the same brand (Settings > Import brands, e.g. the Skechers accounts).
- Each card: picture or sketch, style number, customer, season, product type, *Shares N materials (M fabrics)* or *Same garment and weave*, the **main fabric** (its fabric line with the largest consumption) and **Compare with this style** (the [compare page](#compare-styles-stylesidcomparefrom)).
- The style's own family (same customer and base number, or linked in History) is left out: it is on the History tab.
- Styles without BOM lines are matched by garment, weave and gender only.

## BOM tab: Fill LCO

Admins and merchandisers see **Fill LCO (N)** when lines have no LCO consumption. The dialog lists those lines with the suggested figure and where it comes from: the brand consumption (optionally plus the brand allowance), or the median of other styles using the same material. Untick lines to leave them; **Fill** writes the ticked figures marked as estimates. The same fill for many styles at once, and why the brand consumption is the suggestion: [Fill the gaps](fill-the-gaps.md#lco-consumption).

## Design prompt

A ready-to-paste prompt for outside design tools such as StyTrix or Style3D AI, built from the style's own data. **No AI is called and LT ODM sends nothing**: the user copies the text (or downloads it as `.txt`) and pastes it into the tool. Everyone who can read styles can use it. API: `GET /api/v1/styles/{id}/design-prompt?mode=text|image&colorwayId=`.

- **Text to design** describes the whole garment and asks for front and back technical flats plus a photorealistic front view.
- **Image to design** is for use with the style's sketch (or the photo when there is no sketch): the prompt says to keep the attached image's silhouette, seams and trim placement and apply the materials and colours. Download the image from the dialog and attach it in the tool. Not available until the style has a sketch or photo.
- **Colorway**: *All colorways* lists the in-range colorway names; one colorway gives its fabric and trim colours.
- What goes in: gender and product type, main fabrics (yarn specs removed, shorthand spelled out: `rec. pes` becomes *recycled polyester*), fabric weight from the description (`101.0 G/SQM` becomes *about 101 g/m²*), colours, visible trims (zipper, drawcord, tape, heat transfer, artwork…), and whether recycled fibres are used. Labels, packaging and thread are left out.
- What never goes in: customer, model name, style number, suppliers, costs and brand words (the same filter as AI render). The prompt always asks for an unbranded design.
- The text can be edited in the dialog before copying; **Reset to generated text** brings the original back. *Built from* shows the facts used.

## Compare styles (`/styles/:id/compare?from=...`)

What changed between a style and an earlier one, opened from the **History** tab:

- Each reused style has **Changes from** *(the style it was reused from)* and, when the chain goes further back, **All changes since** *(the family's first style)*.
- Tick any two styles in the family and click **Compare selected**; the later one (oldest season first) is compared against the earlier one.
- On the page, pick another family member or any style to compare with, or **Swap** the two.
- The differences are worked out by rules from the style data (header fields, colorways, BOM lines), the same as AI Studio's [Change summary](ai-studio.md#change-summary-aicomparestyleid), so it **works without an AI service**. The **AI summary** card appears only when the Text job is set up in Settings > AI connections.

## Editing (Admin, Merchandiser)

- Create, edit and delete styles, colorways and BOM lines; upload sketches, photos and images (JPEG, PNG, GIF, WebP up to 10 MB) with the upload button or by **dropping the file on the image box**. Images are made smaller on the server (at most 1600 px, JPEG for photos, PNG for sketches; see [Compression](../installation/08-file-storage.md#compression)) and stored as files on the server under `style-library` ([File storage](../installation/08-file-storage.md)).
- **Swap picture and sketch** (under the two image boxes) fixes a picture saved in the wrong place: the picture becomes the sketch and the sketch the picture. With only one of them, the button says **Move to sketch** or **Move to picture**. It changes the style like an edit (someone else's newer change stops it: reload and try again).
- **Pictures from sketch** (Colorways tab) gives each colorway its own picture, cut from the style's sketch (or picture, when both exist a switch picks which). The server splits the sketch into its colourways: whatever is enclosed by an outline is garment, and the sketch is cut left to right wherever the main colour changes. It then matches each colorway by the colour in its name: the part before the first `/`, season and colour codes such as F27 or 095A dropped, about 140 colour words and short forms such as GREFOU or UTIBLK known. Each colorway shows its suggested box on the sketch (drag to change, or click one of the colours found). Clear matches start ticked; **Check** means a close call; names without a colour (`multicolor 095A`) and colours not in the sketch get no box. **Save** cuts each ticked box, makes small ones sharper (as in [Import pictures](settings.md#import-pictures-settingsimage-import)) and sets it as the colorway's picture. Rules only, nothing is sent anywhere. Tested on 27 stored sketches: solid colourways drawn side by side or fanned (e.g. RC2620OW000: 5 of 5 right) work well; prints and stripes, sketches that show only one colourway, and two colours very close together (night brown and black) need a hand.
- Untick **Active** in Edit style to mark a style inactive (and tick it again to bring it back). Using an AI render as the photo keeps the style's status.
- **Reuse** copies a style to another season or as a variant (colorways, BOM and images) and records the carry-over.
- Set or remove history links by hand. Links made by the import (same base number and model) and by number (`S2808MR0000A` from `S2808MR0000`) are automatic; manual links are never replaced.
- New customer, season, business unit, product type, material, supplier, material type and unit codes typed in forms are added to their lists (clean them up in Settings > Reference lists).
- If someone else saved the record first, the save is refused and the screen asks to reload.

## Loading styles in bulk

See **Settings > Import** in [Settings](settings.md).
