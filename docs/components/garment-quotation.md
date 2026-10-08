# Garment Quotation

**Where:** Manage Offerings > Garment Quotation (`/garment-quotation`); Home > Quotation Dashboard (`/dashboard`). **Who:** Admin, Merchandiser, Factory. **Data:** the TMS quotation tables and procedures (`web_rd_gq_*`, `web_rd_package_*`), reached through the secured procedure hub `/hubs/sp`.

Ported from TMS ("aRC Module"): office users request costings for styles from factories, factories submit their costing, office users compare and decide.

## Flow

1. **Pick a package** (a set of styles), then the styles. Filters by status: New, For Quotation, Submitted, For Revision, Approved, Rejected.
2. **Send to Factory**: assign one or more factories to the selected styles (also: resend, add a different factory, remove a factory, recall).
3. The **factory** sees only its own assignments, fills the **Factory Costing** (BOM prices and CMT items: cut/make/pack, transportation, print, embroidery, garment wash, misc, testing/inspection) and **Submits**. A draft BOM can be saved before submitting.
4. The office **compares** factories side by side (lowest and highest cost per item, grand total, margin vs TMS FOB), then **Approves**, asks to **Revise**, or **Rejects** with a reason. Approval creates the style costing.
5. **History** keeps every round (submission, approval, rejection); **Remarks** hold the conversation per style.

Also: currency conversion and exchange rate, wastage, margin, apply a value to all styles, export to Excel and PDF, print layout. Notifications reach users in real time over `/hubs/notifications`.

## Notes

- The CMT total uses the corrected calculation agreed during the port.
- `role` and factory id are still sent by the screen for some calls (as in TMS); they move to the server when the module gets typed endpoints.
