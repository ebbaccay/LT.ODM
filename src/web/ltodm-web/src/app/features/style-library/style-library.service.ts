import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { environment } from '@env/environment';
import { translate } from '@jsverse/transloco';
import { Observable, firstValueFrom, map, shareReplay } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import type { GapSummary } from './style-gaps.service';

// Style Library: styles, colorways, BOM lines and style history (/api/v1/styles).
// Reading: Admin, Merchandiser, Costing, Viewer. Changing: Admin, Merchandiser (the API checks both).

export interface LookupItem {
  code: string;
  name: string;
}

export interface StyleLookups {
  customers: LookupItem[];
  seasons: LookupItem[];
  businessUnits: LookupItem[];
  productTypes: LookupItem[];
  weaveTypes: LookupItem[];
  materialTypes: LookupItem[];
  contentClasses: LookupItem[];
  uoms: LookupItem[];
  suppliers: LookupItem[];
  seasonTerms: LookupItem[];
}

export interface StyleListItem {
  styleId: number;
  styleNo: string;
  baseStyleNo: string;
  description: string | null;
  modelCode: string | null;
  modelName: string | null;
  customerCode: string;
  customerName: string;
  seasonCode: string;
  businessUnitCode: string | null;
  businessUnitName: string | null;
  productTypeCode: string | null;
  productTypeName: string | null;
  weaveTypeCode: string | null;
  gender: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  colorwayCount: number;
  bomLineCount: number;
  hasHistory: boolean;
  lastChangedUtc: string;
  isActive: boolean;
  /** Parents only: versions of this style folded under it. */
  versions: number;
  /** Where the list search was found (null without a search). */
  found: StyleSearchFound | null;
}

/** A list search found in the style's own number / model / description, and on how many colorways and BOM lines (up to 3 of each listed). */
export interface StyleSearchFound {
  inStyle: boolean;
  colorways: number;
  bomLines: number;
  matches: StyleSearchMatch[];
}

export interface StyleSearchMatch {
  kind: 'Colorway' | 'BomLine';
  /** Colorway code or material code. */
  code: string;
  /** Colorway name or the BOM line's material description. */
  text: string | null;
  /** BOM line section (content class). */
  section: string | null;
}

export interface StyleListFilter {
  search?: string;
  customer?: string;
  season?: string;
  businessUnit?: string;
  productType?: string;
  weaveType?: string;
  gender?: string;
  /** Picture tags 'attribute:value' separated by commas (hood:hooded,colour:navy); only styles with tagged pictures match. */
  looks?: string;
  /** 'active', 'inactive', or empty for both. */
  status?: string;
  /** 'with' (a picture or sketch), 'without', or empty for both. */
  pictures?: string;
  /** Fold a version under the style it came from when that style is in the results too (item.versions counts them). */
  parentsOnly?: boolean;
  /** style, customer, season, businessUnit, productType, colorways, bomLines or changed; empty = newest season first. */
  sort?: string;
  desc?: boolean;
  skip?: number;
  take?: number;
}

/** RowVer: base64 of the row version read with the row; send it back unchanged when saving or deleting. */
export interface StyleHeader {
  styleId: number;
  customerCode: string;
  customerName: string;
  seasonCode: string;
  styleNo: string;
  baseStyleNo: string;
  description: string | null;
  modelCode: string | null;
  modelName: string | null;
  weaveTypeCode: string | null;
  weaveTypeName: string | null;
  productTypeCode: string | null;
  productTypeName: string | null;
  gender: string | null;
  garmentLeadTimeDays: number | null;
  businessUnitCode: string | null;
  businessUnitName: string | null;
  sketchUrl: string | null;
  imageUrl: string | null;
  isActive: boolean;
  sourceCreatedUtc: string | null;
  importBatchId: number | null;
  createdBy: string;
  createdUtc: string;
  updatedBy: string | null;
  updatedUtc: string | null;
  rowVer: string;
}

export type ColorwayStatus = 'INRANGE' | 'DROPPED';

export interface Colorway {
  colorwayId: number;
  sortOrder: number;
  colorwayCode: string;
  colorwayName: string | null;
  status: ColorwayStatus;
  imageUrl: string | null;
  createdBy: string;
  createdUtc: string;
  updatedBy: string | null;
  updatedUtc: string | null;
  rowVer: string;
}

