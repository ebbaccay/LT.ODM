import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';
import { AiJobsService } from '../../core/ai/ai-jobs.service';

// Fill the gaps (/api/v1/style-gaps) and Similar styles (/api/v1/styles/{id}/similar).
// Reading: Style Library readers. Accepting, dismissing, merging, filling and asking AI: Admin, Merchandiser (the API checks).

export type GapField = 'gender' | 'productType';
export type GapShow = 'suggested' | 'none' | 'all';

/** What the library is missing; the *Suggested / *High counts are null on the home dashboard. */
export interface GapSummary {
  styles: number;
  genderMissing: number;
  genderSuggested: number | null;
  genderHigh: number | null;
  productTypeMissing: number;
  productTypeSuggested: number | null;
  productTypeHigh: number | null;
  seasonsToMerge: number;
  stylesInSeasonsToMerge: number;
  leadTimeMissing: number;
  lcoMissing: number;
  lcoFromBrand: number;
  lcoEstimates: number;
  supplierMissing: number;
}

/** A style missing the field; value null = no suggestion. confidence 3 High, 2 Medium, 1 Check, 0 none. */
export interface GapItem {
  styleId: number;
  customerCode: string;
  account: string;
  seasonCode: string;
  styleNo: string;
  description: string | null;
  modelName: string | null;
  weaveTypeCode: string | null;
  gender: string | null;
  productTypeName: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  value: string | null;
  valueName: string | null;
  category: string | null;
  confidence: number;
  reason: string | null;
  source: 'Rule' | 'AI' | null;
}

export interface GapPage {
  items: GapItem[];
  total: number;
  /** Styles in the scope by confidence: "3", "2", "1", "0" (no suggestion). */
  counts: Record<string, number>;
}

export interface GapFilter {
  customer?: string;
  season?: string;
  show: GapShow;
  minConfidence: number;
  skip: number;
  take: number;
}

export interface GapValue {
  styleId: number;
  value: string;
}

export interface AskAiResult {
  saved: number;
  unclear: number;
  remaining: number;
}

export interface GapSeason {
  seasonCode: string;
  seasonYear: number | null;
  term: string | null;
  /** The year-term season the name shows (null for quarters and other names). */
  suggested: string | null;
  styles: number;
  /** Styles that cannot move there: the customer already has the same number in it. */
  conflicts: number;
  suggestedExists: boolean;
}

export type LcoSource = 'Brand' | 'BrandAllowance' | 'SameProductType' | 'SameMaterial';

export interface LcoLine {
  bomLineId: number;
  lineSeq: number;
  materialCode: string;
  materialDescription: string | null;
  contentClassCode: string | null;
  uomCode: string | null;
  brandConsumption: number | null;
  consumptionAllowance: number | null;
  suggested: number | null;
  source: LcoSource | null;
  peers: number | null;
}

export interface LcoGroup {
  customerCode: string;
  account: string;
  seasonCode: string;
  lines: number;
  suggested: number;
  styles: number;
}

export interface ApplyLco {
  bomLineIds?: number[];
  styleId?: number;
  customer?: string;
  season?: string;
  all?: boolean;
  withAllowance: boolean;
}

/** score 0-100; sameBrand false = another brand's style (know-how only, never pitched). */
export interface SimilarStyle {
  styleId: number;
  customerCode: string;
  customerName: string;
  seasonCode: string;
  styleNo: string;
  description: string | null;
  modelName: string | null;
  productTypeName: string | null;
  gender: string | null;
  weaveTypeCode: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  isActive: boolean;
  sameBrand: boolean;
  sameGarment: boolean;
  sharedMaterials: number;
  sharedFabrics: number;
  score: number;
  mainFabric: string | null;
}

@Injectable({ providedIn: 'root' })
export class StyleGapsService {
  private readonly http = inject(HttpClient);
  private readonly jobs = inject(AiJobsService);
  private readonly api = `${environment.apiBaseUrl}/api/v1/style-gaps`;

  summary(): Observable<GapSummary> {
    return this.http.get<GapSummary>(`${this.api}/summary`);
  }

  list(field: GapField, f: GapFilter): Observable<GapPage> {
    let params = new HttpParams().set('show', f.show).set('minConfidence', f.minConfidence).set('skip', f.skip).set('take', f.take);
    if (f.customer) params = params.set('customer', f.customer);
    if (f.season) params = params.set('season', f.season);
    return this.http.get<GapPage>(`${this.api}/${field}`, { params });
  }

  /** items: these values; without items: every suggestion in the scope with at least minConfidence. */
  accept(field: GapField, body: { items?: GapValue[]; customer?: string; season?: string; minConfidence?: number }): Observable<{ applied: number }> {
    return this.http.post<{ applied: number }>(`${this.api}/${field}/accept`, body);
  }

  dismiss(field: GapField, items: GapValue[]): Observable<{ dismissed: number }> {
    return this.http.post<{ dismissed: number }>(`${this.api}/${field}/dismiss`, { items });
  }

  /** Asks the AI about the next 25 styles the rules could not read (one Text-job call, as an AI job: answers are kept even if the page is left). */
  askAi(field: GapField, customer?: string, season?: string): Observable<AskAiResult> {
    return this.jobs.run(this.http.post<{ jobId: number }>(`${this.api}/${field}/ask-ai`, { customer: customer || null, season: season || null }));
  }

  seasons(): Observable<GapSeason[]> {
    return this.http.get<GapSeason[]>(`${this.api}/seasons`);
  }

  mergeSeason(from: string, to: string): Observable<{ moved: number; conflicts: number }> {
    return this.http.post<{ moved: number; conflicts: number }>(`${this.api}/seasons/merge`, { from, to });
  }

  lcoSummary(withAllowance: boolean): Observable<LcoGroup[]> {
    return this.http.get<LcoGroup[]>(`${this.api}/lco`, { params: { withAllowance } });
  }

  lcoForStyle(styleId: number, withAllowance: boolean): Observable<LcoLine[]> {
    return this.http.get<LcoLine[]>(`${this.api}/lco/styles/${styleId}`, { params: { withAllowance } });
  }

  applyLco(body: ApplyLco): Observable<{ updated: number; styles: number }> {
    return this.http.post<{ updated: number; styles: number }>(`${this.api}/lco/apply`, body);
  }

  similar(styleId: number, top = 8): Observable<SimilarStyle[]> {
    return this.http.get<SimilarStyle[]>(`${environment.apiBaseUrl}/api/v1/styles/${styleId}/similar`, { params: { top } });
  }
}
