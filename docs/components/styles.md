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

Search by style number, model, description or colorway code; filter by customer, season, business unit, product type, gender and status. The list shows **active styles** by default; pick *Inactive styles* or *All statuses* to see the rest (inactive rows are tagged). Each row's thumbnail is the style photo, or the sketch when there is no photo (or the photo file is missing). Filters are kept in the address, so a filtered list can be bookmarked or shared. AI Studio's smart search can also set weave, material and several seasons, business units or product types at once; these show as removable chips. A material filter with several words finds styles with a BOM line containing all of them.

## Style page

- Header: sketch and photo, customer, season (an **Inactive** tag when the style is inactive), model, product type, weave, gender, business unit, lead time, who created and changed it.
- Click a sketch, photo, colorway or BOM line image to see it large; **Download** saves it (named after the style, e.g. `S2808MR0000_sketch.jpg`).
- **Colorways** tab, **BOM** tab (by section; pick a colorway to see its material colours; search by part, material code or description, material type, section, supplier, material colour or colorway code, every word must match), **History** tab (the style's family: same base number and model, plus linked styles).
- Material codes on the BOM open the material's page ([Materials](materials.md)).
- AI shortcuts: **What changed** (if reused), **Check BOM**, **AI render** ([AI Studio](ai-studio.md)), **Design prompt** (below).

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
- Untick **Active** in Edit style to mark a style inactive (and tick it again to bring it back). Using an AI render as the photo keeps the style's status.
- **Reuse** copies a style to another season or as a variant (colorways, BOM and images) and records the carry-over.
- Set or remove history links by hand. Links made by the import (same base number and model) and by number (`S2808MR0000A` from `S2808MR0000`) are automatic; manual links are never replaced.
- New customer, season, business unit, product type, material, supplier, material type and unit codes typed in forms are added to their lists (clean them up in Settings > Reference lists).
- If someone else saved the record first, the save is refused and the screen asks to reload.

## Loading styles in bulk

See **Settings > Import** in [Settings](settings.md).
