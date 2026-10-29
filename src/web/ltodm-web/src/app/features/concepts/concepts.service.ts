import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable, map, shareReplay } from 'rxjs';
import { Tone } from '@tms/shared/ui/tones';
import { ClosenessLevel, ClosenessThresholds } from '../style-packages/packages.service';

// Concepts (/api/v1/concepts): LT's own design ideas, worked up and checked for closeness to other brands' designs before they
// are offered in a package (roadmap 5.7, 5.1, 5.2). Reading: Style Library readers. Changing: Admin, Merchandiser.

export const CONCEPT_STATUSES = ['Draft', 'Ready', 'Dropped'] as const;
export type ConceptStatus = (typeof CONCEPT_STATUSES)[number];

export const CONCEPT_STATUS_TONE: Record<ConceptStatus, Tone> = { Draft: 'neutral', Ready: 'green', Dropped: 'neutral' };

/** Where an inspiration style was found: typed or picked, the AI matches, or the closeness check (pictures). */
export type ConceptStyleSource = 'Pick' | 'Match' | 'LookAlike';

export interface CodeName {
  code: string;
  name: string;
}

/** Settings > Reference lists: target markets, trend tags, product categories (active rows). */
export interface ConceptLookups {
  targetMarkets: CodeName[];
  trendTags: CodeName[];
  productCategories: CodeName[];
}

export interface ConceptListItem {
  conceptId: number;
  name: string;
  customerCode: string | null;
  customerName: string | null;
  seasonCode: string | null;
  productCategoryCode: string | null;
  productCategoryName: string | null;
  targetMarketName: string | null;
  gender: string | null;
  status: ConceptStatus;
  ownerName: string;
  images: number;
  styles: number;
  /** Packages with a style offered for the concept. */
  packages: number;
  /** Trend tag names. */
  tags: string | null;
  coverUrl: string | null;
  lastChangedUtc: string;
}

export interface ConceptFilter {
  search?: string;
  customer?: string;
  season?: string;
  category?: string;
  status?: string;
  mine?: boolean;
  skip?: number;
  take?: number;
}

/** The AI brief (as Concept Studio's): summary, suggested products, fabric direction, notes. */
export interface ConceptBrief {
  summary: string | null;
  products: string[];
  fabrics: string[];
  notes: string[];
  by: string | null;
  utc: string | null;
}

/** RowVer: base64; send back unchanged when saving or deleting. */
export interface ConceptHeader {
  conceptId: number;
  name: string;
  customerCode: string | null;
  customerName: string | null;
  brandCode: string | null;
  seasonCode: string | null;
  targetMarketCode: string | null;
  targetMarketName: string | null;
  productCategoryCode: string | null;
  productCategoryName: string | null;
  gender: string | null;
  targetPrice: string | null;
  description: string | null;
  status: ConceptStatus;
  ownerName: string;
  brief: ConceptBrief | null;
  createdBy: string;
  createdUtc: string;
  updatedBy: string | null;
  updatedUtc: string | null;
  rowVer: string;
  /** The story the client reads on the line sheet and share link (the idea in description stays LT's own). */
  clientStory: string | null;
}

/** hasVector: read by the image model now in use (the closeness check can compare it). */
export interface ConceptImage {
  conceptImageId: number;
  imageUrl: string;
  caption: string | null;
  sortOrder: number;
  hasVector: boolean;
  vectorError: string | null;
  addedBy: string;
  addedUtc: string;
  /** Shown on the story page for the client (only when the closeness check found it clear). */
  forClient: boolean;
}

/** sameBrand: the concept customer's own or its brand's (null when the concept has no customer). */
export interface ConceptStyle {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  brandCode: string | null;
  modelName: string | null;
  description: string | null;
  productTypeName: string | null;
  gender: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  isActive: boolean;
  sameBrand: boolean | null;
  note: string | null;
  addedFrom: ConceptStyleSource | null;
  addedBy: string;
  addedUtc: string;
}

export interface ConceptPackageItem {
  packageItemId: number;
  packageId: number;
  packageName: string;
  packageStatus: string;
  customerCode: string;
  styleId: number;
  styleNo: string;
  seasonCode: string;
  outcome: string | null;
}

