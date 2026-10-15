# Dashboard (Home)

**Where:** Home > Dashboard (`/`). **Who:** every signed-in user; the library figures are shown to Style Library readers (Admin, Merchandiser, Costing, Viewer). Factory users see a welcome and the system status only.

A summary of the Style Library:

- **Totals**: styles, colorways, BOM lines, materials, suppliers, customers, seasons, style families and reused styles.
- **By season**: styles, colorways and BOM lines for the latest seasons.
- **Library health**: how many styles have a BOM, colorways, colorways used on the BOM, and an image. Low coverage is highlighted.
- **Customers, product types, material mix, top materials, top suppliers**: where the library is concentrated.
- **Recently changed styles** and the **last import** (file, who, when).
- **API status**: whether the server and database answer.

Admins see shortcuts to import styles when the library is empty.

**Speed.** The figures are worked out once and kept in the server's memory for everyone (`StyleDashboardCache`): the query (`style.usp_Dashboard_Get`) reads every BOM line and took about 0.6-0.9 s on 1,000 styles / 29,000 lines, growing with the library; kept, the page gets them in milliseconds. They are worked out again after any change made in the app (a style, colorway or BOM line saved or deleted, a copy, history links, an import commit, a reference-list change), and at the latest every **5 minutes**, which covers changes made outside the app (SQL scripts). With several web servers behind a load balancer, each keeps its own copy. Pictures of recently changed styles come as small thumbnails ([File storage](../installation/08-file-storage.md#thumbnails-and-browser-caching)).

The charts use the primary colour and its companion colours, and the cards follow the glass effect; both are set in the theme panel (see [Appearance](appearance.md)).

**Quotation Dashboard** (Home > Quotation Dashboard, Admin, Merchandiser, Factory) is the ported TMS dashboard of garment quotations; see [Garment Quotation](garment-quotation.md).