/** Box in pixels of the source picture. */
export interface PictureBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Garment views of one colour in the sketch (colour as #rrggbb). */
export interface ColourGroup {
  index: number;
  colour: string;
  box: PictureBox;
  views: PictureBox[];
}

export interface ColorwayPictureSuggestion {
  colorwayId: number;
  colorwayCode: string;
  colorwayName: string | null;
  imageUrl: string | null;
  /** Colour read from the name (null = no known colour, e.g. "multicolor"). */
  nameColour: string | null;
  nameWord: string | null;
  group: number | null;
  box: PictureBox | null;
  distance: number | null;
  sure: boolean;
}

export interface ColorwayPictures {
  source: 'Sketch' | 'Image';
  sourceUrl: string;
  width: number;
  height: number;
  groups: ColourGroup[];
  colorways: ColorwayPictureSuggestion[];
}

export interface SaveColorwayPictures {
  sourceUrl: string;
  enhance: boolean;
  items: { colorwayId: number; rowVer: string; x: number; y: number; width: number; height: number }[];
}

export interface ColorwayPictureSaved {
  colorwayId: number;
  imageUrl: string | null;
  problem: string | null;
}

export interface BomLineColorway {
  colorwayId: number;
  materialColorCode: string | null;
  materialColorDescription: string | null;
}

export interface BomLine {
  bomLineId: number;
  /** Opens the material's page (/materials/:id). */
  materialId: number;
  lineSeq: number;
  partNo: number | null;
  materialCode: string;
  materialDescription: string | null;
  materialTypeCode: string | null;
  materialTypeName: string | null;
  contentClassCode: string | null;
  contentClassName: string | null;
  nominatedSupplierCode: string | null;
  nominatedSupplierName: string | null;
  supplierCode: string | null;
  supplierName: string | null;
  lcoConsumption: number | null;
  /** The LCO was filled from a suggestion (Fill the gaps), not typed: shown as an estimate. */
  lcoIsEstimate: boolean;
  brandConsumption: number | null;
  /** Brand allowance from the import (0.03 = 3%). */
  consumptionAllowance: number | null;
  uomCode: string | null;
  imageUrl: string | null;
  colorways: BomLineColorway[];
  createdBy: string;
  createdUtc: string;
  updatedBy: string | null;
  updatedUtc: string | null;
  rowVer: string;
}

export type HistoryRelation = 'CarryOver' | 'Variant';

export interface FamilyMember {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  modelName: string | null;
  imageUrl: string | null;
  sourceStyleId: number | null;
  relation: HistoryRelation | null;
  suffix: string | null;
  /** Import: same base number + model; Auto: letters added to another style's number; Manual: set by hand. */
  linkSource: 'Import' | 'Auto' | 'Manual' | null;
  note: string | null;
  linkedBy: string | null;
  isCurrent: boolean;
}

export interface StyleDetail {
  style: StyleHeader;
  colorways: Colorway[];
  bomLines: BomLine[];
  family: FamilyMember[];
}

export interface SaveStyleRequest {
  rowVer: string | null;
  customerCode: string;
  seasonCode: string;
  styleNo: string;
  description: string | null;
  modelCode: string | null;
  modelName: string | null;
  weaveTypeCode: string | null;
  productTypeCode: string | null;
  gender: string | null;
  garmentLeadTimeDays: number | null;
  businessUnitCode: string | null;
  sketchUrl: string | null;
  imageUrl: string | null;
  isActive: boolean;
}

export interface SaveColorwayRequest {
  rowVer: string | null;
  colorwayCode: string;
  colorwayName: string | null;
  status: ColorwayStatus;
  sortOrder: number | null;
  imageUrl: string | null;
}

export interface SaveBomLineRequest {
  rowVer: string | null;
  partNo: number | null;
  materialCode: string;
  materialDescription: string | null;
  materialTypeCode: string | null;
  contentClassCode: string | null;
  nominatedSupplierCode: string | null;
  nominatedSupplierName: string | null;
  supplierCode: string | null;
  supplierName: string | null;
  lcoConsumption: number | null;
  brandConsumption: number | null;
  uomCode: string | null;
  imageUrl: string | null;
  colorways: BomLineColorway[];
}

/** A suggested BOM for a style (Suggest BOM): built from similar library styles and the fabric its description names. */
export interface BomSuggestion {
  styleId: number;
  fabric: { fibres: string | null; recycled: boolean | null; weightGsm: number | null; weave: string | null } | null;
  usedPicture: boolean;
  /** Why the picture was not used: noPicture, pictureModelOff, pictureMissing, pictureUnreadable. */
  pictureNote: string | null;
  references: BomReference[];
  lines: SuggestedBomLine[];
  /** noReferences, noSameBrandReferences, descriptionWithoutFabric. */
  notes: string[];
}

export interface BomReference {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  /** Know-how only: never its labels, packaging or artwork. */
  otherBrand: boolean;
  score: number;
  /** family, productType, garment, weave, gender, customer, brand, fabric, look. */
  reasons: string[];
  lookSimilarity: number | null;
}

export interface SuggestedBomLine {
  key: string;
  contentClassCode: string | null;
  contentClassName: string | null;
  materialTypeCode: string | null;
  materialTypeName: string | null;
  confidence: number;
  recommended: boolean;
  /** references: shared by the reference styles; description: a library fabric matching the description. */
  basis: 'references' | 'description';
  alreadyOnBom: boolean;
  /** The first is the suggestion, the rest alternatives. */
  options: BomOption[];
}

export interface BomOption {
  materialId: number;
  materialCode: string;
  description: string | null;
  partNo: number | null;
  nominatedSupplierCode: string | null;
  nominatedSupplierName: string | null;
  supplierCode: string | null;
  supplierName: string | null;
  lcoConsumption: number | null;
  brandConsumption: number | null;
  uomCode: string | null;
  usedBy: number;
  ofReferences: number;
  fabricMatch: number | null;
  otherBrand: boolean;
}

/** Suggest from picture: a draft of a new style's fields from its sketch or photo (built-in image model). */
export interface PictureDescription {
  /** Garment code (TEE, PANTS...); confident when it clearly leads the others. */
  garment: PictureGuess | null;
  /** Product types for the garment this customer uses most: to pick from (the picture cannot tell 1/2 from 1/4 shorts). */
  productTypes: { productTypeCode: string; name: string; customerStyles: number }[];
  sleeve: PictureGuess | null;
  tags: { attribute: string; value: string; score: number; margin: number }[];
  /** Draft in the library's form ("T-SHIRT; SHORT SLEEVE; SOLID; NAVY"). */
  description: string | null;
  /** From the closest-looking library styles, not the picture: weave KNT / WVN, main fibre text. */
  weave: PictureHint | null;
  fabric: PictureHint | null;
  similarStyles: { styleId: number; styleNo: string; customerCode: string; seasonCode: string; productTypeName: string | null; similarity: number }[];
}

export interface PictureGuess {
  value: string;
  margin: number;
  confident: boolean;
}

export interface PictureHint {
  value: string;
  votes: number;
  of: number;
}

/** Suggested material colours for empty BOM line x colorway cells (rules over the library). */
export interface ColourSuggestion {
  styleId: number;
  cells: number;
  empty: number;
  suggestions: SuggestedColour[];
}

export interface SuggestedColour {
  bomLineId: number;
  colorwayId: number;
  materialColorCode: string | null;
  materialColorDescription: string | null;
  /** colorway: same material in colorways of the same name; material: the one colour the material always comes in. */
  basis: 'colorway' | 'material';
  confidence: number;
  lines: number;
  of: number;
  sameCustomer: boolean;
  recommended: boolean;
}

export type DesignPromptMode = 'text' | 'image';

/** Prompt for outside design tools (GET /api/v1/styles/:id/design-prompt). Built from the style's data; no AI call. */
export interface DesignPrompt {
  /** The mode used: image falls back to text when the style has no sketch or photo. */
  mode: DesignPromptMode;
  colorwayId: number | null;
  /** The image to attach in image mode. */
  referenceKind: 'sketch' | 'photo' | null;
  referenceUrl: string | null;
  /** The style has a sketch or photo (image mode is possible). */
  hasReference: boolean;
  facts: {
    garment: string | null;
    gender: string | null;
    fabrics: string[];
    colours: string[];
    details: string[];
  };
  prompt: string;
}

/** Landing page summary (GET /api/v1/styles/dashboard). */
export interface StyleDashboard {
  totals: {
    styles: number;
    colorways: number;
    colorwaysInRange: number;
    colorwaysWithBom: number;
    bomLines: number;
    materials: number;
    suppliers: number;
    customers: number;
    seasons: number;
    families: number;
    reusedStyles: number;
    stylesWithImage: number;
    stylesWithBom: number;
    stylesWithColorways: number;
  };
  /** The latest seasons, oldest first. */
  seasons: { seasonCode: string; styles: number; colorways: number; bomLines: number }[];
  customers: DashboardCount[];
  materialTypes: DashboardCount[];
  materials: {
    materialCode: string;
    description: string | null;
    materialTypeName: string | null;
    styles: number;
    bomLines: number;
  }[];
  suppliers: DashboardCount[];
  productTypes: DashboardCount[];
  recentStyles: {
    styleId: number;
    styleNo: string;
    modelName: string | null;
    customerCode: string;
    seasonCode: string;
    imageUrl: string | null;
    sketchUrl: string | null;
    colorwayCount: number;
    bomLineCount: number;
    lastChangedBy: string;
    lastChangedUtc: string;
  }[];
  lastImport: {
    batchId: number;
    fileName: string;
    finishedBy: string | null;
    finishedUtc: string | null;
  } | null;
  /** What is missing (suggestion counts are null here; Fill the gaps shows them). */
  gaps: GapSummary | null;
}

/** Code is '' when the value is not set on the style or BOM line. */
export interface DashboardCount {
  code: string;
  name: string;
  styles: number;
  colorways: number | null;
  bomLines: number | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
}

export const GENDERS = [
  { code: 'MALE', name: 'Male' },
  { code: 'FEMALE', name: 'Female' },
  { code: 'UNISEX', name: 'Unisex' },
  { code: 'KIDS', name: 'Kids' },
] as const;

@Injectable({ providedIn: 'root' })
export class StyleLibraryService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly api = `${environment.apiBaseUrl}/api/v1/styles`;
  private static readonly ListKey = 'ltodm.styles.list';

  /**
   * The Styles list's last filters (its address), so "back to Styles" returns to the same list. Kept for the browser tab
   * (session storage, best effort: blocked storage just means the plain list).
   */
  readonly listParams = signal<Record<string, string>>(StyleLibraryService.readListParams());

  rememberListParams(params: Record<string, string>): void {
    this.listParams.set(params);
    try {
      sessionStorage.setItem(StyleLibraryService.ListKey, JSON.stringify(params));
    } catch {
      /* storage blocked: kept in memory only */
    }
  }

  private static readListParams(): Record<string, string> {
    try {
      const saved = JSON.parse(sessionStorage.getItem(StyleLibraryService.ListKey) ?? '{}');
      return saved && typeof saved === 'object' ? saved : {};
    } catch {
      return {};
    }
  }

  /** Admins, merchandisers, costing and viewers can see the library (the API enforces it). */
  readonly canRead = computed(() =>
    (this.auth.user()?.roles ?? []).some((r) =>
      ['admin', 'merchandiser', 'costing', 'viewer'].includes(r.toLowerCase()),
    ),
  );

  /** Admins and merchandisers can create, edit and delete (the API enforces it). */
  readonly canEdit = computed(() =>
    (this.auth.user()?.roles ?? []).some((r) =>
      ['admin', 'merchandiser'].includes(r.toLowerCase()),
    ),
  );

  private lookups$?: Observable<StyleLookups>;

  /** Pick lists, cached until refreshLookups() (new codes typed in forms are added by the API). */
  lookups(): Observable<StyleLookups> {
    return (this.lookups$ ??= this.http
      .get<StyleLookups>(`${this.api}/lookups`)
      .pipe(shareReplay({ bufferSize: 1, refCount: false })));
  }

  refreshLookups(): void {
    this.lookups$ = undefined;
  }

  list(filter: StyleListFilter): Observable<Paged<StyleListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '')
        params = params.set(key, String(value));
    }
    return this.http.get<Paged<StyleListItem>>(this.api, { params });
  }

  dashboard(): Observable<StyleDashboard> {
    return this.http.get<StyleDashboard>(`${this.api}/dashboard`);
  }

  get(styleId: number): Observable<StyleDetail> {
    return this.http.get<StyleDetail>(`${this.api}/${styleId}`);
  }

  createStyle(request: SaveStyleRequest): Observable<number> {
    return this.http.post<{ styleId: number }>(this.api, request).pipe(map((r) => r.styleId));
  }

  updateStyle(styleId: number, request: SaveStyleRequest): Observable<unknown> {
    return this.http.put(`${this.api}/${styleId}`, request);
  }

  /** Which part of the style's sketch (or picture) shows each colorway; nothing changes. */
  colorwayPictures(styleId: number, source?: 'Sketch' | 'Image'): Observable<ColorwayPictures> {
    const query = source ? `?source=${source}` : '';
    return this.http.get<ColorwayPictures>(`${this.api}/${styleId}/colorway-pictures${query}`);
  }

  /** Cuts the confirmed parts and sets them as the colorways' pictures. */
  saveColorwayPictures(styleId: number, request: SaveColorwayPictures): Observable<ColorwayPictureSaved[]> {
    return this.http.post<ColorwayPictureSaved[]>(`${this.api}/${styleId}/colorway-pictures`, request);
  }

  /** A stored picture as a blob (with the sign-in token), e.g. to draw boxes on it. */
  imageBlob(url: string): Observable<Blob> {
    return this.http.get(`${environment.apiBaseUrl}${url}`, { responseType: 'blob' });
  }

  /** Swaps the style's picture and sketch (or moves the only one to the other place). */
  swapPictures(styleId: number, rowVer: string): Observable<unknown> {
    return this.http.post(`${this.api}/${styleId}/swap-pictures?rowVer=${encodeURIComponent(rowVer)}`, null);
  }

  deleteStyle(styleId: number, rowVer: string): Observable<unknown> {
    return this.http.delete(`${this.api}/${styleId}?rowVer=${encodeURIComponent(rowVer)}`);
  }

  copyStyle(styleId: number, seasonCode: string, styleNo: string): Observable<number> {
    return this.http
      .post<{ styleId: number }>(`${this.api}/${styleId}/copy`, { seasonCode, styleNo })
      .pipe(map((r) => r.styleId));
  }

  saveColorway(
    styleId: number,
    colorwayId: number | null,
    request: SaveColorwayRequest,
  ): Observable<unknown> {
    return colorwayId === null
      ? this.http.post(`${this.api}/${styleId}/colorways`, request)
      : this.http.put(`${this.api}/${styleId}/colorways/${colorwayId}`, request);
  }

  deleteColorway(styleId: number, colorway: Colorway): Observable<unknown> {
    return this.http.delete(
      `${this.api}/${styleId}/colorways/${colorway.colorwayId}?rowVer=${encodeURIComponent(colorway.rowVer)}`,
    );
  }

  saveBomLine(
    styleId: number,
    bomLineId: number | null,
    request: SaveBomLineRequest,
  ): Observable<unknown> {
    return bomLineId === null
      ? this.http.post(`${this.api}/${styleId}/bom-lines`, request)
      : this.http.put(`${this.api}/${styleId}/bom-lines/${bomLineId}`, request);
  }

  /** Suggest from picture for an uploaded sketch or photo (409 with a message when the image model cannot run). */
  describePicture(imageUrl: string, isSketch: boolean, customerCode: string | null): Observable<PictureDescription> {
    return this.http.post<PictureDescription>(`${this.api}/describe-picture`, { imageUrl, isSketch, customerCode });
  }

  suggestColours(styleId: number): Observable<ColourSuggestion> {
    return this.http.get<ColourSuggestion>(`${this.api}/${styleId}/colour-suggestion`);
  }

  /** Fills the picked colours into cells that are still empty. */
  applyColours(styleId: number, cells: Pick<SuggestedColour, 'bomLineId' | 'colorwayId' | 'materialColorCode' | 'materialColorDescription'>[]): Observable<{ filled: number }> {
    return this.http.post<{ filled: number }>(`${this.api}/${styleId}/colour-suggestion/apply`, { cells });
  }

  /** A suggested BOM (rules over the library, about a second). Nothing is saved. */
  suggestBom(styleId: number): Observable<BomSuggestion> {
    return this.http.get<BomSuggestion>(`${this.api}/${styleId}/bom-suggestion`);
  }

  /** Adds the picked suggestion lines as ordinary BOM lines. */
  applyBomSuggestion(styleId: number, lines: SaveBomLineRequest[]): Observable<{ added: number }> {
    return this.http.post<{ added: number }>(`${this.api}/${styleId}/bom-suggestion/apply`, { lines });
  }

  deleteBomLine(styleId: number, line: BomLine): Observable<unknown> {
    return this.http.delete(
      `${this.api}/${styleId}/bom-lines/${line.bomLineId}?rowVer=${encodeURIComponent(line.rowVer)}`,
    );
  }

  setHistory(
    styleId: number,
    sourceStyleId: number,
    relation: HistoryRelation,
    note: string | null,
  ): Observable<unknown> {
    return this.http.put(`${this.api}/${styleId}/history`, { sourceStyleId, relation, note });
  }

  removeHistory(styleId: number): Observable<unknown> {
    return this.http.delete(`${this.api}/${styleId}/history`);
  }

  designPrompt(styleId: number, mode: DesignPromptMode, colorwayId: number | null): Observable<DesignPrompt> {
    let params = new HttpParams().set('mode', mode);
    if (colorwayId !== null) params = params.set('colorwayId', colorwayId);
    return this.http.get<DesignPrompt>(`${this.api}/${styleId}/design-prompt`, { params });
  }

  /**
   * Saves an image to the user's computer as `<baseName>.<ext>`. Images served by the API need the sign-in token, so
   * they are fetched first; any other address (e.g. https:// from an import) is opened in a new tab instead.
   */
  async downloadImage(url: string, baseName: string): Promise<void> {
    if (!url.startsWith('/api/')) {
      window.open(url, '_blank', 'noopener');
      return;
    }
    const blob = await firstValueFrom(this.http.get(`${environment.apiBaseUrl}${url}`, { responseType: 'blob' }));
    const ext = { 'image/png': 'png', 'image/gif': 'gif', 'image/webp': 'webp' }[blob.type] ?? 'jpg';
    saveBlob(blob, `${safeFileName(baseName)}.${ext}`);
  }

  /** Downloads the style's tech pack (PDF: header, sketch and photo, colorways, BOM, material colours by colorway). */
  async downloadTechPack(styleId: number, styleNo: string, seasonCode: string): Promise<void> {
    const blob = await firstValueFrom(this.http.get(`${this.api}/${styleId}/tech-pack`, { responseType: 'blob' }));
    saveBlob(blob, `${safeFileName(styleNo)}_${safeFileName(seasonCode)}_techpack.pdf`);
  }

  /** Uploads an image (already compressed) and returns its URL to save with the style, colorway or BOM line. */
  uploadImage(blob: Blob, fileName: string): Observable<string> {
    const form = new FormData();
    form.append('file', blob, fileName);
    return this.http
      .post<{ imageUrl: string }>(`${this.api}/images`, form)
      .pipe(map((r) => r.imageUrl));
  }
}

