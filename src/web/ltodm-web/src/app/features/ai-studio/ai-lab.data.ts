// AI Lab: SAMPLE results for capabilities planned for the production build. Nothing here comes from the library or
// from an AI model; the style numbers start with DEMO- so they cannot be mistaken for real ones. When a capability is
// built, its card moves to a real AI Studio page and its sample here is deleted.

export type LabStatus = 'ready' | 'needsData' | 'needsInfra';

/** The AI job (Settings > AI connections) a capability would run on. */
export type LabPurpose = 'text' | 'image' | 'embedding' | 'vision' | 'document' | 'prediction';

/** Which library numbers (GET /api/v1/styles/dashboard totals) show how ready the data is. */
export type ReadinessMetric = 'photos' | 'bom' | 'materials' | 'colorways' | 'families' | 'library' | 'lastImport';

export type LabSample =
  | { kind: 'matches'; input: string; items: { styleNo: string; model: string; season: string; score: number; why: string }[] }
  | { kind: 'table'; input: string; columns: string[]; rows: string[][]; note: string }
  | { kind: 'breakdown'; input: string; total: string; range: string; parts: { label: string; value: number; text: string }[]; note: string }
  | { kind: 'swatches'; input: string; items: { hex: string; name: string; share: number; nearest: string }[] }
  | { kind: 'fields'; input: string; fields: { label: string; value: string; confidence: number }[] }
  | { kind: 'fixes'; input: string; items: { issue: string; suggestion: string; confidence: number }[] }
  | { kind: 'chat'; turns: { role: 'user' | 'assistant' | 'tool'; text: string }[] };

/** Where the built feature lives; the demo stays until the capability card is retired. */
export interface LabLive {
  route: string;
  /** Query parameters, e.g. the Materials view to open. */
  query?: Record<string, string>;
  /** Who can open the page (any one of these roles). */
  roles: string[];
  /** Translation key naming where it is, e.g. "Settings > Import > New codes". */
  where: string;
}

export interface LabCapability {
  id: string;
  icon: string;
  status: LabStatus;
  /** Translation keys: ai.lab.cap.<id>.title / .what / .value and ai.lab.cap.<id>.needs.<n>. */
  needs: number;
  readiness: ReadinessMetric[];
  purpose: LabPurpose;
  /** In-house model kind that would run it (shown as is; model names are not translated). */
  runsOn: string;
  sample: LabSample;
  /** Set once the capability is built. */
  live?: LabLive;
}