/** An LT design developed from the concept (a style of its customer). originStyleId: another brand's style its BOM know-how came from. */
export interface DevelopedStyle {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  originStyleId: number | null;
  originStyleNo: string | null;
  createdBy: string;
  createdUtc: string;
  /** Package items it is in. */
  packages: number;
}

export interface ConceptDetail {
  concept: ConceptHeader;
  tags: CodeName[];
  images: ConceptImage[];
  styles: ConceptStyle[];
  packageItems: ConceptPackageItem[];
  developedStyles: DevelopedStyle[] | null;
}

/** baseStyleId: an inspiration style to start the BOM from; conceptImageId: the picture to use as the sketch. */
export interface DevelopStyle {
  seasonCode: string;
  styleNo: string;
  baseStyleId: number | null;
  conceptImageId: number | null;
}

/** A concept of a style (style page): kind Developed = the style is an LT design from it; Inspired = it inspired it. */
export interface StyleConcept {
  conceptId: number;
  name: string;
  customerCode: string | null;
  seasonCode: string | null;
  status: ConceptStatus;
  ownerName: string;
  note: string | null;
  kind: 'Developed' | 'Inspired';
}

export interface SaveConcept {
  rowVer: string | null;
  name: string;
  customerCode: string | null;
  seasonCode: string | null;
  targetMarketCode: string | null;
  productCategoryCode: string | null;
  gender: string | null;
  targetPrice: string | null;
  description: string | null;
  status: ConceptStatus;
  ownerName: string | null;
  trendTags: string[];
  clientStory: string | null;
}

/** A library style that looks like a concept picture; level is Clear for the concept customer's own brand. */
export interface ClosenessMatch {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  brandCode: string | null;
  modelName: string | null;
  productTypeName: string | null;
  source: string;
  imageUrl: string;
  sameBrand: boolean | null;
  similarity: number;
  level: ClosenessLevel;
}

/** status: Checked, NotRead (the image model is off or not installed) or Failed. */
export interface ConceptImageCloseness {
  conceptImageId: number;
  status: 'Checked' | 'NotRead' | 'Failed';
  error: string | null;
  level: ClosenessLevel;
  matches: ClosenessMatch[];
}

export interface ConceptCloseness {
  thresholds: ClosenessThresholds;
  level: ClosenessLevel;
  images: ConceptImageCloseness[];
}

export interface ConceptStyleAddResult {
  styleId: number;
  styleNo: string | null;
  seasonCode: string | null;
  customerCode: string | null;
  result: 'Added' | 'AlreadyIn' | 'NotFound';
}

/** What the library was searched for (from the concept's own fields; no AI). */
export interface MatchCriteria {
  customer: string | null;
  productTypes: string[];
  gender: string | null;
  weaveType: string | null;
  keywords: string[];
  explanation: string;
}

/** A library style scored against the concept (style.usp_ConceptMatch_Candidates). */
export interface MatchCandidate {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  description: string | null;
  productTypeName: string | null;
  gender: string | null;
  weaveTypeCode: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  score: number;
  matchedKeywords: string | null;
  mainFabrics: string | null;
  trims: string | null;
  colorwayCount: number;
}

/** sameBrand: the concept customer's brand (null when the concept has no customer). */
export interface LibraryMatches {
  criteria: MatchCriteria;
  matches: { style: MatchCandidate; sameBrand: boolean | null }[];
}

/** The AI's ranking of the scored styles: fit 0-100, why, what to reuse, what differs. */
export interface MatchExplanations {
  candidatesScored: number;
  matches: { style: MatchCandidate; fit: number; why: string; reuse: string; differs: string }[];
}

