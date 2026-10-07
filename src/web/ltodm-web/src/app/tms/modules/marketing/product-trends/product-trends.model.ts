export type RegionId = 'na' | 'eu' | 'as' | 'sa' | 'af' | 'oce';
export type Strength = 'Strong' | 'Moderate' | 'Emerging' | 'Declining';
export type TagType = 'growing' | 'emerging' | 'declining' | 'restocked';

export interface CollectionPick {
  recid: number;
  concept_name: string;
  customer: string;
  season: string;
  styleCount: number;
}

export interface TrendHeader {
  collectionRecid: number;
  conceptName: string;
  customer: string;
  season: string;
  targetMarket: string;
  targetFob: number;
  activeTags: string;
  fabricDirection: string;
  sustainabilityNotes: string;
}

export interface TrendStyle {
  itemRecid: number;
  productName: string;
  styleCode: string;
  category: string;
  sbu: string;
  country: string;
  imageUrl: string;
  fobPrice: number;
}

export interface RegionScore {
  regionId: RegionId;
  trendLabel: string;
  subLabel: string;
  strength: Strength;
  /** AI estimate of year-on-year demand growth (%), not market data. */
  growthPct: number;
}

export interface StyleScore {
  itemRecid: number;
  trendScore: number;
  trendTags: string[];
  insight: string;
  tagType: TagType;
  /** AI estimate (%), not market data. */
  growthPct: number;
}

/** POST /api/v1/market-trends/analysis */
export interface TrendAnalysis {
  regions: { id: RegionId; label: string; subLabel: string; strength: Strength; growthPct: number }[];
  styles: StyleScore[];
  insights: string[];
}

export const TREND_SP = {
  COLLECTIONS: 'web_rd_ct_list_collections',
  LOAD_SESSION: 'web_rd_ct_load_session',
  STYLES: 'web_rd_ct_get_collection_styles',
  CLEAR: 'web_rd_ct_clear_scores',
  SAVE_REGION: 'web_rd_ct_save_region_score',
  SAVE_STYLE: 'web_rd_ct_save_style_score',
  MARK_COMPLETE: 'web_rd_ct_mark_complete',
  REGION_SCORES: 'web_rd_ct_get_region_scores',
  STYLE_SCORES: 'web_rd_ct_get_style_scores',
} as const;

/** Colour TMS stored with each region score (kept so TMS-era data and new rows look the same in the table). */
export const STRENGTH_HEX: Record<Strength, string> = {
  Strong: '#3470c8',
  Moderate: '#2ab89a',
  Emerging: '#8fc4d8',
  Declining: '#ef4444',
};
