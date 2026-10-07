import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Translation, TranslocoLoader } from '@jsverse/transloco';
import { BUILD_VERSION } from '@env/build-version';

// Ported from TMS: loads public/assets/i18n/<lang>.json for the ported modules.

@Injectable({ providedIn: 'root' })
export class TranslationLoader implements TranslocoLoader {
  private http = inject(HttpClient);

  getTranslation(lang: string) {
    // Reads <base href> so the path works in both dev (/) and prod (/tmsarc/)
    const base = document.querySelector('base')?.getAttribute('href') ?? '/';
    return this.http.get<Translation>(`${base}assets/i18n/${lang}.json?v=${BUILD_VERSION}`);
  }
}
