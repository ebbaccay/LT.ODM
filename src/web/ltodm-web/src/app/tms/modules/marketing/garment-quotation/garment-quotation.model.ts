import { Tone } from '@tms/shared/ui/tones';
export type QuotationStatus = 'new' | 'sent_to_factory' | 'factory_submitted' | 'for_revision' | 'approved' | 'rejected';
export type UserRole       = 'merchandiser' | 'factory';
export type PriceType      = 'FOB' | 'CIF' | 'EXW';
export type TabId          = 'bom' | 'costing' | 'compare' | 'history' | 'remarks';

export interface GqPackage { pack_name: string; }

export interface GqStyle {
  customer_id:  string;
  style_id:     string;
  season_id:    string;
  prodcat_id:   string;
  model_name:   string;
  composition:  string;
  curr_id:      string;
  rate_in_usd:  number;
  style_image:  string;
  // client-side
  selected:     boolean;
  quotations:   GqQuotation[];
  bom:          GqBomItem[];
  bomLoaded:    boolean;
  tms_fob?:     string;
  costing_curr?: string;
}

export interface GqBomItem {
  mat_class:    string | null;
  material:     string | null;
  part_desc:    string;
  consump:      string | null;
  consum?:      string | null;
  consump_uom?: string | null;
  wastage_pct?: string | null;
  curr_id:      string;
  price:        string | null;
  isCustom?:          boolean;
  addedByFactory?:    boolean;  // row was added by a factory user (not in original TMS BOM)
  factoryEditedFields?: string[];  // fields edited by factory user
}

export interface GqUom {
  uom_id:   string;
  uom_desc: string;
}

export interface GqCmtItem {
  key:         string;
  label:       string;
  value:       string;
  description: string;
  curr_id:     string;
  isCustom:    boolean;
}

export interface GqFactory { id: string; name: string; }

export interface GqRemark {
  rid:  string;
  role: UserRole;
  user: string;
  text: string;
  ts:   string;
}

export interface GqQuotation {
  qid:              string;
  factory:          GqFactory;
  status:           QuotationStatus;
  bom_json?:        string;
  bom?:             GqBomItem[];   // per-factory priced BOM (undefined = use style.bom)
  factoryCosting:   GqCmtItem[];
  smv:              string;
  smv_rate:         string;
  factory_remarks:  string;
  fc_qty:           string;
  ttl_production:   string;
  gross_margin_pct: string;
  adjustment:        string;
  fob_price:        string;
  revised_factory_fob: string;
  tms_comments:      string;
  price_type:       PriceType;
  fty_rate_usd?:    number;   // factory's chosen USD↔RMB rate; falls back to style.rate_in_usd
  rejectReason:     string | null;
  remarks:          GqRemark[];
  sentAt:           string;
}

export interface GqTotals {
  fabricTotal:    number;
  trimTotal:      number;
  othersTotal:    number;
  cmtTotal:       number;
  total:          number;
  totalUSD:       number;
  fabricTotalUSD: number;
  trimTotalUSD:   number;
  othersTotalUSD: number;
  cmtTotalUSD:    number;
}

export interface GqToast {
  id:   number;
  msg:  string;
  type: 'success' | 'danger' | 'info';
}

export interface StatusCfg {
  label: string;
  /** Colour family, rendered with the shared tone helpers (tms/shared/ui/tones.ts). */
  tone:  Tone;
}

// ── SP name constants ──────────────────────────────────────────────────────
export const GQ_SP = {
  PACKAGES:         'web_rd_product_package',
  STYLES:           'web_rd_package_styles',
  BOM_QUOTE:        'web_rd_package_style_bom_quote',
  UOM_LIST:         'web_rd_get_uom_list',
  CURRENCY_CONVERSION: 'web_rd_get_currency_conversion',
  SAVE_REMARK:      'web_rd_gq_save_remark',
  DELETE_REMARK:    'web_rd_gq_delete_remark',
  LOAD_REMARKS:     'web_rd_gq_remarks',
  SAVE_COSTING:     'web_rd_gq_save_costing',
  COSTING_HISTORY:  'web_rd_gq_costing_history',
  SAVE_QUOTATION:   'web_rd_gq_save_quotation',
  LOAD_QUOTATIONS:  'web_rd_gq_quotations',
  FACTORY_LIST:     'web_rd_get_factory_list',
  APPROVE_COSTING:  'web_rd_crt_style_costing_on_approve',
  DELETE_QUOTATION: 'web_rd_gq_delete_quotation',
  SAVE_STYLE_PRICING:    'web_rd_gq_save_style_pricing',
  LOAD_STYLE_PRICING:    'web_rd_gq_load_style_pricing',
  APPROVED_PRODUCTS:     'web_rd_get_gq_approved_products',
  UPD_STYLE_DISPLAY:     'web_rd_upd_gq_style_display',
  SAVE_BOM_DRAFT:        'web_rd_gq_save_bom_draft',
  LOAD_BOM_DRAFT:        'web_rd_gq_load_bom_draft',
} as const;

