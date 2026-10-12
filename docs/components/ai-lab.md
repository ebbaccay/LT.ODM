# AI Lab

**Where:** AI Studio > AI Lab (`/ai/lab`). **Who:** Admin, Merchandiser, Costing, Viewer.

A showcase of AI capabilities planned for production. **The demo results are written by hand** (style numbers start with `DEMO-`) and no AI model is called on this page. Beside each demo, live numbers from the library show how ready the data is.

| Capability | Status | Needs | Job |
|---|---|---|---|
| Material description reader | **Live** (Materials > Description reader) | - | Text |
| Import fix suggestions | **Live** (Settings > Import > New codes) | - | Text |
| Concept to existing styles | **Live** (Concept Studio > Proven styles in the library) | - | Text |
| Look-alike search | **Live** (AI Studio > [Image search](ai-studio.md#image-search-aiimage-search)); still needs data: photos or sketches on most styles | Pictures on the styles (5 of 1,003 had one in October 2026) | Vision + Embedding |
| Tech pack reader | Needs data | Sample tech packs per customer; a document-reading model | Document |
| Colour reader | Needs data | Colorway photos; customers' colour libraries | Vision |
| Cost estimate from BOM | Needs data | Quotation history linked to library styles; material prices; SMV | Prediction |
| Lead-time forecast | Needs data | Actual production lead times; supplier lead times | Prediction |
| Library assistant | Needs the in-house AI server | A model that can call tools; a read-only list of procedures it may use | Text |

The Library assistant is planned to use the same phrase list as Smart search (`StyleQueryParser`) first: questions made of known words (gender, season, product type, business unit, customer, material, style number) are answered from the database without AI, and only the rest go to the AI server.

For each capability the page shows what it does, what it needs, **Your data today** (live), which AI job it would use and whether a service is connected (admins get a **Configure** link), and a **Run demo** button. Live capabilities have an **Open the real feature** button.

To retire a demo when its feature is in use: delete its sample from `src/web/ltodm-web/src/app/features/ai-studio/ai-lab.data.ts`.
