import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';
import { Paged } from '../style-library/style-library.service';

// Materials (/api/v1/materials): the BOM seen by material. Read only; same readers as Styles.

export interface MaterialListItem {
  materialId: number;
  materialCode: string;
  description: string | null;
  materialTypeCode: string | null;
  materialTypeName: string | null;
  contentClassCode: string | null;
  contentClassName: string | null;
  /** Counts cover the styles matching the customer / season filters. */
  styles: number;
  lines: number;
  seasons: number;
  customers: number;
  suppliers: number;
  topSupplierName: string | null;
  latestSeason: string | null;
}

export interface MaterialListFilter {
  search?: string;
  contentClass?: string;
  materialType?: string;
  supplier?: string;
  customer?: string;
  season?: string;
  sort?: 'used' | 'code';
  skip?: number;
  take?: number;
}

export interface MaterialUse {
  bomLineId: number;
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  productTypeCode: string | null;
  productTypeName: string | null;
  gender: string | null;
  lineSeq: number;
  partNo: number | null;
  lcoConsumption: number | null;
  brandConsumption: number | null;
  uomCode: string | null;
  supplierName: string | null;
  nominatedSupplierName: string | null;
  colorways: number;
  colours: string | null;
}

/** Per-style consumption for one product type and unit. */
export interface MaterialBenchmark {
  productTypeCode: string | null;
  productTypeName: string | null;
  uomCode: string;
  styles: number;
  minValue: number;
  p25: number;
  median: number;
  p75: number;
  maxValue: number;
}

export interface MaterialDetail {
  header: {
    materialId: number;
    materialCode: string;
    description: string | null;
    materialTypeCode: string | null;
    materialTypeName: string | null;
    contentClassCode: string | null;
    contentClassName: string | null;
    styles: number;
    lines: number;
    customers: number;
    seasons: number;
    createdBy: string;
    createdUtc: string;
    updatedBy: string | null;
    updatedUtc: string | null;
  };
  /** At most 1000, newest season first; header.lines counts them all. */
  uses: MaterialUse[];
  benchmarks: MaterialBenchmark[];
  /** supplierCode '' = no SAP supplier on those lines. */
  suppliers: { supplierCode: string; supplierName: string | null; styles: number; lines: number }[];
  colours: { materialColorCode: string | null; materialColorDescription: string | null; colorways: number; styles: number }[];
  similar: { materialId: number; materialCode: string; description: string | null; reason: 'SameDescription' | 'SameBaseCode'; styles: number }[];
}

// ----- Insights (views on the Materials page) -----

export type MaterialView = 'all' | 'trims' | 'suppliers' | 'duplicates' | 'recycled' | 'colours' | 'reader';

export interface InsightFilter {
  customer?: string | null;
  season?: string | null;
  contentClass?: string | null;
  productType?: string | null;
}

export interface StandardTrims {
  stylesWithBom: number;
  /** How many different materials do one job, and how many are used on one style only. */
  types: { materialTypeCode: string | null; materialTypeName: string | null; contentClassCode: string | null; materials: number; oneOffs: number; styles: number }[];
  materials: {
    materialId: number;
    materialCode: string;
    description: string | null;
    materialTypeCode: string | null;
    materialTypeName: string | null;
    contentClassCode: string | null;
    styles: number;
    /** 0..1 of the product type's styles with a BOM. */
    share: number;
    uomCode: string | null;
    median: number | null;
  }[];
}

export interface SupplierConcentration {
  totals: { styles: number; materials: number; suppliers: number; singleSource: number; multiSource: number; linesWithoutSupplier: number; lines: number };
  suppliers: { supplierCode: string; supplierName: string | null; styles: number; share: number; lines: number; materials: number; onlySource: number }[];
}

export interface Duplicates {
  totalGroups: number;
  totalMaterials: number;
  groups: { groupNo: number; styles: number; members: { materialId: number; materialCode: string; description: string | null; contentClassCode: string | null; styles: number }[] }[];
}

