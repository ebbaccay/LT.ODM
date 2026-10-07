import { environment } from '@env/environment';

/**
 * A saved AI concept (web_rd_get_concept_studio_results). Array fields (active_tags, suggested_products, ...)
 * are JSON strings in the database; parse them with parseList().
 */
export interface ConceptStudioResult {
  recid: number;
  concept_name: string;
  customer: string;
  customer_id: string;
  season: string;
  season_id: string;
  target_market: string;
  fob_price: string;
  /** JSON string, e.g. '["Sustainable","Athleisure"]' */
  active_tags: string;
  concept_brief: string;
  suggested_products: string;
  fabric_direction: string;
  sustainability_notes: string;
  /** JSON string of image URLs (LT ODM uploads: /api/v1/concept-studio/images/...) */
  inspiration_images: string;
  createdBy: string;
  username?: string;
  location: string;
  user_group: string;
  userGroup?: string;
  status: string;
  created_date: string;
  modified_date: string | null;
  /** The signed-in user may edit or delete it (its creator, or an Admin). */
  can_edit?: boolean;
}

/**
 * Which saved concepts to list: own, the team's (same user group + location), or every concept.
 * The API decides who may use 'all' (Admins and factory users); anyone else gets 'team'.
 */
export type ConceptScope = 'mine' | 'team' | 'all';

export interface TrendTag {
  tag_id: number;
  tag_name: string;
  sort_order: number;
}

export interface TargetMarket {
  market_id: number;
  market_name: string;
  sort_order: number;
}

/** A normalised SBU / approved GQ product used for FOB-based matching. */
export interface SbuMatchProduct {
  id: string;
  name: string;
  styleCode: string;
  sbu: string;
  fobPrice: number;
  category: string;
  image: string;
}

/** AI concept brief (POST /api/v1/concept-studio/draft). */
export interface ConceptDraft {
  summary: string;
  products: string[];
  fabrics: string[];
  notes: string[];
}

export interface ConceptDraftRequest {
  name: string;
  client: string;
  season: string;
  targetMarket: string;
  targetPrice: string;
  trends: string[];
}

/** TMS procedure names (the hub ignores any database prefix). */
export const CONCEPT_STUDIO_SP = {
  READ: 'web_rd_get_concept_studio_results',
  INSERT: 'web_rd_ins_concept_studio_result',
  UPDATE: 'web_rd_upd_concept_studio_result',
  DELETE: 'web_rd_del_concept_studio_result',
  DELETE_COLLECTION_BUILDER: 'web_rd_del_collection_builder',
  GET_CUSTOMERS: 'web_rd_get_customers',
  GET_SEASONS: 'web_rd_get_seasons',
  GET_TAGS: 'web_rd_get_cs_trend_tags',
  INS_TAG: 'web_rd_ins_cs_trend_tag',
  DEL_TAG: 'web_rd_del_cs_trend_tag',
  GET_MARKETS: 'web_rd_get_cs_target_markets',
  INS_MARKET: 'web_rd_ins_cs_target_market',
  DEL_MARKET: 'web_rd_del_cs_target_market',
  SBU_PRODUCTS: 'web_rd_get_sbu_products',
  GQ_APPROVED: 'web_rd_get_gq_approved_products',
} as const;

/** Safely parses a JSON array string (also accepts a JSON string that holds a JSON array, as older rows do). */
export function parseList(json: string | null | undefined): string[] {
  if (!json) return [];
  try {
    let parsed: unknown = JSON.parse(json);
    if (typeof parsed === 'string') parsed = JSON.parse(parsed);
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string' && x.trim() !== '').map((x) => x.trim()) : [];
  } catch {
    return json.trim() && !json.trim().startsWith('[') ? [json.trim()] : [];
  }
}

/**
 * Image URL for display. LT ODM uploads (/api/...) are loaded with the sign-in token (AuthImgDirective);
 * TMS file-server paths (uploads/...) go to environment.fileServerUrl.
 */
export function resolveImageUrl(url: string | null | undefined): string {
  const cleaned = (url ?? '').trim().replace(/^['"]+|['"]+$/g, '');
  if (!cleaned) return '';
  if (cleaned.startsWith('/api/') || cleaned.startsWith('blob:') || cleaned.startsWith('data:')) return cleaned;
  if (/^https?:\/\//.test(cleaned)) return cleaned;
  if (cleaned.includes('/uploads/') || cleaned.startsWith('uploads/')) {
    return `${environment.fileServerUrl}/uploads/${cleaned.split('/uploads/').pop()!.replace(/^uploads\//, '')}`;
  }
  return `${environment.fileServerUrl}${cleaned.startsWith('/') ? cleaned : `/${cleaned}`}`;
}
