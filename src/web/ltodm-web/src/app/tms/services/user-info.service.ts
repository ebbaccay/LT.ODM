import { Injectable, computed, inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { AuthService } from '../../core/auth/auth.service';

export interface TmsUser {
  username: string;
  firstName: string;
  lastName?: string;
  email?: string;
  /** Current LT ODM access token (memory only). */
  token: string;
  language?: string;
  /** 'FTY' = factory user (TMS). */
  userGroup?: string;
  /** Factory / office code (TMS). */
  location?: string;
}

/**
 * Same API as the TMS UserInfoService, backed by the LT ODM sign-in instead of localStorage.
 * Only the language preference is stored locally.
 */
@Injectable({ providedIn: 'root' })
export class UserInfoService {
  private readonly auth = inject(AuthService);
  private readonly langKey = `${environment.localeKeyString}.language`;

  private readonly current = computed<TmsUser | undefined>(() => {
    const u = this.auth.user();
    if (!u) return undefined;
    const [firstName, ...rest] = u.displayName.split(' ');
    return {
      username: u.userName,
      firstName: firstName ?? u.userName,
      lastName: rest.join(' ') || undefined,
      email: u.email,
      token: this.auth.getAccessToken() ?? '',
      language: this.savedLanguage(),
      userGroup: u.userGroup ?? undefined,
      location: u.location ?? undefined,
    };
  });

  /** Kept for compatibility; the user now comes from the sign-in session. */
  initUserInfo(): void {}

  get user(): TmsUser | undefined {
    const u = this.current();
    return u ? { ...u, token: this.auth.getAccessToken() ?? '' } : undefined;
  }

  get ObserveUser(): Observable<TmsUser | undefined> {
    return this.observeUser;
  }

  private readonly observeUser = toObservable(this.current);

  setLanguage(lang: string): void {
    try {
      localStorage.setItem(this.langKey, lang);
    } catch {
      // Storage blocked: the choice lasts for this session only.
    }
  }

  /** Saved UI language for this browser (also read before sign-in). */
  savedLanguage(): string | undefined {
    try {
      return localStorage.getItem(this.langKey) ?? undefined;
    } catch {
      return undefined;
    }
  }
}