/** Field errors (400 ValidationProblem) and a message for the toast. */
export function styleError(err: unknown): { message: string; fields: Record<string, string> } {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { title?: string; errors?: Record<string, string[]> } | null;
    if (body?.errors) {
      const fields: Record<string, string> = {};
      for (const [key, messages] of Object.entries(body.errors))
        fields[key.charAt(0).toLowerCase() + key.slice(1)] = messages[0];
      return { message: Object.values(fields)[0] ?? translate('styles.errors.checkFields'), fields };
    }
    if (err.status === 0) return { message: translate('styles.errors.network'), fields: {} };
    if (err.status === 403) return { message: translate('styles.errors.forbidden'), fields: {} };
    if (body?.title && err.status < 500) return { message: body.title, fields: {} };
  }
  return { message: translate('styles.errors.saveFailed'), fields: {} };
}

/** Starts a browser download of the blob. */
export function saveBlob(blob: Blob, fileName: string): void {
  const href = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = href;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(href), 1000);
}

/** File-name-safe text: keeps letters, digits, '-', '_' and '.'. */
export function safeFileName(text: string): string {
  return text.replace(/[^\w.-]+/g, '_').replace(/^_+|_+$/g, '') || 'image';
}

/** Plain number for inputs; null when blank or not a number. */
export function toNumber(value: string): number | null {
  const text = value.trim();
  if (!text) return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : null;
}
