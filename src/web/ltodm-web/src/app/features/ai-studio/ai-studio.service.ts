import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable, shareReplay } from 'rxjs';
import { AiJobsService } from '../../core/ai/ai-jobs.service';
import { Paged, StyleListItem } from '../style-library/style-library.service';

// AI Studio (/api/v1/ai): smart search, change summary, BOM check and style renders on the Style Library.
// Reading: Admin, Merchandiser, Costing, Viewer. Making renders: Admin, Merchandiser (the API checks both).

/** Where AI requests go. leavesNetwork: the provider is a cloud service, so the data sent leaves LT. */
/** aiOff: all AI turned off. jobOff: this job turned off. cloudBlocked: the job's service is outside LT and cloud AI is not allowed. */
export type AiBlock = 'aiOff' | 'jobOff' | 'cloudBlocked';

export interface AiProviderInfo {
  provider: string;
  model: string;
  isConfigured: boolean;
  leavesNetwork: boolean;
  supportsReferenceImage: boolean;
  /** 'settings' = Settings > AI connections; 'appsettings' = the server's configuration file. */
  source: string | null;
  /** Why the job cannot run although a service is set: the central switches (Settings > AI connections). */
  blocked?: AiBlock | null;
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
  /** Why the job cannot run although a service is set: the central switches (Settings > AI connections). */
  blocked?: AiBlock | null;
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
  /** Calls this server gets at once while AI call limits are on. */
  maxConcurrent: number;
  usedBy: AiPurpose[];
  updatedBy: string;
  updatedUtc: string;
  rowVer: string;
}

export interface AiSettings {
  connections: AiConnectionItem[];
  purposes: {
    purpose: AiPurpose;
    inUse: boolean;
    connectionId: number | null;
    connectionName: string | null;
    model: string | null;
    updatedBy: string | null;
    updatedUtc: string | null;
    /** The job is turned off (no app default either). */
    disabled: boolean;
    blocked: AiBlock | null;
  }[];
  /** What text / image use when not set here (appsettings). */
  fallbacks: { purpose: AiPurpose; provider: string; model: string; isConfigured: boolean; leavesNetwork: boolean }[];
  kinds: AiConnectionKind[];
  /** The central switches. allowCloud false = nothing is sent to services outside LT. */
  policy: {
    aiEnabled: boolean;
    allowCloud: boolean;
    updatedBy: string | null;
    updatedUtc: string | null;
    /** AI call limits: calls at once per AI server (set per connection) and AI tasks per user. */
    limitCalls: boolean;
    maxTasksPerUser: number;
  };
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
  maxConcurrent: number;
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
  /** Picture tags (hood:hooded, colour:navy). */
  looks?: string[] | null;
}

export interface StyleSearchResult {
  filters: StyleSearchFilters;
  /** The AI's reading; null when the phrase list understood the request. */
  explanation: string | null;
  results: Paged<StyleListItem>;
  /** rules: the phrase list understood it (no AI call). ai: the AI read it. */
  /** rules: phrase list. look: matched by how the pictures look (built-in image model). ai: the AI read it. */
  source: 'rules' | 'look' | 'ai';
  /** Every word was understood but no style met them all: matched by the pictures instead (source look). */
  phraseListFoundNothing?: boolean;
  /** Words the phrase list did not understand, ignored because AI was not used (or its reading found nothing). */
  unmatched: string[];
  /** The AI read the request but its criteria found no style: the phrase list's result is shown (source rules). */
  aiFoundNothing?: boolean;
}

