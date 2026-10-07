import { Tone } from '@tms/shared/ui/tones';

export const SBU_PERF_SP = { READ: 'web_rd_get_sbu_performance' } as const;

export type QuoteStatus = 'Approved' | 'Pending' | 'Draft';
export type LeadStatus = 'faster' | 'inLine' | 'slower';
export type SortKey = 'overall' | 'cost' | 'delivery' | 'quality' | 'products';

/** One factory or SBU office as scored on this screen. Scores are 6–10 (higher is better); null = no data. */
export interface FactoryScore {
  id: string;
  name: string;
  country: string;
  countryCode: string;
  /** 'catalog' = from SBU overview products; 'gq' = a factory known only from garment quotations. */
  source: 'catalog' | 'gq';
  image: string;
  products: number;
  quotations: number;
  avgFob: number | null;
  leadDays: number | null;
  leadStatus: LeadStatus | null;
  /** Quotation status for 'gq' rows; null for catalog rows (they have no quotations). */
  status: QuoteStatus | null;
  cost: number | null;
  delivery: number | null;
  quality: number | null;
  /** 0–100, weighted cost 45 % / delivery 35 % / quality 20 %; missing parts' weight is shared out. Null = no data at all. */
  overall: number | null;
  highlights: { key: string; params?: Record<string, unknown> }[];
}

export interface CountrySummary {
  country: string;
  factories: number;
  products: number;
  /** Average of the factories that have a score; null when none has. */
  overall: number | null;
}

export const STATUS_TONE: Record<QuoteStatus, Tone> = { Approved: 'green', Pending: 'amber', Draft: 'neutral' };

const num = (v: unknown): number | null => {
  const n = Number(v);
  return v === null || v === undefined || v === '' || Number.isNaN(n) ? null : n;
};
const round1 = (n: number) => Math.round(n * 10) / 10;

/** 10 for the best (lowest) value down to 6 for the worst, as in the TMS procedure; 8 when all are equal. */
function inverseScale(values: number[]): (v: number | null) => number | null {
  if (!values.length) return () => null;
  const min = Math.min(...values);
  const max = Math.max(...values);
  return (v) => (v === null ? null : max === min ? 8 : round1(10 - ((v - min) / (max - min)) * 4));
}

export function overallScore(cost: number | null, delivery: number | null, quality: number | null): number | null {
  let sum = 0;
  let weight = 0;
  for (const [score, w] of [[cost, 0.45], [delivery, 0.35], [quality, 0.2]] as const) {
    if (score !== null) {
      sum += score * w;
      weight += w;
    }
  }
  return weight ? Math.round((sum / weight) * 10) : null;
}

/**
 * Scores the rows of web_rd_get_sbu_performance.
 *
 * The procedure reads a quotation's ttl_production (the total production cost) as a lead time in days, so
 * quotation-only factories got made-up delivery scores and skewed everyone else's. Here lead times come from
 * catalog products only, and cost and delivery are re-scaled with the procedure's own formula.
 */
export function scoreRows(rows: Record<string, any>[]): FactoryScore[] {
  const base = rows.map((r, i) => {
    const products = num(r['product_count']) ?? 0;
    const source: FactoryScore['source'] = products > 0 ? 'catalog' : 'gq';
    const fob = num(r['avg_fob']);
    const lead = source === 'catalog' ? num(r['avg_lead_days']) : null;
    return {
      r,
      i,
      source,
      products,
      avgFob: fob && fob > 0 ? fob : null,
      leadDays: lead && lead > 0 ? lead : null,
    };
  });
  const costOf = inverseScale(base.map((b) => b.avgFob).filter((v): v is number => v !== null));
  const leads = base.map((b) => b.leadDays).filter((v): v is number => v !== null);
  const deliveryOf = inverseScale(leads);
  const avgLead = leads.length ? leads.reduce((s, v) => s + v, 0) / leads.length : 0;

  return base.map(({ r, i, source, products, avgFob, leadDays }) => {
    const cost = costOf(avgFob);
    const delivery = deliveryOf(leadDays);
    const q = num(r['quality_score']);
    const quality = q === null ? null : round1(q);
    const quotations = num(r['order_count']) ?? 0;
    const ratio = leadDays && avgLead ? leadDays / avgLead : null;
    const status = source === 'gq' ? ((['Approved', 'Pending', 'Draft'] as const).find((s) => s === r['gq_status']) ?? 'Draft') : null;

    const highlights: FactoryScore['highlights'] = [];
    if (cost !== null && cost >= 9.5) highlights.push({ key: 'sp.hl.lowestFob' });
    else if (cost !== null && cost >= 8.5) highlights.push({ key: 'sp.hl.competitiveFob' });
    if (delivery !== null && delivery >= 9.5) highlights.push({ key: 'sp.hl.fastestLead' });
    else if (delivery !== null && delivery >= 8.5) highlights.push({ key: 'sp.hl.fastLead' });
    if (quality !== null && quality >= 9) highlights.push({ key: 'sp.hl.highApproval' });
    if (products >= 5) highlights.push({ key: 'sp.hl.wideRange', params: { count: products } });

    return {
      id: `${r['country_code'] ?? ''}-${r['sbu_name'] ?? ''}-${i}`,
      name: String(r['sbu_name'] ?? ''),
      country: String(r['country'] ?? ''),
      countryCode: String(r['country_code'] ?? ''),
      source,
      image: String(r['latest_image'] ?? ''),
      products,
      quotations,
      avgFob,
      leadDays: leadDays === null ? null : Math.round(leadDays),
      leadStatus: ratio === null ? null : ratio < 0.9 ? 'faster' : ratio > 1.1 ? 'slower' : 'inLine',
      status,
      cost,
      delivery,
      quality,
      overall: overallScore(cost, delivery, quality),
      highlights,
    };
  });
}

export function countrySummaries(rows: FactoryScore[]): CountrySummary[] {
  const by = new Map<string, FactoryScore[]>();
  for (const r of rows) by.set(r.country, [...(by.get(r.country) ?? []), r]);
  return [...by.entries()]
    .map(([country, list]) => {
      const scored = list.map((r) => r.overall).filter((v): v is number => v !== null);
      return {
        country,
        factories: list.length,
        products: list.reduce((s, r) => s + r.products, 0),
        overall: scored.length ? Math.round(scored.reduce((s, v) => s + v, 0) / scored.length) : null,
      };
    })
    .sort((a, b) => (b.overall ?? -1) - (a.overall ?? -1));
}