// Tables (tms_ prefix) + SPs → see database/garment_quotation_schema.sql
//   web_rd_gq_remarks         → SELECT from tms_gq_quotation_remarks
//   web_rd_gq_save_remark     → INSERT into tms_gq_quotation_remarks
//   web_rd_gq_save_costing    → INSERT into tms_gq_costing_history (rejection audit)
//   web_rd_gq_save_quotation  → UPSERT into tms_gq_quotations (live state)
//   web_rd_gq_quotations      → SELECT from tms_gq_quotations by pack_name

// ── Status display config ──────────────────────────────────────────────────
export const STATUS_CFG: Record<string, StatusCfg> = {
  new:               { label: 'New',           tone: 'neutral' },
  sent_to_factory:   { label: 'For Quotation', tone: 'primary' },
  factory_submitted: { label: 'Submitted',     tone: 'amber' },
  for_revision:      { label: 'For Revision',  tone: 'violet' },
  approved:          { label: 'Approved',      tone: 'green' },
  rejected:          { label: 'Rejected',      tone: 'red' },
};

export const FACTORIES: GqFactory[] = [
  { id: 'F001', name: 'Guangzhou Apex Garments Co.'   },
  { id: 'F002', name: 'Shanghai Star Textiles Ltd.'   },
  { id: 'F003', name: 'Dongguan PinPoint Manufacturing' },
  { id: 'F004', name: 'Foshan Elite Fashion Ltd.'     },
];

export const CMT_TEMPLATE: Pick<GqCmtItem, 'key' | 'label'>[] = [
  { key: 'cut_make_pack',      label: 'Cut / Make / Pack'        },
  { key: 'transportation',     label: 'Transportation'            },
  { key: 'print',              label: 'Print'                     },
  { key: 'embroidery',         label: 'Embroidery'                },
  { key: 'gmt_wash',           label: 'Gmt Wash'                  },
  { key: 'misc_charges',       label: 'Misc Charges'              },
  { key: 'testing_inspection', label: 'Testing / Inspection Fee'  },
];

export const CURRENCIES = ['RMB', 'USD', 'EUR', 'HKD'];

// Ordered by seq_no from IPLEXREF_CONTENT_CLASS (active_yn = 'Y')
// seq 1=FAB→FABRICS, 2=TRI→TRIMS, 4=ART→ARTWORK, 6=LBL→LABELS, 7=PCK→PACKAGING
export const MAT_CLASS_ORDER = ['FABRICS', 'TRIMS', 'ARTWORK', 'LABELS', 'PACKAGING'] as const;
export type MatClass = (typeof MAT_CLASS_ORDER)[number];

export const MAT_CLASS_LABELS: Record<string, string> = {
  FABRICS:   'Fabrics',
  TRIMS:     'Trims',
  ARTWORK:   'Artwork',
  LABELS:    'Labels',
  PACKAGING: 'Packaging',
};

export const STATUS_FILTER_KEYS: Array<string> =
  ['all', 'new', 'sent_to_factory', 'factory_submitted', 'for_revision', 'approved', 'rejected'];

// ── Pure helper functions ──────────────────────────────────────────────────
export function makeCmt(overrides: Record<string, string> = {}): GqCmtItem[] {
  return CMT_TEMPLATE.map(t => ({
    key:         t.key,
    label:       t.label,
    value:       overrides[t.key]            || '',
    description: overrides[`${t.key}_desc`]  || '',
    curr_id:     'RMB',
    isCustom:    false,
  }));
}

// FTY view uses the factory-edited rate when set; TMS / approval always use style rate.
export function effRate(style: GqStyle, q?: GqQuotation | null, role?: UserRole): number {
  if (role === 'factory' && q?.fty_rate_usd && q.fty_rate_usd > 0) return q.fty_rate_usd;
  return style.rate_in_usd;
}