export const LAB_CAPABILITIES: LabCapability[] = [
  {
    id: 'materialParse',
    live: { route: '/materials', query: { view: 'reader' }, roles: ['admin', 'merchandiser', 'costing', 'viewer'], where: 'ai.lab.liveFeature.materialParse' },
    purpose: 'text',
    icon: 'lucideListChecks',
    status: 'ready',
    needs: 1,
    readiness: ['materials'],
    runsOn: 'Text LLM (e.g. Qwen3 / Llama on vLLM)',
    sample: {
      kind: 'fields',
      input: '70% COTTON 30% RECYCLED POLYESTER,SOLID FLEECE,32s/1 cotton + 75D/36F PET-REC + 10s/1, 320GSM, CW 72"',
      fields: [
        { label: 'Composition', value: 'Cotton 70% · Recycled polyester 30%', confidence: 0.97 },
        { label: 'Construction', value: 'Fleece (knit), solid', confidence: 0.94 },
        { label: 'Weight', value: '320 g/m²', confidence: 0.92 },
        { label: 'Cuttable width', value: '72 in (183 cm)', confidence: 0.88 },
        { label: 'Recycled content', value: '30% (polyester)', confidence: 0.95 },
        { label: 'Suggested content class', value: 'FAB – Fabric', confidence: 0.99 },
      ],
    },
  },
  {
    id: 'importFix',
    live: { route: '/settings/import', roles: ['admin'], where: 'ai.lab.liveFeature.importFix' },
    purpose: 'text',
    icon: 'lucideWrench',
    status: 'ready',
    needs: 1,
    readiness: ['lastImport'],
    runsOn: 'Text LLM (e.g. Qwen3 / Llama on vLLM)',
    sample: {
      kind: 'fixes',
      input: 'Workbook DEMO_SS28.xlsx: 4 rows blocked',
      items: [
        { issue: 'Style Header row 12: customer "ADIDAS" is not a customer code', suggestion: 'Use ADI (adidas)', confidence: 0.98 },
        { issue: 'BOM Detail row 340: UOM "YDS" is not a unit', suggestion: 'Use yd', confidence: 0.96 },
        { issue: 'BOM Detail row 512: material type "ZIPPER" unknown', suggestion: 'Use ZIP (ZIPPER)', confidence: 0.93 },
        { issue: 'Style Header row 40: product type "Track Jacket" unknown', suggestion: 'Use TRKSJACKET (TRACKSUIT JACKET) or TRACKTOP (TRACK TOP)', confidence: 0.71 },
      ],
    },
  },
  {
    id: 'conceptMatch',
    live: { route: '/concept-studio', roles: ['admin', 'merchandiser'], where: 'ai.lab.liveFeature.conceptMatch' },
    purpose: 'text',
    icon: 'lucideLightbulb',
    status: 'ready',
    needs: 1,
    readiness: ['bom', 'families'],
    runsOn: 'Text LLM + SQL scoring',
    sample: {
      kind: 'matches',
      input: 'Concept "SS28 Trail Runner": lightweight men\'s woven running jacket, recycled ripstop, packable hood, target FOB $18',
      items: [
        { styleNo: 'DEMO-F2308MR1600', model: 'TRAIL WIND JKT M', season: '2027-SS', score: 0.91, why: 'Men\'s woven jacket, recycled ripstop shell, hood with drawcord and stoppers' },
        { styleNo: 'DEMO-F2308MR1588', model: 'RUN PACK JKT M', season: '2027-SS', score: 0.84, why: 'Same product type and shell fabric; no hood' },
        { styleNo: 'DEMO-S2608MR0412', model: 'OWN THE RUN JKT', season: '2026-FW', score: 0.77, why: 'Running jacket with packable pocket; plain-weave shell instead of ripstop' },
        { styleNo: 'DEMO-S2708WR0019', model: 'TRAIL WIND JKT W', season: '2027-SS', score: 0.69, why: 'Women\'s version of the closest match (grading and trims reusable)' },
      ],
    },
  },
  {
    id: 'lookAlike',
    purpose: 'embedding',
    icon: 'lucideImages',
    status: 'needsData',
    needs: 3,
    readiness: ['photos'],
    runsOn: 'Vision embedding model (e.g. SigLIP / CLIP) + SQL Server 2025 VECTOR search',
    sample: {
      kind: 'matches',
      input: 'Uploaded photo: competitor hooded track top, colour-blocked sleeves',
      items: [
        { styleNo: 'DEMO-F2608MR1604', model: 'ADZ HOODED TT', season: '2026-FW', score: 0.88, why: 'Hooded track top, raglan sleeve panels, full zip' },
        { styleNo: 'DEMO-S2708MR0230', model: 'TIRO HD TT', season: '2027-SS', score: 0.81, why: 'Colour-blocked sleeves, same zip and cuff construction' },
        { styleNo: 'DEMO-S2608UR0077', model: 'ESS FZ HOODIE', season: '2026-FW', score: 0.72, why: 'Similar silhouette; knit fleece instead of tricot' },
      ],
    },
  },
  {
    id: 'techPack',
    purpose: 'document',
    icon: 'lucideFileScan',
    status: 'needsData',
    needs: 3,
    readiness: ['library'],
    runsOn: 'Document vision-language model (e.g. Qwen2.5-VL) on the in-house GPU server',
    sample: {
      kind: 'table',
      input: 'TechPack_DEMO-S2808MR0050_SS28.pdf (14 pages)',
      columns: ['Part', 'Material', 'Description', 'Consumption', 'Unit', 'Confidence'],
      rows: [
        ['10', '70038112_1', '100% recycled polyester single jersey 140 g/m²', '1.420', 'yd', '96%'],
        ['20', '70014910_6', '96% rec. polyester 4% spandex 1x1 rib', '0.165', 'yd', '93%'],
        ['200', '80028350_2', 'Heat transfer, logo, chest left', '1', 'pc', '91%'],
        ['210', '80020552_VN', 'Woven tape 6 mm, back neck', '0.056', 'yd', '88%'],
        ['230', 'ZP-5RC-OE', 'Coil zipper #5, open end, 64 cm', '1', 'pc', '79%'],
      ],
      note: 'Rows under 85% confidence are highlighted for review before they go to the import preview.',
    },
  },
  {
    id: 'colorway',
    purpose: 'vision',
    icon: 'lucidePipette',
    status: 'needsData',
    needs: 2,
    readiness: ['colorways', 'photos'],
    runsOn: 'Vision model + colour library matching (in-house)',
    sample: {
      kind: 'swatches',
      input: 'Colorway photo DEMO-KG1976',
      items: [
        { hex: '#EDE6D6', name: 'Cream white', share: 0.71, nearest: 'Cream White (A0TP)' },
        { hex: '#1F5C46', name: 'Collegiate green', share: 0.18, nearest: 'Collegiate Green (D04X)' },
        { hex: '#FFFFFF', name: 'White', share: 0.08, nearest: 'White (0001)' },
        { hex: '#2B2B2B', name: 'Black', share: 0.03, nearest: 'Black (0009)' },
      ],
    },
  },
  {
    id: 'costPredict',
    purpose: 'prediction',
    icon: 'lucideDollarSign',
    status: 'needsData',
    needs: 3,
    readiness: ['bom'],
    runsOn: 'Gradient-boosted model (e.g. LightGBM) trained in-house on quotation history; LLM explains',
    sample: {
      kind: 'breakdown',
      input: 'DEMO-S2808MR0050 · men\'s woven jacket · 38 BOM lines',
      total: '$14.85',
      range: '$13.90 – $15.95 (80% range)',
      parts: [
        { label: 'Fabric', value: 52, text: '$7.72 · shell 1.42 yd, lining 0.95 yd' },
        { label: 'Trims & accessories', value: 17, text: '$2.52 · zipper, drawcord, stoppers, heat transfer' },
        { label: 'Labels & packaging', value: 6, text: '$0.89' },
        { label: 'CMT', value: 21, text: '$3.12 · 42 SMV' },
        { label: 'Overhead & margin', value: 4, text: '$0.60' },
      ],
      note: 'Closest quoted styles: DEMO-F2308MR1600 ($15.20), DEMO-F2308MR1588 ($14.10).',
    },
  },
  {
    id: 'leadTime',
    purpose: 'prediction',
    icon: 'lucideClock',
    status: 'needsData',
    needs: 2,
    readiness: ['library'],
    runsOn: 'Gradient-boosted model trained in-house on production actuals',
    sample: {
      kind: 'fields',
      input: 'DEMO-S2808MR0050 · 2028-SS · factory KH01 · 3 nominated fabrics',
      fields: [
        { label: 'Predicted lead time', value: '78 days (70 – 88)', confidence: 0.8 },
        { label: 'Longest path', value: 'Recycled ripstop (nominated mill): 45 days', confidence: 0.86 },
        { label: 'Risk', value: 'Heat transfer artwork not yet approved (+6 days typical)', confidence: 0.74 },
        { label: 'Compared with family', value: '+9 days vs DEMO-F2308MR1600 (new zipper supplier)', confidence: 0.82 },
      ],
    },
  },
  {
    id: 'assistant',
    purpose: 'text',
    icon: 'lucideMessagesSquare',
    status: 'needsInfra',
    needs: 2,
    readiness: ['library', 'bom'],
    runsOn: 'Text LLM with tool calling on vLLM; tools = the library\'s stored procedures (read-only)',
    sample: {
      kind: 'chat',
      turns: [
        { role: 'user', text: 'Which SS28 men\'s jackets use the recycled ripstop from TOP SPORTS, and which ones changed consumption since last season?' },
        { role: 'tool', text: 'style search: season 2028-SS, gender MALE, product types JACKET/JACKETS, material "RIPSTOP" → 6 styles' },
        { role: 'tool', text: 'change summary × 6 (carry-overs only) → 4 compared' },
        {
          role: 'assistant',
          text: '6 SS28 men\'s jackets use it. 4 are carry-overs; 2 of those changed shell consumption: DEMO-S2808MR0050 (1.38 → 1.42 yd, +3%) and DEMO-S2808MR0061 (1.55 → 1.71 yd, +10%, new hood). The other 2 are unchanged. Want the list opened in Styles?',
        },
      ],
    },
  },
];
