# SBU module

## SBU Products (`/sbu-overview`)

**Who:** Admin, Merchandiser.

Products offered by each SBU/factory, with photos (stored on the server under `sbu-products`, signed-in users only; see [File storage](../installation/08-file-storage.md)). The API also limits factory users to their own factory's products wherever these procedures are used, and stops a product being moved to another factory on edit; a new product's location and user group come from the sign-in token. Removing a photo keeps the file. Approved quotations show no lead time (TMS showed a cost there).

## SBU Performance (`/sbu-performance`)

**Who:** Admin, Merchandiser (it shows every factory's prices).

Scores factories on cost and delivery. The TMS procedure uses a cost field as lead time, so the screen re-scores with catalogue lead times only; "Overall" is TMS's fixed weighted formula (shown as "AI score" in TMS, but no AI is involved).

## Known open items

- Factory users can update or delete another factory's SBU product by id (the TMS procedures do not check ownership); to be fixed in SQL.
- The performance procedure's lead-time source should be corrected so the screen can drop its workaround.