/** Words understood without AI, by criterion, and example requests that find styles today. */
export interface StyleSearchPhrases {
  examples: string[];
  groups: { criterion: string; words: string[] }[];
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
  /** Read from the uploaded sketch by the built-in image model: "long sleeves", "a high neck", "maxi length"... */
  shape: string[] | null;
  /** The sketch has front and back drawn apart. */
  frontAndBack: boolean;
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

// ----- Image search -----

export type ImageSource = 'Photo' | 'Sketch' | 'Colorway' | 'Render';

export interface ImageIndexSourceCount {
  source: ImageSource;
  pictures: number;
  indexed: number;
  failed: number;
  toIndex: number;
}

/**
 * Look-alike vectors from the image model built into the API (search by photo, more like this). problem: why it cannot
 * run ('aiOff' when AI features are switched off, otherwise a sentence), null when it can.
 */
export interface LookAlikeStatus {
  isAvailable: boolean;
  model: string;
  problem: string | null;
  sources: ImageIndexSourceCount[];
  pictures: number;
  indexed: number;
  failed: number;
  toIndex: number;
  /** The model's text part is installed too: words are searched by look. */
  supportsWords: boolean;
}

/** Descriptions (Vision + Embedding jobs; search by words) at the top level, look-alike vectors in lookAlike. */
export interface ImageSearchStatus {
  vision: AiProviderInfo;
  embedding: AiProviderInfo;
  dimensions: number;
  sources: ImageIndexSourceCount[];
  pictures: number;
  indexed: number;
  failed: number;
  toIndex: number;
  lookAlike: LookAlikeStatus;
}

/** step: what this round did. remaining covers both steps. missing: pictures whose file is not on the server (stay to do). */
export interface IndexImagesResult {
  indexed: number;
  failed: number;
  remaining: number;
  step: 'lookAlike' | 'descriptions' | 'none';
  missing: number;
}

/** How the vision model read an uploaded photo. */
export interface ImageReading {
  garment: string | null;
  colours: string[];
  details: string[];
  fabricLook: string | null;
  description: string | null;
}

export interface ImageMatch {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  productTypeName: string | null;
  isActive: boolean;
  source: ImageSource;
  colorwayCode: string | null;
  imageUrl: string;
  garment: string | null;
  colours: string | null;
  details: string | null;
  description: string | null;
  /** 1 = same direction, 0 = unrelated. */
  similarity: number;
}

export interface ImageSearchResult {
  mode: 'photo' | 'words' | 'similar';
  /** lookAlike: by the picture itself (built-in model); description: by the vision model's text. */
  method: 'lookAlike' | 'description';
  reading: ImageReading | null;
  matches: ImageMatch[];
  /** Words searched by look whose best match is far: say no close match was found. */
  weak: boolean;
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
  /** Typical seconds of recent renders with this image model (null: none since the API started). */
  estimate: { withSketchSeconds: number | null; withoutSketchSeconds: number | null; sketchRenders: number } | null;
  /** The picture a render starts from: the colorway's own picture when it has one (its print), else the sketch. */
  guideUrl: string | null;
  guideIsColorway: boolean;
}

/**
 * How far a running AI text call has got (GET progress/{id}): waiting = the model is still reading the request,
 * writing = the answer is coming in (words so far, field names included). Not a percentage.
 */
export interface AiProgress {
  /** queued: waiting for the AI server (AI call limits), ahead = calls before it. */
  phase: 'queued' | 'waiting' | 'writing';
  words: number;
  seconds: number;
  ahead?: number | null;
}

@Injectable({ providedIn: 'root' })
export class AiStudioService {
  private readonly http = inject(HttpClient);
  private readonly jobs = inject(AiJobsService);
  private readonly api = `${environment.apiBaseUrl}/api/v1/ai`;

  private status$?: Observable<AiStatus>;

  /** Provider status, cached for the session (it only changes with the server's settings). */
  status(): Observable<AiStatus> {
    return (this.status$ ??= this.http.get<AiStatus>(`${this.api}/status`).pipe(shareReplay({ bufferSize: 1, refCount: false })));
  }

  /**
   * Phrase list, then the built-in image model (pictures that clearly stand out), then AI for requests it does not fully
   * understand (AI rate limit applies). skipLook: straight from the phrase list to the AI ("Search with AI instead").
   */
  search(query: string, skipLook = false): Observable<StyleSearchResult> {
    return this.http.post<StyleSearchResult>(`${this.api}/style-search`, { query, skipLook });
  }

  /** Phrase list, then the built-in image model: never calls the AI server. */
  searchByRules(query: string): Observable<StyleSearchResult> {
    return this.http.post<StyleSearchResult>(`${this.api}/style-search/rules`, { query });
  }

  phrases(): Observable<StyleSearchPhrases> {
    return this.http.get<StyleSearchPhrases>(`${this.api}/style-search/phrases`);
  }

  compare(styleId: number, fromStyleId: number | null): Observable<StyleCompare> {
    let params = new HttpParams().set('styleId', styleId);
    if (fromStyleId) params = params.set('fromStyleId', fromStyleId);
    return this.http.get<StyleCompare>(`${this.api}/style-compare`, { params });
  }

  /** An AI job: the answer once the job has finished (taskId: the tray's id for the run, see AiJobsService.describe). */
  summarize(styleId: number, fromStyleId: number | null, taskId?: string): Observable<CompareSummary> {
    return this.jobs.run(this.http.post<{ jobId: number }>(`${this.api}/style-compare/summary`, { styleId, fromStyleId }, this.jobs.options(taskId)), taskId);
  }