@Injectable({ providedIn: 'root' })
export class ConceptsService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/concepts`;
  private lookups$: Observable<ConceptLookups> | null = null;

  list(f: ConceptFilter): Observable<{ items: ConceptListItem[]; total: number }> {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(f)) if (v !== undefined && v !== null && v !== '' && v !== false) params = params.set(k, String(v));
    return this.http.get<{ items: ConceptListItem[]; total: number }>(this.api, { params });
  }

  /** Cached for the session; Settings > Reference lists changes show after a reload. */
  lookups(): Observable<ConceptLookups> {
    return (this.lookups$ ??= this.http.get<ConceptLookups>(`${this.api}/lookups`).pipe(shareReplay({ bufferSize: 1, refCount: false })));
  }

  get(conceptId: number): Observable<ConceptDetail> {
    return this.http.get<ConceptDetail>(`${this.api}/${conceptId}`);
  }

  forStyle(styleId: number): Observable<StyleConcept[]> {
    return this.http.get<StyleConcept[]>(`${this.api}/for-style/${styleId}`);
  }

  create(c: SaveConcept): Observable<number> {
    return this.http.post<{ conceptId: number }>(this.api, c).pipe(map((r) => r.conceptId));
  }

  update(conceptId: number, c: SaveConcept): Observable<number> {
    return this.http.put<{ conceptId: number }>(`${this.api}/${conceptId}`, c).pipe(map((r) => r.conceptId));
  }

  delete(conceptId: number, rowVer: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${conceptId}`, { params: { rowVer } });
  }

  /** Drafts the brief with AI from the concept as saved and keeps it; returns it with the concept's new RowVer. */
  draftBrief(conceptId: number): Observable<{ brief: ConceptBrief; rowVer: string }> {
    return this.http.post<{ brief: ConceptBrief; rowVer: string }>(`${this.api}/${conceptId}/brief`, {});
  }

  addImage(conceptId: number, blob: Blob, fileName: string, caption: string | null): Observable<{ conceptImageId: number; imageUrl: string }> {
    const form = new FormData();
    form.append('file', blob, fileName);
    if (caption) form.append('caption', caption);
    return this.http.post<{ conceptImageId: number; imageUrl: string }>(`${this.api}/${conceptId}/images`, form);
  }

  /** Shows a picture to the client or not (refused with 409 when it looks like another brand's design or is not checked). */
  saveImage(conceptId: number, conceptImageId: number, forClient: boolean, caption: string | null): Observable<void> {
    return this.http.put<void>(`${this.api}/${conceptId}/images/${conceptImageId}`, { forClient, caption });
  }

  removeImage(conceptId: number, conceptImageId: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/${conceptId}/images/${conceptImageId}`);
  }

  addStyles(conceptId: number, styleIds: number[], addedFrom: ConceptStyleSource): Observable<ConceptStyleAddResult[]> {
    return this.http.post<ConceptStyleAddResult[]>(`${this.api}/${conceptId}/styles`, { styleIds, addedFrom });
  }

  saveStyleNote(conceptId: number, styleId: number, note: string | null): Observable<void> {
    return this.http.put<void>(`${this.api}/${conceptId}/styles/${styleId}`, { note });
  }

  removeStyle(conceptId: number, styleId: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/${conceptId}/styles/${styleId}`);
  }

  /** Makes an LT design from the concept: a new style of its customer, sketch from a concept picture, BOM from an inspiration style. */
  develop(conceptId: number, d: DevelopStyle): Observable<{ styleId: number; sameBrandBase: boolean | null; sketchUrl: string | null }> {
    return this.http.post<{ styleId: number; sameBrandBase: boolean | null; sketchUrl: string | null }>(`${this.api}/${conceptId}/develop`, d);
  }

  /** Proven styles for the concept at once (its category, gender, customer and fabric words; no AI). */
  libraryMatches(conceptId: number): Observable<LibraryMatches> {
    return this.http.get<LibraryMatches>(`${this.api}/${conceptId}/library-matches`);
  }

  /** The AI ranks the scored styles and explains the best (one Text-job call; minutes on an in-house CPU server). */
  explainMatches(conceptId: number): Observable<MatchExplanations> {
    return this.http.post<MatchExplanations>(`${this.api}/${conceptId}/library-matches/explain`, {});
  }

  /** Brand closeness: reads pictures not read yet, then the library styles that look closest to each. */
  closeness(conceptId: number): Observable<ConceptCloseness> {
    return this.http.get<ConceptCloseness>(`${this.api}/${conceptId}/closeness`);
  }
}
