# Settings

**Who:** Admin only (menu group at the bottom of the sidebar; the API checks the role on every call).

## Menu (`/settings/nav-config`)

Sidebar groups and pages: names (plain text or a translation key such as `nav.styles`), icons, order, and which roles see each page (no roles = every signed-in user). Hiding a page from the menu does not block its address; restricted pages are also guarded in the app and the API.

## Roles (`/settings/roles`)

Add roles, edit display names and descriptions. A role's name cannot change once created; `Admin` cannot be deleted.

## User roles (`/settings/user-roles`)

Add users (they set their own password from an emailed link, or sign in with a temporary password the Admin sets and must change it), set roles, user group and location, send a new password link, set a temporary password, deactivate, reactivate or remove a user. See [Authentication and users](authentication-and-users.md).

## Reference lists (`/settings/reference-lists`)

Every pick list on the Styles screens: customers, seasons, season terms, business units, product types, weave types, content classes (BOM sections), material types, units and SAP suppliers; and for [Concepts](concepts.md) (added 2026-10-28): **target markets**, **trend tags** and **product categories** (codes upper-case: letters, digits, `-`, `_`; a first set is added when a list is empty; product categories use the garment codes of What to offer and the hit rate, such as JACKET or PANTS). Codes are fixed once created; **Used by** counts where a code is used. A used code cannot be deleted, but customers, suppliers, seasons, business units, product types and the three concept lists can be made inactive to hide them from pick lists.

**Seasons** can have any name of up to 16 characters, without `|`, stored upper-case (e.g. `2027-SS`, `FA26`, `ALL-SEASON`). Names that show a year and term (`2027-SS`, `FA26`, `26FW`) get them filled for sorting; others sort last. Season drop-downs show the **description** when there is one, otherwise the name. Note that [Fill the gaps](fill-the-gaps.md) lists every season not named year-term (`2027-SS`) as one to merge.

## Import (`/settings/import`)

Loads the Style Library workbook (sheets *Style Header*, *Article*, *BOM Detail*; download the template from the page). Files up to 100 MB. Style ID and Article ID can be up to 100 characters; a longer value stops the upload with its sheet and row.

