import { resolveImageUrl } from '../modules/marketing/concept-studio/concept-studio.model';
import { GqBomItem, GqCmtItem, calcTotals } from '../modules/marketing/garment-quotation/garment-quotation.model';
import { SpTables } from './sp-result';

/** One cost line of a product (stored as JSON in tms_sbu_products.cost_breakdown). */
export interface CostBreakdownItem {
  item: string;
  amount: number;
}

/**
 * A product an SBU or factory offers: either entered in the SBU overview ('manual', editable)
 * or an approved garment quotation ('gq', read-only).
 */
export interface SbuProduct {
  id: string;
  recid?: number;
  name: string;
  styleCode: string;
  sbu: string;
  country: string;
  countryCode: string;
  category: string;
  fobPrice: number;
  /** JSON: CostBreakdownItem[] */
  costBreakdown: string;
  /** Null when unknown (approved quotations have no lead time). */
  leadTimeDays: number | null;
  image: string;
  /** Stored image value (as saved; image is the display URL). */
  imageStored: string;
  createdBy: string;
  source: 'manual' | 'gq';
}

/** TMS procedures behind the product lists (hub module business-unit). */
export const SBU_PRODUCT_SP = {
  PRODUCTS: 'web_rd_get_sbu_products',
  GQ_APPROVED: 'web_rd_get_gq_approved_products',
  CATEGORIES: 'web_rd_get_prod_categories',
  INSERT: 'web_rd_ins_sbu_product',
  UPDATE: 'web_rd_upd_sbu_product',
  DELETE_BULK: 'web_rd_del_sbu_products_bulk',
} as const;

/** Rows of web_rd_get_sbu_products. */
export function mapSbuProducts(data: SpTables): SbuProduct[] {
  return (data['table0'] ?? []).map((r) => {
    const lead = Number(r['leadTimeDays']);
    return {
      id: String(r['id'] ?? r['recid']),
      recid: Number(r['recid']),
      name: r['name'] ?? '',
      styleCode: r['styleCode'] ?? '',
      sbu: r['sbu'] ?? '',
      country: r['country'] ?? '',
      countryCode: r['countryCode'] ?? '',
      category: r['category'] ?? '',
      fobPrice: Number(r['fobPrice']) || 0,
      costBreakdown: r['costBreakdown'] ?? '[]',
      leadTimeDays: r['leadTimeDays'] == null || Number.isNaN(lead) ? null : lead,
      image: resolveImageUrl(r['image']),
      imageStored: r['image'] ?? '',
      createdBy: r['createdBy'] ?? '',
      source: 'manual' as const,
    };
  });
}

/**
 * Rows of web_rd_get_gq_approved_products, with the cost lines TMS derived from BOM + CMT.
 * TMS showed ttl_production as the lead time; it is the production cost, so the lead time is left empty.
 */
export function mapGqProducts(data: SpTables): SbuProduct[] {
  const json = <T>(value: unknown, fallback: T): T => {
    try {
      return typeof value === 'string' && value ? (JSON.parse(value) as T) : fallback;
    } catch {
      return fallback;
    }
  };
  const round = (n: number) => +n.toFixed(2);
  return (data['table0'] ?? []).map((r) => {
    const t = calcTotals(json<GqBomItem[]>(r['bom_json'], []), json<GqCmtItem[]>(r['cmt_json'], []), parseFloat(r['rate_in_usd']) || 6.55);
    const quoted = parseFloat(r['fob_price']) || 0;
    const fob = quoted > 0 ? quoted : round(t.totalUSD);
    const breakdown: CostBreakdownItem[] = [
      { item: 'Fabric', amount: round(t.fabricTotalUSD) },
      { item: 'Trims', amount: round(t.trimTotalUSD) },
      { item: 'Other Cost', amount: round(t.othersTotalUSD) },
      { item: 'Fty FOB', amount: round(t.cmtTotalUSD) },
      { item: 'FTY Margin', amount: round(fob - t.totalUSD) },
    ];
    return {
      id: `gq_${r['qid']}`,
      name: r['model_name'] ?? r['style_id'] ?? '',
      styleCode: r['style_id'] ?? '',
      sbu: r['factory_name'] ?? '',
      country: r['country'] ?? '',
      countryCode: r['country_code'] ?? '',
      category: r['category'] ?? '',
      fobPrice: fob,
      costBreakdown: JSON.stringify(breakdown),
      leadTimeDays: null,
      image: resolveImageUrl(r['style_image']),
      imageStored: r['style_image'] ?? '',
      createdBy: '',
      source: 'gq' as const,
    };
  });
}
