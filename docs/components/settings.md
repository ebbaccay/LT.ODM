# Settings

**Who:** Admin only (menu group at the bottom of the sidebar; the API checks the role on every call).

## Menu (`/settings/nav-config`)

Sidebar groups and pages: names (plain text or a translation key such as `nav.styles`), icons, order, and which roles see each page (no roles = every signed-in user). Hiding a page from the menu does not block its address; restricted pages are also guarded in the app and the API.

## Roles (`/settings/roles`)

Add roles, edit display names and descriptions. A role's name cannot change once created; `Admin` cannot be deleted.

## User roles (`/settings/user-roles`)

Add users (they set their own password from an emailed link), set roles, user group and location, send a new password link. See [Authentication and users](authentication-and-users.md).

## Reference lists (`/settings/reference-lists`)

Every pick list on the Styles screens: customers, seasons, season terms, business units, product types, weave types, content classes (BOM sections), material types, units and SAP suppliers. Codes are fixed once created; **Used by** counts where a code is used. A used code cannot be deleted, but customers, suppliers, seasons, business units and product types can be made inactive to hide them from pick lists.

## Import (`/settings/import`)

Loads the Style Library workbook (sheets *Style Header*, *Article*, *BOM Detail*; download the template from the page). Files up to 50 MB.

1. **Upload**: the server reads the workbook, stages every row and checks it. Nothing reaches the library yet.
2. **Preview** tabs:
   - **Styles**: each style as **New**, **Changed** (header, colorways and/or BOM differ from the library), **Unchanged** or **Blocked** (has errors).
   - **Issues**: every error and warning with its sheet and row.
   - **New codes**: codes the import would add to the reference lists (customer, product type, weave, business unit, material type, unit, SAP supplier) and values that block rows (content class, article status) or are left blank (gender). Each comes with a suggestion: **Same spelling** (ignoring case, spaces, punctuation and a plural S: `JACKETS` → `JACKET`, `YDS` → `yd`) or, after **Suggest with AI**, an AI suggestion with a reason. **Use** rewrites those rows in this import (not the workbook) and checks again. Values left alone are added as new codes at commit.
3. **Commit** writes the New styles and the Changed styles you ticked. A replaced style keeps its id; images uploaded in the app are kept when the file has none. Style history links are rebuilt.

Unfinished imports can be reopened from **Recent imports**; staged imports older than a day are cancelled automatically.

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

## AI connections (`/settings/ai`)

Which AI service each job uses. See [AI overview](../ai/overview.md) for how it works and [Data sent to AI services](../ai/data-sent.md) before connecting a cloud service.

- **Switches** (top of the page):
  - **AI features**: off = no AI call anywhere in the app; AI pages say AI is turned off.
  - **Allow cloud AI services**: off = only in-house connections are used; jobs set to Gemini or to a connection not on the LT network are blocked and send nothing. Off on new installs.
  - Turning cloud on or AI off asks for confirmation. Who changed them last, and when, is shown.
- **Connections**: add a service (Gemini, OpenAI-compatible in-house server, or custom HTTP), its address, API key (write-only: only the last 4 characters are shown afterwards; leave blank when editing to keep it), timeout, notes, whether it runs on the LT network, active or not. **Test connection** lists the service's models.
- **Jobs**: for each job (Text, Image in use; Embedding, Vision, Document, Prediction prepared for AI Lab) choose a connection and model (the chip button loads the model list), then **Save** that row. "App default" uses the server's configuration file. **Off** (shown when there is an app default) turns the job off entirely. A job stopped by a switch shows a red line saying why.
- Changes apply to the next AI call (within a minute on other server processes); no restart.
- A connection used by a job cannot be deleted or turned off.