export function toRmb(amount: string | number, curr: string, rate: number): number {
  const n = parseFloat(String(amount)) || 0;
  if (!n) return 0;
  switch (curr) {
    case 'USD': return n * rate;
    case 'EUR': return n * rate * 1.08;
    case 'HKD': return n * (rate / 7.83);
    default:    return n;
  }
}

export function calcTotals(bom: GqBomItem[], costing: GqCmtItem[], rate: number): GqTotals {
  const r = rate || 6.55;
  const lineRmb = (b: GqBomItem) => {
    const consumRaw = parseFloat(b.consump ?? b.consum ?? '');
    const consump   = Number.isFinite(consumRaw) ? consumRaw : 1;
    const price     = parseFloat(b.price || '0') || 0;
    const wastage = parseFloat(b.wastage_pct ?? '0') || 0;
    const base    = consump * price;
    return toRmb(base * (1 + wastage / 100), b.curr_id, r);
  };
  const fabricTotal  = bom.filter(b => b.mat_class === 'FABRICS').reduce((s, b) => s + lineRmb(b), 0);
  const trimTotal    = bom.filter(b => b.mat_class === 'TRIMS').reduce((s, b) => s + lineRmb(b), 0);
  const othersTotal  = bom.filter(b => b.mat_class !== 'FABRICS' && b.mat_class !== 'TRIMS')
    .reduce((s, b) => s + lineRmb(b), 0);
  const cmtTotal = (costing || []).reduce((s, i) => s + toRmb(i.value, i.curr_id, r), 0);
  const total = fabricTotal + trimTotal + othersTotal + cmtTotal;
  return {
    fabricTotal, trimTotal, othersTotal, cmtTotal, total,
    totalUSD:        total / r,
    fabricTotalUSD:  fabricTotal / r,
    trimTotalUSD:    trimTotal / r,
    othersTotalUSD:  othersTotal / r,
    cmtTotalUSD:     cmtTotal / r,
  };
}

export function deriveStatus(style: GqStyle): QuotationStatus {
  if (!style.quotations?.length) return 'new';
  const ss = style.quotations.map(q => q.status);
  if (ss.includes('approved'))          return 'approved';
  if (ss.includes('factory_submitted')) return 'factory_submitted';
  if (ss.includes('for_revision'))      return 'for_revision';
  if (ss.includes('rejected'))          return 'rejected';
  if (ss.includes('sent_to_factory'))   return 'sent_to_factory';
  return 'new';
}

export function fmtNum(n: number | string, d = 2): string {
  return (parseFloat(String(n)) || 0).toFixed(d);
}

export function nowTs(): string {
  return new Date().toLocaleString('en-US', {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

// ── History & Compare types ────────────────────────────────────────────────
export interface GqCostingHistory {
  historyId:      number;
  qid:            string;
  factoryId:      string;
  factoryName:    string;
  status:         QuotationStatus;
  changedBy:      string;
  changedAt:      string;
  rateInUsd:      number;
  cmt:            GqCmtItem[];
  bom:            GqBomItem[];
  smv:            string;
  smvRate:        string;
  fcQty:          string;
  fobPrice:       string;
  grossMarginPct: string;
  adjustment:      string;
  priceType:      PriceType;
  factoryRemarks: string;
  // computed on load
  fabricTotal:    number;
  trimTotal:      number;
  cmtTotal:       number;
  total:          number;
  fobPriceRmb:    number;
}

export interface CompareRow {
  key:             string;
  label:           string;
  labelKey?:       string;
  values:          (number | null)[];  // null = factory not yet submitted
  isTotal?:        boolean;
  isSep?:          boolean;
  isHigherBetter?: boolean;
  isPercent?:      boolean;
  isCustom?:       boolean;           // factory-added custom CMT item
}

export function makeQuotation(factory: GqFactory): GqQuotation {
  return {
    qid:              `Q_${factory.id}_${Date.now()}`,
    factory,
    status:           'sent_to_factory',
    factoryCosting:   makeCmt(),
    smv:              '',
    smv_rate:         '',
    factory_remarks:  '',
    fc_qty:           '',
    ttl_production:   '',
    gross_margin_pct: '',
    adjustment:        '',
    fob_price:        '',
    revised_factory_fob: '',
    tms_comments:      '',
    price_type:       'FOB',
    rejectReason:     null,
    remarks:          [],
    sentAt:           nowTs(),
  };
}
