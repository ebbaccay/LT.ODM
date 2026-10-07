import { DOCUMENT, inject, Injectable, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { UserInfoService } from './user-info.service';

export const AVAILABLE_LANGS = [
  { code: 'en', label: 'English' },
  { code: 'zh-Hans', label: '中文 (简体)' },
] as const;

export type LangCode = (typeof AVAILABLE_LANGS)[number]['code'];

const isLang = (code: string | null | undefined): code is LangCode => AVAILABLE_LANGS.some((l) => l.code === code);

/**
 * Active UI language. The choice is saved per browser (UserInfoService), so it also applies on the sign-in pages;
 * the first visit follows the browser language (zh-* -> zh-Hans).
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private transloco = inject(TranslocoService);
  private userInfo = inject(UserInfoService);
  private document = inject(DOCUMENT);

  readonly currentLang = signal<LangCode>('en');

  /** Called once by App at start-up. */
  init(): void {
    const saved = this.userInfo.savedLanguage();
    this.apply(isLang(saved) ? saved : this.browserLang());
  }

  setLanguage(lang: LangCode): void {
    this.apply(lang);
    this.userInfo.setLanguage(lang);
  }

  private apply(lang: LangCode): void {
    this.transloco.setActiveLang(lang);
    this.currentLang.set(lang);
    this.document.documentElement.lang = lang;
  }

  private browserLang(): LangCode {
    const preferred = this.document.defaultView?.navigator.language ?? '';
    return preferred.toLowerCase().startsWith('zh') ? 'zh-Hans' : 'en';
  }
}
