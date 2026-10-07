import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { environment } from '@env/environment';
import { translate } from '@jsverse/transloco';
import { Observable, map, shareReplay } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

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
}

export interface StyleListFilter {
  search?: string;
  customer?: string;
  season?: string;
  businessUnit?: string;
  productType?: string;
  weaveType?: string;
  gender?: string;
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
  brandConsumption: number | null;
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

/** Plain number for inputs; null when blank or not a number. */
export function toNumber(value: string): number | null {
  const text = value.trim();
  if (!text) return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : null;
}