1. **Map columns**: after choosing the file, pick the workbook sheet for Style Header, Article and BOM Detail, and for each Excel column the database column it goes to (or *Not imported*). Columns with a known heading (the template's, and spellings such as `Season_id`, `AD_COnsump_UOM`, `ASAD_Consump_Allowance`) are matched for you; sample values show what each column holds. Example, GO style export: `Gen_Mat_ID` → *Ad_Mat_ID* (material code) and `Prod_tyle_id` → *Prod_type_id*. **Save this mapping as** keeps it by name; the next file whose sheets and headings fit a saved mapping opens with it (the best fit if several do). Required columns (*) must be mapped before upload.
2. **Upload**: the server reads the workbook with that mapping, stages every row and checks it. Nothing reaches the library yet.
3. **Preview** tabs:
   - **Styles**: each style as **New**, **Changed** (header, colorways and/or BOM differ from the library), **Unchanged** or **Blocked** (has errors).
   - **Issues**: every error and warning with its sheet and row.
   - **New codes**: codes the import would add to the reference lists (season, customer, product type, weave, business unit, material type, unit, SAP supplier) and values that block rows (content class, article status) or are left blank (gender). Each comes with a suggestion: **Same spelling** (ignoring case, spaces, punctuation and a plural S: `JACKETS` → `JACKET`, `YDS` → `yd`) or, after **Suggest with AI**, an AI suggestion with a reason. **Use** rewrites those rows in this import (not the workbook) and checks again. Values left alone are added as new codes at commit.
4. **Commit** writes the New styles and the Changed styles you ticked. A replaced style keeps its id; images uploaded in the app are kept when the file has none. Style history links are rebuilt.

Unfinished imports can be reopened from **Recent imports**; staged imports older than a day are cancelled automatically.

**Kept workbooks.** Choosing **Next: map columns** keeps the workbook on the server with a new import in status *Mapping columns*; the mapping is saved as you edit it. In **Recent imports**:
- *Mapping columns*: **Continue mapping** reopens the step where you left it;
- *Waiting to commit* and *Cancelled*: **Change mapping** reopens the step with the mapping used; uploading again checks the kept workbook from scratch with the new mapping (the import is waiting to commit again; no new file needed);
- any import not committed can be **deleted** (trash icon): it leaves the list with its checked rows and kept workbook. Committed imports stay (use **Roll back**).
The workbook is deleted on commit, and 7 days after a cancel or an unfinished mapping (an unfinished mapping is then removed from the list); after that, upload the file again. Imports cancelled before 2026-10-15 have no kept workbook and can only be viewed or deleted.

**Roll back** (Admin; on a committed import, for 30 days after the commit) undoes it in one transaction: the styles it added are deleted with their colorways, BOM lines, AI renders and picture data; the styles it replaced get back the header, colorways and BOM they had before (commit keeps a copy of each in `staging.ImportUndoStyle`; restored colorways and BOM lines get new ids); materials it changed get their old description, type and section back, and materials it added are deleted when no BOM uses them (with their description readings); style history is rebuilt for the families involved. Then each season, customer, supplier, product type, business unit, weave, material type and unit it added is deleted if nothing uses it (one still in use, e.g. by a later import, stays).
Nothing is changed, and the page lists why, when anything happened after the import: a style edited or deleted, replaced by a later import, colorways or BOM lines added or changed, AI renders made, style history linked by hand, a material it touched edited, or a style it added put in a [style package](style-packages.md). Undo those changes (or leave the import) and try again. Imports committed before rollback existed (2026-10-14) cannot be rolled back. The rollback data is kept for 30 days (`StyleImportRules.RollbackDays`).

**BOM rows without a material code** (Ad_Mat_ID, or the column mapped to it, empty or NULL) cannot become BOM lines: each is a warning (*this BOM row is skipped*), the Questions panel and the commit confirmation say how many rows in how many styles, and the commit leaves them out; their styles import with the rest of their BOM. To keep them, fill in the material code and upload again.
An Article row without an Article ID blocks its style (before 2026-10-14 it was reported without blocking and the commit then failed).

**Seasons** are kept as the workbook writes them: `2027-SS`, or the source system's own names (`26FW`, `FA26`, `SS27`, `26Q2`, `CR26`, `999`). Year and term are read from the name where possible (`26FW` → 2026 FW) to sort seasons; the others sort last and are not used for style history links until normalised. On **New codes**, a season such as `FA26` is suggested as `2026-FA` when that season exists; **Use** moves the rows to it. New seasons typed in the app (style form, Reference lists) still use year-term.

**Questions before commit** (above the preview tabs) group what would otherwise add duplicates:
- values with a suggested existing code, list by list: *63 SAP supplier values match existing codes* (SAP numbers match with or without leading zeros, `0001100131` = `1100131`; seasons such as `FA26` = `2026-FA`) → **Use all**, or **Review** on the New codes tab;
- customer accounts that look like one customer, by the brand word in their names (`Cust_Name`): accounts whose brand word starts with an existing customer's code or matches its name are offered to it (*4 customer accounts look like SKE* → **Map all to SKE**); two or more accounts with the same brand word and no existing customer (11 POLO accounts) can be mapped to a customer you choose, **grouped as a new customer** (code and name; the customer is added straight away), or kept separate;
  or **kept separate, same brand**: each account stays its own customer, all under one brand (`ref.Brand`, `partner.Partner.BrandCode`; the brand is added if new, e.g. POLO for 11 POLO accounts; for a group that looks like an existing customer such as SKE, that customer gets the brand too if it has none);
- customers that will still be added as new are listed (with their brand), and the commit confirmation says how many customers and SAP suppliers it adds.

**Brands** can also come from the workbook: map a column to *Customer_Brand* (one brand per customer account in a file). New brands are added at commit (and listed on the New codes tab); an existing customer without a brand gets the file's, one with a brand keeps it. Brands are listed in Reference lists > Brands (*Used by* counts customers). A rollback or deleting the import removes brands and customers added for it (when unused) and takes back brands it gave existing customers.

With **Remember my answers** ticked (default), every answer here and on the New codes tab is saved (`staging.ImportCodeAlias`) and applied to later uploads before the check, so each value is asked about once. The panel shows the answers an import used; **Forget** makes later imports ask again (to undo one in the current import, cancel and upload again).

**BOM rows without an Article ID** apply to all of the style's colorways; one warning per style says how many rows did so.

**Extra columns** (added 2026-10-14): *Cust_Name* is kept on each style (`style.Style.CustomerName`) and names a customer the import adds (instead of its code); *Consump_Allowance* (`ASAD_Consump_Allowance`) is stored per BOM line (`style.BomLine.ConsumptionAllowance`, a number of 0 or more) and is part of what makes a BOM line Changed. Neither is shown on the Styles screens yet.

## Import pictures (`/settings/image-import`)

Adds style pictures from a folder of files named after the style. Admin only. Nothing changes until **Save**.

**File names.** The style number is the file name up to its first `_` (`RC2606OW001_Img01.jpg` → `RC2606OW001`; the rest is ignored, as for style numbers in the library). A file is offered for:

| Match | Example for `RC2606OW001` | Ticked |
|---|---|---|
| **Same number** | `RC2606OW001` | Yes |
| **Version** (suffix after `_` or `-`) | `RC2606OW001_H`, `RC2606OW001_LH`, `RC2617OW006-H` | Yes |
| **Letter version** (its own style) | `RC2701OW001A_H` | No |
| **Base style** (file has a letter version) | `RC2702OM001` for `RC2702OM001A_Img01.jpg` | No |

All seasons of a style number are listed. Files whose number is not in the library show **No style found**. **Only styles in the library** (on by default) hides those files from the list; they stay under the **No style found** tab. **Only styles without a picture** (off by default) shows only files whose styles have neither a picture nor a sketch yet, and saving then skips the ticked styles that already have one (marked *Has a picture: skipped*). While saving, a progress bar shows how many pictures are done.

**Steps.**

1. Drop the pictures or a folder on the page, or use **Choose a folder** (JPEG, PNG, GIF or WebP, up to 10 MB each). Each file is analysed on the server, two at a time (1-6 s each).
2. Check each picture. On sheets that hold other information (checkpoint tables, screenshots of a sketch window), the server suggests the **garment box**. It finds the picture among the table lines and text, and the image model built into the API picks the part that looks like a garment. Drag the box to move it, drag a corner to resize, or drag elsewhere to draw a new one (arrow keys move it; Shift + arrows resize). **Whole picture** keeps everything; **Suggested box** goes back to the suggestion. Pictures marked **Check the box** are the ones the model is not sure about. **Preview** shows the result exactly as it will be saved.
3. Tick the styles, choose the options and **Save**. Options:
   - **Save as**: **Detect** (default), Picture (`style.Style.ImageUrl`) or Sketch (`SketchUrl`). With Detect, each file goes where it belongs: sketches and flat drawings to the sketch, photos of the made garment to the picture. Each file also has its own **Save to** switch showing what was detected; close calls say *Not sure* and are listed under **Check the box**. Detection uses the image model built into the API plus black outlines and background colour (tested on 45 sketches and photos: all right, 2 close calls). Without the model only clear cases are detected, and the rest go to the picture. A picture saved in the wrong place can be moved later on the style's page (**Swap picture and sketch**).
   - **Make small pictures sharper** (on): cut-outs under 600 px are enlarged 4x, under 150 px 16x (two passes), with the AI upscaler built into the API. Without it, or with AI features off, they are enlarged smoothly instead.
   - **Replace existing pictures** (off): styles that already have a picture of that kind keep it.

One file is stored per picture and shared by all styles ticked; it goes through the usual [compression](../installation/08-file-storage.md#compression) (at most 1600 px). New pictures appear in image search after **Index pictures** in AI Studio > Image search.

**Pictures already in the library.** **Check existing pictures** (top of the page) runs the same sketch-or-photo check on every stored style picture and sketch (each file once, about a quarter of a second each) and lists the styles to fix: *Sketch → move to sketch*, *Photo → move to picture*, *Swap picture and sketch*, or *Both look the same kind* (left alone; check by hand). Clear calls start ticked, close calls do not. **Move** swaps picture and sketch on the ticked styles. A style changed since the check is skipped (check again). Nothing changes before **Move**. On LTODM (2026-10-13) the first run moved 42 imported sketches from the picture to the sketch; 3 close calls (dark textured flats, all sketches) and one conflict (OJS27250477: a photo in the sketch place) were left for a person to decide.

**How good is the box?** Tested on 37 checkpoint sheets and sketch screenshots: about 6 in 7 boxes needed no change. The rest cut off a view, took in a table column, or picked text next to a very small picture (those are mostly marked **Check the box**). The finder and the upscaler run on the web server's CPU. Nothing is sent to an AI service ([Data sent to AI services](../ai/data-sent.md#settings--import-pictures)). Without the image model, the box is chosen by shape alone and every picture is marked to check.

## Translations (`/settings/translations`)

Corrects the screen texts (English and Chinese) without a new release. The texts that come with a release (`wwwroot/assets/i18n/<lang>.json`) stay the base; a correction is kept only while it differs from the released text, so a later release still brings its new and reworded texts.

- **List**: every text key with its text per language. Corrected texts are highlighted. **Show** filters to *Corrected*, *Not translated* (missing, or the same as the English text) or *Changed by a release* (a release reworded the original after it was corrected here: check the correction still fits). Search looks at keys and texts.
- **Edit**: click a row, change the text for any language, **Save**. Leave a box empty, or click **Use released text**, to go back to the released text. Placeholders such as `{{ name }}` must stay exactly as in the English text (the app fills them in); the page and the API refuse a text that drops or renames one.
- **Download Excel**: a workbook with the sheet *Translations* (Key, one column per language headed e.g. `Chinese (Simplified) (zh-Hans)`, and *Corrected in LT ODM*; corrected cells are yellow) and a *How to use* sheet for the translator. Rows can be sorted or filtered; extra columns are ignored.
- **Upload Excel**: choose the edited workbook (.xlsx, up to 10 MB). The server lists what would change (old → new per key and language) and any problems: a broken placeholder or a key listed twice (**error**, that cell or row is skipped), a key the app no longer uses (**warning**, skipped). Nothing is saved until **Apply**. An empty cell, or the released text typed back, removes the correction. Keys missing from the file are left as they are.
- **History**: every save (edit, upload, restore) keeps the version it replaced (last 30 by default); **Restore** brings an earlier version back and keeps the current one, so it can be undone.
- Saved changes show at once for the Admin who saved them; other users get them the next time the app loads (sign-in pages included).
- New keys still need a release: the screens refer to keys, so this page changes the wording of existing keys only.
- Corrections are a file on the server (`Translations:OverridesFolder`, see [Configuration](../installation/04-configuration.md#files)); export now and then and copy the texts into `src/web/ltodm-web/public/assets/i18n` so the repository catches up.

## Sign-in page (`/settings/sign-in-page`)

Changes the look and the texts of the sign-in, forgot-password and reset-password pages, with a live preview (wide-screen layout, in the current language and light/dark mode). Nothing changes for users until **Save**; **Discard changes** goes back to what is saved, **Default look** to the original look (texts are not touched).

- **Background**: upload a picture (JPEG, PNG, GIF or WebP, up to 10 MB; stored at most 2560 px on the longest side, metadata removed). A landscape picture at least 1920 px wide looks best. **Keep in view** chooses the part that stays visible when the picture is cropped (centre, top, bottom, left, right); **Blur the picture** 0-24 px. The picture is shown **before sign-in**, so anyone who can reach the site can see it: do not use confidential pictures.
- **Light mode / Dark mode**: the switch above the picture chooses which mode is edited and previewed. Dark mode uses the light-mode look until **Separate look in dark mode** is ticked; then dark mode has its own picture (or none), focus, blur, tint, headline text colour and card opacity, starting from a copy of the light-mode ones. The colour glow, frosting and headline-panel options are shared. The sign-in page shows the look of the mode the visitor uses (the moon/sun button, or the device setting).
  - Why it matters for the texts: the **headline panel** text colour depends on the picture (dark text on a light picture, light text on a dark one), so it is set per mode. The **card** text follows the mode: in dark mode the card is dark glass with light text, so over a light picture it turns grey and red error messages and links lose contrast. Use a darker picture for dark mode, or raise the dark-mode card opacity.
  - The preview can show dark mode while the app is light, not the other way round: with the app in dark mode the light-mode look is previewed with dark-mode card colours, and **Switch to light mode** changes the app's mode.
- **Tint**: none, dark, light or the brand colour, with a strength of 0-90 %, so the headline and card stay readable over a busy picture. **Colour glow**: the soft moving colour blobs and faint grid of the original look (also over a picture).
- **Glass card**: **Card opacity** 10-95 % (lower = more of the background shows through) and **Frosting** 0-40 px (blur of what shows through). Without blur support in the browser, with *reduce transparency* or high contrast turned on, the card is shown nearly solid.
- **Headline panel** (left of the card on wide screens): show or hide it and the four feature tiles. With a picture, **Text colour on the picture** is light (for dark pictures), dark (for light ones) or automatic (follows light/dark mode).
- **Texts**: the badge, headline, introduction, feature tiles, card title and subtitle and the note under the form, per language. They are saved as corrections in [Translations](#translations-settingstranslations) (same file, same history and restore), so the sign-in page follows the language the user picks; the arrow puts back the released text. Other sign-in texts (field labels, messages) are changed in Translations.
- The first picture uploaded also sets a dark 35 % tint, light headline text, card opacity at least 70 % and turns the glow off; adjust them after.
- Storage: the look is `sign-in-page.json` and the picture is in `sign-in-images` under `Files:Root` (see [Configuration](../installation/04-configuration.md#files)); no database change. Pictures uploaded but not saved, and the one replaced, are removed at the next save.
- API: `GET /api/v1/sign-in-page` and `GET /api/v1/sign-in-page/images/{file}` are open without sign-in (the page shows before it); `POST /api/v1/sign-in-page/images` and `PUT /api/v1/sign-in-page` need the Admin role.

## AI connections (`/settings/ai`)

Which AI service each job uses. See [AI overview](../ai/overview.md) for how it works and [Data sent to AI services](../ai/data-sent.md) before connecting a cloud service.

- **Switches** (top of the page):
  - **AI features**: off = no AI call anywhere in the app; AI pages say AI is turned off.
  - **Allow cloud AI services**: off = only in-house connections are used; jobs set to Gemini or to a connection not on the LT network are blocked and send nothing. Off on new installs.
  - **Limit AI calls** (on by default): each AI server gets at most its connection's *Calls at once*; the other calls wait their turn (quick questions first, users taking turns), and each user may have **AI tasks per user** (default 3) waiting or running. Off = no limits. See [AI call limits](../ai/overview.md#ai-call-limits-taking-turns-at-the-ai-server).
  - Turning cloud on or AI off asks for confirmation. Who changed them last, and when, is shown.
- **Connections**: add a service (Gemini, OpenAI-compatible in-house server, or custom HTTP), its address, API key (write-only: only the last 4 characters are shown afterwards; leave blank when editing to keep it), timeout, **calls at once** (while AI call limits are on: 1 for a server without a GPU, 4-16 for a GPU server with vLLM or a cloud service; connections with the same address share the lowest), notes, whether it runs on the LT network, active or not. **Test connection** lists the service's models.
- **Jobs**: for each job (Text, Image, Embedding and Vision in use; Document, Prediction prepared for AI Lab; Embedding and Vision accept only OpenAI-compatible connections and the embedding model must return 1024 numbers) choose a connection and model (the chip button loads the model list), then **Save** that row. "App default" uses the server's configuration file. **Off** (shown when there is an app default) turns the job off entirely. A job stopped by a switch shows a red line saying why.
- Changes apply to the next AI call (within a minute on other server processes); no restart.
- A connection used by a job cannot be deleted or turned off.
- **AI tasks** (bottom of the page): every user's slow AI work (renders, summaries, explanations, material readings, Fill the gaps, import suggestions), newest first, filtered by status (default *Queued or running*): what it is, the style or label, who started it, when, how long it ran, and any error. **Stop** ends a queued or running task (it asks first; nothing unfinished is saved). **Keep finished tasks for** N days (1-365, default 7): older tasks are deleted with their answers. See [AI tasks](../ai/overview.md#ai-tasks-slow-ai-work-in-the-background).