  bomCheck(query: BomCheckQuery): Observable<BomCheck> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) if (value) params = params.set(key, String(value));
    return this.http.get<BomCheck>(`${this.api}/bom-check`, { params });
  }

  /** An AI job (see summarize). */
  explain(query: BomCheckQuery, taskId?: string): Observable<BomCheckExplanation> {
    return this.jobs.run(this.http.post<{ jobId: number }>(`${this.api}/bom-check/explain`, query, this.jobs.options(taskId)), taskId);
  }

  renderBrief(styleId: number, colorwayId: number | null): Observable<RenderBrief> {
    const params = colorwayId ? new HttpParams().set('colorwayId', colorwayId) : undefined;
    return this.http.get<RenderBrief>(`${this.api}/style-render/${styleId}`, { params });
  }

  /** An AI job (see summarize). */
  writePrompt(styleId: number, colorwayId: number | null, taskId?: string): Observable<{ prompt: string }> {
    return this.jobs.run(this.http.post<{ jobId: number }>(`${this.api}/style-render/${styleId}/prompt`, { colorwayId }, this.jobs.options(taskId)), taskId);
  }

  /** An AI job on the image lane (see summarize). */
  render(styleId: number, colorwayId: number | null, prompt: string, useSketch: boolean, taskId?: string): Observable<StyleRender> {
    return this.jobs.run(this.http.post<{ jobId: number }>(`${this.api}/style-render/${styleId}`, { colorwayId, prompt, useSketch }, this.jobs.options(taskId)), taskId);
  }

  deleteRender(styleId: number, renderId: number): Observable<unknown> {
    return this.http.delete(`${this.api}/style-render/${styleId}/renders/${renderId}`);
  }

  // ----- Image search (in-house Vision + Embedding jobs) -----

  imageSearchStatus(): Observable<ImageSearchStatus> {
    return this.http.get<ImageSearchStatus>(`${this.api}/image-search/status`);
  }

  /** Indexes the next pictures (about 45 s of work per call; Admin, Merchandiser). */
  indexImages(): Observable<IndexImagesResult> {
    return this.http.post<IndexImagesResult>(`${this.api}/image-search/index`, {});
  }

  /** useAiServer: search the descriptions (in-house AI server) instead of the built-in model. */
  searchImagesByWords(query: string, customer: string | null, useAiServer: boolean): Observable<ImageSearchResult> {
    return this.http.post<ImageSearchResult>(`${this.api}/image-search/words`, { query, customer, useAiServer });
  }

  /** The photo is read inside LT and not stored. useAiServer: by the vision model's description instead of the built-in model. */
  searchImagesByPhoto(file: Blob, customer: string | null, useAiServer: boolean): Observable<ImageSearchResult> {
    const form = new FormData();
    form.append('file', file, 'photo');
    if (customer) form.append('customer', customer);
    form.append('useAiServer', String(useAiServer));
    return this.http.post<ImageSearchResult>(`${this.api}/image-search/photo`, form);
  }

  /** Styles that look like this one, from stored vectors (no AI call). */
  similarImages(styleId: number, customer: string | null): Observable<ImageSearchResult> {
    const params = customer ? new HttpParams().set('customer', customer) : undefined;
    return this.http.get<ImageSearchResult>(`${this.api}/image-search/similar/${styleId}`, { params });
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

  /** off: turn the job off (no app default either). */
  savePurpose(purpose: AiPurpose, connectionId: number | null, model: string | null, off = false): Observable<unknown> {
    return this.http.put(`${this.admin}/purposes/${purpose}`, { connectionId, model, off });
  }

  /** The central switches. */
  savePolicy(aiEnabled: boolean, allowCloud: boolean): Observable<unknown> {
    return this.http.put(`${this.admin}/policy`, { aiEnabled, allowCloud });
  }

  /** AI call limits on or off, and how many AI tasks one user may have queued or running. */
  saveLimits(limitCalls: boolean, maxTasksPerUser: number): Observable<unknown> {
    return this.http.put(`${this.admin}/limits`, { limitCalls, maxTasksPerUser });
  }

  /** Tests a saved connection or the form's draft (a blank key uses the saved one). */
  testConnection(request: { connectionId: number | null; kind: AiConnectionKind; endpoint: string; apiKey: string | null; timeoutSeconds: number }): Observable<AiTestResult> {
    return this.http.post<AiTestResult>(`${this.admin}/test`, request);
  }
}
