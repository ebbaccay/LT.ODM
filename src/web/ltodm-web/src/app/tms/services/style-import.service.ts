import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';

// Settings > Import: Style Library workbook import (/api/v1/style-library/imports, Admin role only).

export type ImportStyleAction = 'New' | 'Changed' | 'Unchanged' | 'Blocked';
/** Mapping: the workbook is kept, its columns not mapped yet. */
export type ImportBatchStatus = 'Mapping' | 'Staged' | 'Committed' | 'Cancelled' | 'RolledBack';

export interface StyleImportSummary {
  styles: { total: number; new: number; changed: number; unchanged: number; blocked: number };
  rows: { styles: number; colorways: number; bom: number };
  bomLines: number;
  errors: number;
  warnings: number;
  committed: { styles: number; new: number; replaced: number; bomLines: number } | null;
  /** Answers remembered from earlier imports that were applied to this one. */
  remembered?: number;
  /** BOM rows without a material code: flagged and skipped on commit (their styles import without them). */
  skippedBom?: { rows: number; styles: number } | null;
  /** What a rollback did: styles removed, styles restored, reference codes removed. */
  rolledBack?: { removed: number; restored: number; codes: number } | null;
}

export interface StyleImportBatch {
  batchId: number;
  fileName: string;
  status: ImportBatchStatus;
  summary: StyleImportSummary | null;
  uploadedBy: string;
  uploadedUtc: string;
  finishedBy: string | null;
  finishedUtc: string | null;
  rolledBackBy: string | null;
  rolledBackUtc: string | null;
  /** Committed in the last 30 days, with what the commit did recorded. */
  canRollBack: boolean;
  /** The workbook is kept (not committed; up to 7 days after a cancel), so its columns can be mapped (again). */
  hasFile: boolean;
}

/** Why a rollback stopped (nothing was changed): a style, or (no styleNo) a material, changed after the import. */
export interface RollbackBlocker {
  styleNo: string | null;
  seasonCode: string | null;
  reason: string;
}

export interface StyleImportStyle {
  /** Cust|Season|Style */
  styleKey: string;
  customerCode: string;
  seasonCode: string;
  styleNo: string;
  modelName: string | null;
  action: ImportStyleAction;
  existingStyleId: number | null;
  /** For Changed: what differs, e.g. "header, BOM". */
  changes: string | null;
  colorwayCount: number;
  bomLineCount: number;
  errorCount: number;
  warningCount: number;
  committed: boolean;
}

export interface StyleImportIssue {
  sheet: 'Style' | 'Article' | 'BOM';
  /** Excel row number. */
  rowNo: number | null;
  severity: 'Error' | 'Warning';
  styleKey: string | null;
  message: string;
}

export interface Paged<T> {
  items: T[];
  total: number;
}

// ----- New codes (preview tab) -----

export type ImportCodeList =
  | 'season' | 'customer' | 'brand' | 'customerBrand' | 'productType' | 'weaveType' | 'businessUnit' | 'gender' | 'status' | 'contentClass' | 'materialType' | 'uom' | 'supplier';

/** A value the import would add as a new code (or that blocks rows / is blanked), with a same-spelling suggestion. */
export interface ImportNewCode {
  list: ImportCodeList;
  value: string;
  label: string | null;
  rows: number;
  styles: number;
  /** The rows are blocked until the value is mapped (content class, status). */
  blocking: boolean;
  suggestedCode: string | null;
  suggestedName: string | null;
  /** Customer accounts: the brand the file (Customer_Brand) or an answer gives the account. */
  brand: string | null;
}

export interface ImportNewCodes {
  codes: ImportNewCode[];
  /** Every list's current codes, for "use existing". */
  options: { list: ImportCodeList; code: string; name: string | null }[];
  /** New customer accounts that look like one customer (same brand word), with an existing customer they may be. */
  groups: ImportCustomerGroup[] | null;
  /** Remembered answers this import used. */
  applied: ImportAppliedAnswer[] | null;
}

export interface ImportCustomerGroup {
  /** The brand word the accounts share, e.g. POLO. */
  key: string;
  accounts: ImportNewCode[];
  suggestedCode: string | null;
  suggestedName: string | null;
}

