import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable, shareReplay } from 'rxjs';
import { Paged, StyleListItem } from '../style-library/style-library.service';

// AI Studio (/api/v1/ai): smart search, change summary, BOM check and style renders on the Style Library.
// Reading: Admin, Merchandiser, Costing, Viewer. Making renders: Admin, Merchandiser (the API checks both).

/** Where AI requests go. leavesNetwork: the provider is a cloud service, so the data sent leaves LT. */
export interface AiProviderInfo {
  provider: string;
  model: string;
  isConfigured: boolean;
  leavesNetwork: boolean;
  supportsReferenceImage: boolean;
  /** 'settings' = Settings > AI connections; 'appsettings' = the server's configuration file. */
  source: string | null;
}

/** AI jobs: text and image are used today; the others are prepared for AI Lab capabilities. */
export type AiPurpose = 'text' | 'image' | 'embedding' | 'vision' | 'document' | 'prediction';
export const AI_PURPOSES: AiPurpose[] = ['text', 'image', 'embedding', 'vision', 'document', 'prediction'];

export interface AiPurposeStatus {
  purpose: AiPurpose;
  inUse: boolean;
  isConfigured: boolean;
  provider: string | null;
  model: string | null;
  leavesNetwork: boolean;
  source: string | null;
}

export interface AiStatus {
  text: AiProviderInfo;
  image: AiProviderInfo;
  purposes: AiPurposeStatus[];
}

// ----- Settings > AI connections (Admin) -----

export type AiConnectionKind = 'Gemini' | 'OpenAiCompatible' | 'Custom';

/** apiKeyHint: the saved key's last 4 characters (the key itself never reaches the browser). rowVer: base64. */
export interface AiConnectionItem {
  connectionId: number;
  name: string;
  kind: AiConnectionKind;
  endpoint: string;
  hasApiKey: boolean;
  apiKeyHint: string | null;
  inHouse: boolean;
  timeoutSeconds: number;
  notes: string | null;
  isActive: boolean;
  usedBy: AiPurpose[];
  updatedBy: string;
  updatedUtc: string;
  rowVer: string;
}

export interface AiSettings {
  connections: AiConnectionItem[];
  purposes: { purpose: AiPurpose; inUse: boolean; connectionId: number | null; connectionName: string | null; model: string | null; updatedBy: string | null; updatedUtc: string | null }[];
  /** What text / image use when not set here (appsettings). */
  fallbacks: { purpose: AiPurpose; provider: string; model: string; isConfigured: boolean; leavesNetwork: boolean }[];
  kinds: AiConnectionKind[];
}

/** apiKey: blank keeps the saved key; clearApiKey removes it. */
export interface SaveAiConnection {
  rowVer: string | null;
  name: string;
  kind: AiConnectionKind;
  endpoint: string;
  apiKey: string | null;
  clearApiKey: boolean;
  inHouse: boolean;
  timeoutSeconds: number;
  notes: string | null;
  isActive: boolean;
}

export interface AiTestResult {
  ok: boolean;
  message: string;
  models: string[];
}

// ----- Smart search -----

export interface StyleSearchFilters {
  search: string | null;
  material: string | null;
  customer: string | null;
  seasons: string[];
  businessUnit: string | null;
  productTypes: string[];
  weaveType: string | null;
  gender: string | null;
}

export interface StyleSearchResult {
  filters: StyleSearchFilters;
  explanation: string;
  results: Paged<StyleListItem>;
}

// ----- Change summary -----

export interface StyleRef {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  colorways: number;
  bomLines: number;
}

export interface FieldChange {
  field: string;
  before: string | null;
  after: string | null;
}

export interface ColorwayChange {
  change: 'Added' | 'Removed' | 'Changed' | 'Recoded';
  colorwayCode: string;
  colorwayName: string | null;
  fromCode: string | null;
  fields: FieldChange[];
}

export interface BomLineChange {
  change: 'Added' | 'Removed' | 'Changed' | 'Swapped';
  contentClassCode: string | null;
  contentClassName: string | null;
  partNo: number | null;
  materialCode: string;
  materialDescription: string | null;
  fromMaterialCode: string | null;
  fromMaterialDescription: string | null;
  fields: FieldChange[];
}

export interface StyleCompare {
  from: StyleRef;
  to: StyleRef;
  relation: 'CarryOver' | 'Variant' | null;
  header: FieldChange[];
  colorways: ColorwayChange[];
  bomLines: BomLineChange[];
  unchangedColorways: number;
  unchangedLines: number;
}

export interface CompareSummary {
  headline: string;
  summary: string;
  highlights: { area: string; text: string }[];
  checks: string[];
}

// ----- BOM check -----

export type Severity = 'High' | 'Medium' | 'Low';
export const BOM_RULES = ['NoConsumption', 'LcoBrandGap', 'PeerOutlier', 'MainFabricOutlier', 'FamilyDrift', 'LcoMissing', 'NoSupplier'] as const;
export type BomRule = (typeof BOM_RULES)[number];

export interface BomCheckQuery {
  styleId?: number | null;
  customer?: string | null;
  season?: string | null;
}

export interface BomCheckFinding {
  bomLineId: number;
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  lineSeq: number;
  partNo: number | null;
  materialCode: string;
  materialDescription: string | null;
  contentClassCode: string | null;
  uomCode: string | null;
  lcoConsumption: number | null;
  brandConsumption: number | null;
  ruleCode: BomRule;
  severity: Severity;
  value: number | null;
  refValue: number | null;
  peerCount: number | null;
  refStyleId: number | null;
  refStyleNo: string | null;
}

