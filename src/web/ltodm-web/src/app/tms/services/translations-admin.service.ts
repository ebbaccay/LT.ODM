import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';
import { TranslationLoader } from '@tms/core/translation-loader';

// Settings > Translations: corrections to the UI texts without a release (/api/v1/translations, Admin role only).
// The deployed files stay the base; corrections are layered on top by TranslationLoader.

export interface TranslationLanguage {
  code: string;
  label: string;
}

export interface TranslationRow {
  key: string;
  /** Deployed text per language (null = missing from that file). */
  base: Record<string, string | null>;
  /** Corrections per language. */
  overrides: Record<string, string>;
  /** Languages whose deployed text changed in a release after the correction was made. */
  baseChanged: string[];
}

export interface TranslationTable {
  languages: TranslationLanguage[];
  rows: TranslationRow[];
  updatedBy: string | null;
  updatedUtc: string | null;
}

export interface TranslationChange {
  rowNo: number;
  key: string;
  lang: string;
  from: string | null;
  to: string | null;
  /** Back to the deployed text. */
  revert: boolean;
}

export interface TranslationProblem {
  rowNo: number;
  key: string | null;
  lang: string | null;
  severity: 'Error' | 'Warning';
  message: string;
}

export interface TranslationImportResult {
  applied: boolean;
  rows: number;
  languages: string[];
  changes: TranslationChange[];
  problems: TranslationProblem[];
  problemCount: number;
}

export interface TranslationVersion {
  /** 'current' for the version in use. */
  id: string;
  savedUtc: string | null;
  savedBy: string | null;
  source: string | null;
  overrides: number;
  current: boolean;
}

@Injectable({ providedIn: 'root' })
export class TranslationsAdminService {
  private readonly http = inject(HttpClient);
  private readonly transloco = inject(TranslocoService);
  private readonly loader = inject(TranslationLoader);
  private readonly api = `${environment.apiBaseUrl}/api/v1/translations`;

  table(): Observable<TranslationTable> {
    return this.http.get<TranslationTable>(this.api);
  }

  /** Value null (or the deployed text) goes back to the deployed text. Returns the key's row. */
  set(lang: string, key: string, value: string | null): Observable<TranslationRow> {
    return this.http.put<TranslationRow>(`${this.api}/entries`, { lang, key, value });
  }

  export(): Observable<Blob> {
    return this.http.get(`${this.api}/export`, { responseType: 'blob' });
  }

  /** apply=false only checks the workbook and lists the changes; apply=true also saves them. */
  import(file: File, apply: boolean): Observable<TranslationImportResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<TranslationImportResult>(`${this.api}/import`, form, { params: new HttpParams().set('apply', apply) });
  }

  history(): Observable<TranslationVersion[]> {
    return this.http.get<TranslationVersion[]>(`${this.api}/history`);
  }

  restore(id: string): Observable<TranslationVersion> {
    return this.http.post<TranslationVersion>(`${this.api}/history/${encodeURIComponent(id)}/restore`, {});
  }

  /** Reloads the languages already in use so a saved correction shows straight away (other users see it on their next load). */
  refreshLive(): void {
    for (const lang of this.transloco.getAvailableLangs() as string[]) {
      if (!Object.keys(this.transloco.getTranslation(lang)).length) continue;
      this.loader.getTranslation(lang).subscribe((t) => this.transloco.setTranslation(t, lang, { merge: false }));
    }
  }
}

/** Readable message from a translations API error (ProblemDetails title). */
export function translationsErrorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { title?: string } | null;
    if (err.status === 0) return 'The server could not be reached. Check your connection and try again.';
    if (err.status === 403) return 'You need the Admin role to change translations.';
    if (err.status === 413) return 'The workbook is too large. Files must be 10 MB or smaller.';
    if (body?.title && err.status < 500) return body.title;
  }
  return 'The change could not be saved. Please try again.';
}
