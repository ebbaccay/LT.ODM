import type { CostBreakdownItem, SbuProduct } from '@tms/shared/sbu-products';
import { Tone } from '@tms/shared/ui/tones';

/**
 * A product offered against a Concept Studio brief (tms_sbu_submissions).
 * Product fields (name, style code, image, FOB, cost breakdown) are a snapshot taken when the submission is
 * created, so the offer stays as it was made even if the product changes later.
 *
 * Status flow: Draft -> Submitted -> Accepted | Rejected,
 *              Submitted -> For Review (sent to the factory) -> Factory Submitted -> Accepted | Rejected | For Review.
 */
export interface SbuSubmission {
  id: string;
  recid: number;
  conceptRecid: number;
  conceptName: string;
  productRecid: number;
  productName: string;
  styleCode: string;
  sbu: string;
  imageUrl: string;
  fobPrice: number;
  /** JSON: CostBreakdownItem[] or a cost-optimization record { costOptimization, originalCosts, aiSuggestions, originalFob } */
  costBreakdown: string;
  targetFob: number;
  notes: string;
  status: SubmissionStatus;
  statusReason: string;
  username: string;
  location: string;
  userGroup: string;
  createdDate: string;
  modifiedDate: string | null;
  submittedDate: string | null;
}

export type SubmissionStatus = 'Draft' | 'Submitted' | 'Accepted' | 'Rejected' | 'For Review' | 'Factory Submitted';

export type { CostBreakdownItem } from '@tms/shared/sbu-products';

/** A product that can be offered: an SBU catalog product or an approved garment quotation. */
/** A product that can be offered (SBU catalog or approved quotation). */
export type OfferProduct = SbuProduct;

export const SBU_SUBMISSION_SP = {
  READ: 'web_rd_get_sbu_submissions',
  INSERT: 'web_rd_ins_sbu_submission',
  UPDATE: 'web_rd_upd_sbu_submission',
  DELETE: 'web_rd_del_sbu_submission',
  SUBMIT: 'web_rd_submit_sbu_submission',
  FTY_PROPOSAL: 'web_rd_co_fty_submit_proposal',
  CONCEPTS: 'web_rd_get_concept_studio_results',
  PRODUCTS: 'web_rd_get_sbu_products',
  GQ_APPROVED: 'web_rd_get_gq_approved_products',
} as const;

export const STATUS_TONE: Record<SubmissionStatus, Tone> = {
  Draft: 'neutral',
  Submitted: 'primary',
  Accepted: 'green',
  Rejected: 'red',
  'For Review': 'amber',
  'Factory Submitted': 'violet',
};

interface CostOptRecord {
  costOptimization?: boolean;
  originalCosts?: CostBreakdownItem[];
  aiSuggestions?: CostBreakdownItem[];
  originalFob?: number;
}

function parse(json: string | null | undefined): unknown {
  try {
    return json ? JSON.parse(json) : null;
  } catch {
    return null;
  }
}

const items = (value: unknown): CostBreakdownItem[] =>
  Array.isArray(value)
    ? value.filter((i) => i && typeof i === 'object').map((i) => ({ item: String(i.item ?? ''), amount: Number(i.amount) || 0 }))
    : [];

/** Cost lines of a submission or product (for a cost-optimization record: the original costs). */
export function costItems(json: string | null | undefined): CostBreakdownItem[] {
  const parsed = parse(json);
  if (Array.isArray(parsed)) return items(parsed);
  const rec = parsed as CostOptRecord | null;
  return rec?.costOptimization ? items(rec.originalCosts) : [];
}

/** AI savings suggestions of a cost-optimization record (empty for normal submissions). */
export function costOptSuggestions(json: string | null | undefined): CostBreakdownItem[] {
  const rec = parse(json) as CostOptRecord | null;
  return rec?.costOptimization ? items(rec.aiSuggestions) : [];
}

/** Original vs projected FOB for cost-optimization records, else null. */
export function costOptFob(sub: SbuSubmission): { originalFob: number; projectedFob: number; savings: number; savingsPct: number } | null {
  const rec = parse(sub.costBreakdown) as CostOptRecord | null;
  if (!rec?.costOptimization) return null;
  const originalFob = Number(rec.originalFob) || 0;
  const savings = originalFob - sub.fobPrice;
  return { originalFob, projectedFob: sub.fobPrice, savings, savingsPct: originalFob > 0 ? (savings / originalFob) * 100 : 0 };
}
