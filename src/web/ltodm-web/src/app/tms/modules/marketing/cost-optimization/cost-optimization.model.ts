export type CostCategory = 'Fabric' | 'Trim' | 'Labor' | 'Overhead';
export type CostLabel = CostCategory | 'Margin';
export const COST_LABELS: CostLabel[] = ['Fabric', 'Trim', 'Labor', 'Overhead', 'Margin'];

/** A style in the concept's collection (web_rd_get_collection_builder_items). */
export interface CostStyle {
  /** Session key: the GQ quotation id, or "item-{itemRecid}" for SBU offers (as in TMS). */
  qid: string;
  itemRecid: number;
  conceptRecid: number;
  styleName: string;
  styleId: string;
  factoryName: string;
  currentFob: number;
  imageUrl: string;
  costBreakdown: string;
  hasSession: boolean;
  sessionStatus: string;
}

export interface CollectionHeader {
  collectionRecid: number;
  conceptRecid: number;
  conceptName: string;
  customer: string;
  season: string;
  targetFob: number;
  fabricDirection: string;
}

export interface CostSession {
  id: number;
  /** draft | under_review | accepted | rejected */
  status: string;
  reviewStatus: '' | 'pending' | 'approved' | 'rejected' | 'recalled';
  sentToName: string;
  reviewerComment: string;
}

export interface Suggestion {
  /** suggestion_id in tms_cost_opt_suggestions */
  id: number;
  category: CostCategory;
  title: string;
  detail: string;
  fromPrice: number;
  toPrice: number;
  saving: number;
  applied: boolean;
}

/** AI answer from POST /api/v1/cost-optimization/suggestions (saved to the session before it is shown). */
export interface SuggestionIdea {
  category: CostCategory;
  title: string;
  detail: string;
  fromPrice: number;
  toPrice: number;
  saving: number;
}

export const CO_SP = {
  CONCEPTS: 'web_rd_get_concept_studio_results',
  HEADER: 'web_rd_get_collection_builder',
  ITEMS: 'web_rd_get_collection_builder_items',
  LOAD_SESSION: 'web_rd_co_load_session',
  GET_SUGGESTIONS: 'web_rd_co_get_suggestions',
  CLEAR_SUGGESTIONS: 'web_rd_co_clear_suggestions',
  SAVE_SUGGESTION: 'web_rd_co_save_suggestion',
  TOGGLE_SUGGESTION: 'web_rd_co_toggle_suggestion',
  SUBMIT_REVIEW: 'web_rd_co_submit_review',
  INS_SBU_REVIEW: 'web_rd_co_ins_sbu_review',
  RECALL_REVIEW: 'web_rd_co_recall_review',
  FACTORIES: 'web_rd_get_factory_list',
} as const;

/**
 * Maps a cost breakdown snapshot ([{ item, amount }]) to the five simulator lines (TMS rules), with one fix:
 * approved quotations call the CMT line "Fty FOB", which TMS did not recognise, so their labour cost was lost.
 * Margin is taken from the breakdown, or is what is left of the FOB.
 */
export function costLines(breakdown: { item: string; amount: number }[], totalFob: number): Record<CostLabel, number> {
  const find = (...keys: string[]) =>
    breakdown.find((b) => keys.some((k) => String(b.item ?? '').toLowerCase().includes(k)))?.amount ?? 0;
  const fabric = Number(find('fabric')) || 0;
  const trim = Number(find('trim')) || 0;
  const labor = Number(find('labor', 'labour', 'cmt', 'make', 'sewing', 'fty fob')) || 0;
  const overhead = Number(find('overhead', 'other')) || 0;
  const margin = Number(find('margin')) || Math.max(0, totalFob - fabric - trim - labor - overhead);
  const r = (n: number) => +n.toFixed(2);
  return { Fabric: r(fabric), Trim: r(trim), Labor: r(labor), Overhead: r(overhead), Margin: r(margin) };
}

/** Result status column: TMS cost procedures return resultStatus, others result_status. */
export const resultOk = (row: Record<string, unknown> | undefined) => (row?.['resultStatus'] ?? row?.['result_status']) === 'Success';
export const resultText = (row: Record<string, unknown> | undefined) => String(row?.['resultStatus'] ?? row?.['result_status'] ?? '');