export interface BomCheck {
  stylesChecked: number;
  linesChecked: number;
  totalFindings: number;
  rules: { ruleCode: BomRule; severity: Severity; lines: number; styles: number }[];
  /** The first 500 findings, most severe first. */
  findings: BomCheckFinding[];
}

export interface BomCheckExplanation {
  summary: string;
  priorities: { bomLineId: number; why: string; action: string }[];
  ruleNotes: { ruleCode: BomRule; note: string }[];
}

// ----- Renders -----

export interface RenderFacts {
  garment: string | null;
  gender: string | null;
  construction: string | null;
  modelName: string | null;
  colorwayCode: string | null;
  colorwayName: string | null;
  fabrics: string[];
  colours: string[];
  details: string[];
}

export interface StyleRender {
  renderId: number;
  styleId: number;
  colorwayId: number | null;
  colorwayCode: string | null;
  imageUrl: string;
  prompt: string;
  provider: string;
  model: string;
  usedSketch: boolean;
  createdBy: string;
  createdUtc: string;
}

export interface RenderBrief {
  style: StyleRef;
  colorways: { colorwayId: number; colorwayCode: string; colorwayName: string | null; status: string }[];
  colorwayId: number | null;
  hasSketch: boolean;
  /** The sketch is uploaded in the app and the image model accepts reference images. */
  sketchUsable: boolean;
  facts: RenderFacts;
  prompt: string;
  renders: StyleRender[];
}

@Injectable({ providedIn: 'root' })
export class AiStudioService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/ai`;

  private status$?: Observable<AiStatus>;

  /** Provider status, cached for the session (it only changes with the server's settings). */
  status(): Observable<AiStatus> {
    return (this.status$ ??= this.http.get<AiStatus>(`${this.api}/status`).pipe(shareReplay({ bufferSize: 1, refCount: false })));
  }

  search(query: string): Observable<StyleSearchResult> {
    return this.http.post<StyleSearchResult>(`${this.api}/style-search`, { query });
  }

  compare(styleId: number, fromStyleId: number | null): Observable<StyleCompare> {
    let params = new HttpParams().set('styleId', styleId);
    if (fromStyleId) params = params.set('fromStyleId', fromStyleId);
    return this.http.get<StyleCompare>(`${this.api}/style-compare`, { params });
  }

  summarize(styleId: number, fromStyleId: number | null): Observable<CompareSummary> {
    return this.http.post<CompareSummary>(`${this.api}/style-compare/summary`, { styleId, fromStyleId });
  }

  bomCheck(query: BomCheckQuery): Observable<BomCheck> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) if (value) params = params.set(key, String(value));
    return this.http.get<BomCheck>(`${this.api}/bom-check`, { params });
  }

  explain(query: BomCheckQuery): Observable<BomCheckExplanation> {
    return this.http.post<BomCheckExplanation>(`${this.api}/bom-check/explain`, query);
  }

  renderBrief(styleId: number, colorwayId: number | null): Observable<RenderBrief> {
    const params = colorwayId ? new HttpParams().set('colorwayId', colorwayId) : undefined;
    return this.http.get<RenderBrief>(`${this.api}/style-render/${styleId}`, { params });
  }

  writePrompt(styleId: number, colorwayId: number | null): Observable<{ prompt: string }> {
    return this.http.post<{ prompt: string }>(`${this.api}/style-render/${styleId}/prompt`, { colorwayId });
  }

  render(styleId: number, colorwayId: number | null, prompt: string, useSketch: boolean): Observable<StyleRender> {
    return this.http.post<StyleRender>(`${this.api}/style-render/${styleId}`, { colorwayId, prompt, useSketch });
  }

  deleteRender(styleId: number, renderId: number): Observable<unknown> {
    return this.http.delete(`${this.api}/style-render/${styleId}/renders/${renderId}`);
  }

  /** Drops the cached status (after the AI settings change). */
  refreshStatus(): void {
    this.status$ = undefined;
  }

  // ----- Settings > AI connections (Admin) -----

  private readonly admin = `${environment.apiBaseUrl}/api/v1/admin/ai`;

  settings(): Observable<AiSettings> {
    return this.http.get<AiSettings>(this.admin);
  }

  saveConnection(connectionId: number | null, request: SaveAiConnection): Observable<{ connectionId: number }> {
    return connectionId === null
      ? this.http.post<{ connectionId: number }>(`${this.admin}/connections`, request)
      : this.http.put<{ connectionId: number }>(`${this.admin}/connections/${connectionId}`, request);
  }

  deleteConnection(c: AiConnectionItem): Observable<unknown> {
    return this.http.delete(`${this.admin}/connections/${c.connectionId}?rowVer=${encodeURIComponent(c.rowVer)}`);
  }

  savePurpose(purpose: AiPurpose, connectionId: number | null, model: string | null): Observable<unknown> {
    return this.http.put(`${this.admin}/purposes/${purpose}`, { connectionId, model });
  }

  /** Tests a saved connection or the form's draft (a blank key uses the saved one). */
  testConnection(request: { connectionId: number | null; kind: AiConnectionKind; endpoint: string; apiKey: string | null; timeoutSeconds: number }): Observable<AiTestResult> {
    return this.http.post<AiTestResult>(`${this.admin}/test`, request);
  }
}
