import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';

// Settings > Import: Style Library workbook import (/api/v1/style-library/imports, Admin role only).

export type ImportStyleAction = 'New' | 'Changed' | 'Unchanged' | 'Blocked';
export type ImportBatchStatus = 'Staged' | 'Committed' | 'Cancelled';

export interface StyleImportSummary {
  styles: { total: number; new: number; changed: number; unchanged: number; blocked: number };
  rows: { styles: number; colorways: number; bom: number };
  bomLines: number;
  errors: number;
  warnings: number;
  committed: { styles: number; new: number; replaced: number; bomLines: number } | null;
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
  | 'customer' | 'productType' | 'weaveType' | 'businessUnit' | 'gender' | 'status' | 'contentClass' | 'materialType' | 'uom' | 'supplier';

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
}

export interface ImportNewCodes {
  codes: ImportNewCode[];
  /** Every list's current codes, for "use existing". */
  options: { list: ImportCodeList; code: string; name: string | null }[];
}

/** AI suggestion; code null = keep as new. */
export interface ImportCodeSuggestion {
  list: ImportCodeList;
  value: string;
  code: string | null;
  reason: string;
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

  /** Stages and checks the workbook; nothing reaches the library until commit. */
  upload(file: File): Observable<StyleImportBatch> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<StyleImportBatch>(this.api, form);
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

  /** Uses an existing code for a value (the staged rows are rewritten and checked again); returns the batch. */
  mapCode(batchId: number, list: ImportCodeList, value: string, code: string): Observable<StyleImportBatch> {
    return this.http.post<StyleImportBatch>(`${this.api}/${batchId}/codes/map`, { list, value, code });
  }

  /** AI suggestions (Text job) for the values the spelling rules could not match. */
  suggestCodes(batchId: number): Observable<ImportCodeSuggestion[]> {
    return this.http.post<ImportCodeSuggestion[]>(`${this.api}/${batchId}/codes/suggest`, {});
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
export function importErrorMessage(err: unknown): { title: string; problems: string[] } {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { title?: string; problems?: string[] } | null;
    if (err.status === 0) return { title: 'The server could not be reached. Check your connection and try again.', problems: [] };
    if (err.status === 403) return { title: 'You need the Admin role to import styles.', problems: [] };
    if (err.status === 413) return { title: 'The workbook is too large. Files must be 50 MB or smaller.', problems: [] };
    if (body?.title && err.status < 500) return { title: body.title, problems: body.problems ?? [] };
  }
  return { title: 'The import could not be completed. Please try again.', problems: [] };
}
