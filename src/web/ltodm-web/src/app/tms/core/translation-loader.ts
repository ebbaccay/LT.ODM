import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Translation, TranslocoLoader } from '@jsverse/transloco';
import { catchError, forkJoin, map, of } from 'rxjs';
import { BUILD_VERSION } from '@env/build-version';
import { environment } from '@env/environment';

// Ported from TMS: loads public/assets/i18n/<lang>.json for the ported modules.
// Corrections made in Settings > Translations (GET /api/v1/translations/<lang>/overrides) are layered on top;
// if they cannot be loaded the deployed texts are used as they are.

@Injectable({ providedIn: 'root' })
export class TranslationLoader implements TranslocoLoader {
  private http = inject(HttpClient);

  getTranslation(lang: string) {
    // Reads <base href> so the path works in both dev (/) and prod (/tmsarc/)
    const base = document.querySelector('base')?.getAttribute('href') ?? '/';
    const file = this.http.get<Translation>(`${base}assets/i18n/${lang}.json?v=${BUILD_VERSION}`);
    const overrides = this.http
      .get<Record<string, string>>(`${environment.apiBaseUrl}/api/v1/translations/${encodeURIComponent(lang)}/overrides`)
      .pipe(catchError(() => of({} as Record<string, string>)));
    return forkJoin([file, overrides]).pipe(map(([texts, corrections]) => applyOverrides(texts, corrections)));
  }
}

/** Copy of the nested translation with each dotted key (a.b.c) set to its correction. */
export function applyOverrides(texts: Translation, corrections: Record<string, string> | null): Translation {
  const entries = Object.entries(corrections ?? {});
  if (!entries.length) return texts;
  const result = structuredClone(texts);
  for (const [key, value] of entries) {
    const path = key.split('.');
    let node: Translation = result;
    for (const part of path.slice(0, -1)) {
      if (typeof node[part] !== 'object' || node[part] === null) node[part] = {};
      node = node[part] as Translation;
    }
    node[path[path.length - 1]] = value;
  }
  return result;
}