export interface ImportAppliedAnswer {
  list: ImportCodeList;
  value: string;
  code: string;
  rows: number;
}

/** AI suggestion; code null = keep as new. */
export interface ImportCodeSuggestion {
  list: ImportCodeList;
  value: string;
  code: string | null;
  reason: string;
}

// ----- Column mapping (upload step) -----

export type ImportSheetKey = 'Style' | 'Article' | 'Bom';

/** Which workbook sheet feeds an import sheet, and which heading feeds each column (null = not imported). */
export interface ImportSheetMapping {
  source: string | null;
  /** Staging column name (e.g. AdMatId) -> workbook heading. */
  columns: Record<string, string | null>;
}

export interface ImportColumnMapping {
  sheets: Record<ImportSheetKey, ImportSheetMapping>;
}

export interface WorkbookSheet {
  name: string;
  columns: { heading: string; samples: string[] }[];
}

/** A column the import can fill: Heading is the template's, Destination where it ends up in the library. */
export interface ImportTargetColumn {
  name: string;
  heading: string;
  destination: string;
  required: boolean;
}

export interface ImportTargetSheet {
  key: ImportSheetKey;
  label: string;
  columns: ImportTargetColumn[];
}

export interface SavedImportMapping {
  mappingId: number;
  name: string;
  mapping: ImportColumnMapping;
  updatedBy: string;
  updatedUtc: string;
}

/** The workbook's sheets and headings, what the import needs, and the suggested mapping (saved one when mappingId is set). */
export interface WorkbookInspection {
  sheets: WorkbookSheet[];
  targets: ImportTargetSheet[];
  mapping: ImportColumnMapping;
  mappingId: number | null;
  saved: SavedImportMapping[];
  /** The import the workbook is kept with (status Mapping until it is staged). */
  batchId: number;
  fileName: string;
}