export interface RecycledRow {
  key: string | null;
  name: string | null;
  styles: number;
  allRecycled: number;
  someRecycled: number;
  fabricLines: number;
  recycledLines: number;
}

export interface RecycledShare {
  seasons: RecycledRow[];
  productTypes: RecycledRow[];
}

export interface ColourUsage {
  colour: string;
  codes: number;
  colorways: number;
  styles: number;
  materials: number;
  seasons: number;
}

// ----- Description reader (/api/v1/ai/material-specs) -----

export type SpecState = 'Unread' | 'Pending' | 'Accepted' | 'Rejected' | 'Outdated';

export interface MaterialSpec {
  materialId: number;
  materialCode: string;
  description: string | null;
  contentClassCode: string | null;
  materialTypeCode: string | null;
  styles: number;
  /** Outdated: the description changed since it was read. */
  state: SpecState;
  composition: string | null;
  fibres: { fibre: string; percent: number; recycled: boolean }[];
  recycledPct: number | null;
  construction: string | null;
  weightGsm: number | null;
  widthCm: number | null;
  suggestedContentClass: string | null;
  confidence: number | null;
  notes: string | null;
  provider: string | null;
  model: string | null;
  readBy: string | null;
  readUtc: string | null;
  reviewedBy: string | null;
  reviewedUtc: string | null;
}

export interface MaterialSpecPage {
  counts: { total: number; unread: number; pending: number; accepted: number; rejected: number; outdated: number };
  items: MaterialSpec[];
  total: number;
}

@Injectable({ providedIn: 'root' })
export class MaterialsService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/materials`;

  list(filter: MaterialListFilter): Observable<Paged<MaterialListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter))
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    return this.http.get<Paged<MaterialListItem>>(this.api, { params });
  }

  get(materialId: number): Observable<MaterialDetail> {
    return this.http.get<MaterialDetail>(`${this.api}/${materialId}`);
  }

  standardTrims(filter: InsightFilter): Observable<StandardTrims> {
    return this.http.get<StandardTrims>(`${this.api}/insights/standard-trims`, { params: this.params(filter) });
  }

  suppliers(filter: InsightFilter): Observable<SupplierConcentration> {
    return this.http.get<SupplierConcentration>(`${this.api}/insights/suppliers`, { params: this.params(filter) });
  }

  duplicates(filter: InsightFilter): Observable<Duplicates> {
    return this.http.get<Duplicates>(`${this.api}/insights/duplicates`, { params: this.params(filter) });
  }

  recycled(filter: InsightFilter): Observable<RecycledShare> {
    return this.http.get<RecycledShare>(`${this.api}/insights/recycled`, { params: this.params(filter) });
  }

  colours(filter: InsightFilter): Observable<ColourUsage[]> {
    return this.http.get<ColourUsage[]>(`${this.api}/insights/colours`, { params: this.params(filter) });
  }

  // ----- Description reader -----

  private readonly specsApi = `${environment.apiBaseUrl}/api/v1/ai/material-specs`;

  specs(filter: { contentClass?: string | null; materialType?: string | null; status?: SpecState | null; search?: string; skip?: number; take?: number }): Observable<MaterialSpecPage> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) if (value !== null && value !== undefined && value !== '') params = params.set(key, String(value));
    return this.http.get<MaterialSpecPage>(this.specsApi, { params });
  }

  spec(materialId: number): Observable<MaterialSpec> {
    return this.http.get<MaterialSpec>(`${this.specsApi}/${materialId}`);
  }

  /** Reads the next 25 unread or changed descriptions in the scope (one AI call). */
  readSpecs(contentClass: string | null, materialType: string | null): Observable<{ read: number; remaining: number }> {
    return this.http.post<{ read: number; remaining: number }>(`${this.specsApi}/read`, { contentClass, materialType });
  }

  reviewSpecs(materialIds: number[], status: 'Accepted' | 'Rejected' | 'Pending'): Observable<{ updated: number }> {
    return this.http.post<{ updated: number }>(`${this.specsApi}/review`, { materialIds, status });
  }

  private params(filter: InsightFilter): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) if (value) params = params.set(key, value);
    return params;
  }
}