@Injectable({ providedIn: 'root' })
export class StyleImportService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/style-library/imports`;

  /** The workbook layout to fill in (headings in the customer's terms). */
  readonly templateUrl = 'assets/templates/style-library-import-template.xlsx';

  list(): Observable<StyleImportBatch[]> {
    return this.http.get<StyleImportBatch[]>(this.api);
  }

  get(batchId: number): Observable<StyleImportBatch> {
    return this.http.get<StyleImportBatch>(`${this.api}/${batchId}`);
  }

  /** Reads the workbook's sheets and headings and suggests a column mapping. The file is kept with a new import (status Mapping); nothing is staged. */
  inspect(file: File): Observable<WorkbookInspection> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<WorkbookInspection>(`${this.api}/inspect`, form);
  }

  /** The kept workbook of an import that was not committed, with its mapping: to finish or change the mapping. */
  reinspect(batchId: number): Observable<WorkbookInspection> {
    return this.http.get<WorkbookInspection>(`${this.api}/${batchId}/inspect`);
  }

  /** Saves the mapping being edited, so it can be resumed later. */
  saveBatchMapping(batchId: number, mapping: ImportColumnMapping): Observable<void> {
    return this.http.put<void>(`${this.api}/${batchId}/mapping`, mapping);
  }

  /** Stages the kept workbook with this mapping and checks it; returns the batch. */
  stage(batchId: number, mapping: ImportColumnMapping): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/stage`, mapping);
  }

  /** Deletes an import that was not committed, with its kept workbook. */
  deleteImport(batchId: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/${batchId}`);
  }

  mappings(): Observable<SavedImportMapping[]> {
    return this.http.get<SavedImportMapping[]>(`${this.api}/mappings`);
  }

  /** Saves the mapping under its name (one with the same name is replaced). */
  saveMapping(name: string, mapping: ImportColumnMapping): Observable<SavedImportMapping> {
    return this.http.put<SavedImportMapping>(`${this.api}/mappings`, { name, mapping });
  }

  deleteMapping(mappingId: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/mappings/${mappingId}`);
  }

  styles(batchId: number, options: { action?: ImportStyleAction | null; search?: string; skip?: number; take?: number } = {}): Observable<Paged<StyleImportStyle>> {
    return this.http.get<Paged<StyleImportStyle>>(`${this.api}/${batchId}/styles`, { params: this.params(options) });
  }

  issues(batchId: number, options: { severity?: 'Error' | 'Warning' | null; styleKey?: string; skip?: number; take?: number } = {}): Observable<Paged<StyleImportIssue>> {
    return this.http.get<Paged<StyleImportIssue>>(`${this.api}/${batchId}/issues`, { params: this.params(options) });
  }

  /** Writes every New style and the Changed styles listed (by styleKey). */
  commit(batchId: number, overwriteStyleKeys: string[]): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/commit`, { overwriteStyleKeys });
  }

  newCodes(batchId: number): Observable<ImportNewCodes> {
    return this.http.get<ImportNewCodes>(`${this.api}/${batchId}/codes`);
  }

  /**
   * Uses an existing code for a value (the staged rows are rewritten and checked again); returns the batch.
   * remember: later imports use the same code for this value without asking.
   */
  mapCode(batchId: number, list: ImportCodeList, value: string, code: string, remember = true): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/codes/map`, { list, value, code, remember });
  }

  /** Several values at once ("Use all"); checked once. */
  mapCodes(batchId: number, items: { list: ImportCodeList; value: string; code: string }[], remember = true): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/codes/map-many`, { items, remember });
  }

  /** Customer accounts that are one customer: adds customer `code` (named `name`) if new and uses it for them. */
  groupCustomers(batchId: number, values: string[], code: string, name: string, remember = true): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/codes/group-customers`, { values, code, name, remember });
  }

  /**
   * Customer accounts that stay separate customers under one brand: adds the brand if new, gives it to them (and to
   * `alsoCustomer`, an existing customer they look like, when it has none); returns the batch.
   */
  brandCustomers(batchId: number, values: string[], brand: string, name: string, alsoCustomer: string | null, remember = true): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/codes/brand-customers`, { values, brand, name, alsoCustomer, remember });
  }

  /** Forgets a remembered answer: later imports ask about the value again. */
  forgetCode(list: ImportCodeList, value: string): Observable<void> {
    return this.http.post<void>(`${this.api}/codes/forget`, { list, value });
  }

  /** AI suggestions (Text job) for the values the spelling rules could not match. */
  suggestCodes(batchId: number): Observable<ImportCodeSuggestion[]> {
    return this.http.post<ImportCodeSuggestion[]>(`${this.api}/${batchId}/codes/suggest`, {});
  }

  /** Undoes a commit. 409 with `blockers` (RollbackBlocker[]) when styles changed after it; nothing is changed then. */
  rollback(batchId: number): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/rollback`, {});
  }

  cancel(batchId: number): Observable<void> {
    return this.http.post<void>(`${this.api}/${batchId}/cancel`, {});
  }

  private params(options: Record<string, string | number | null | undefined>): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(options)) {
      if (value !== null && value !== undefined && value !== '') params = params.set(key, String(value));
    }
    return params;
  }
}

/** Readable message from an import API error (the API answers with ProblemDetails; template problems in `problems`). */
/** The blockers of a refused rollback (409), or null for any other error. */
export function rollbackBlockers(err: unknown): RollbackBlocker[] | null {
  return err instanceof HttpErrorResponse && err.status === 409 && Array.isArray(err.error?.blockers) ? (err.error.blockers as RollbackBlocker[]) : null;
}

export function importErrorMessage(err: unknown): { title: string; problems: string[] } {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { title?: string; problems?: string[] } | null;
    if (err.status === 0) return { title: 'The server could not be reached. Check your connection and try again.', problems: [] };
    if (err.status === 403) return { title: 'You need the Admin role to import styles.', problems: [] };
    if (err.status === 413) return { title: 'The workbook is too large. Files must be 100 MB or smaller.', problems: [] };
    if (body?.title && err.status < 500) return { title: body.title, problems: body.problems ?? [] };
  }
  return { title: 'The import could not be completed. Please try again.', problems: [] };
}
