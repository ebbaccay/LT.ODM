import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse, DEFAULT_PASSWORD_POLICY, PasswordPolicy, ProblemDetails, UserProfile } from './auth.models';

/**
 * Session handling:
 * - the access token (15 min JWT) lives only in memory, never in localStorage/sessionStorage;
 * - the refresh token is an HttpOnly cookie the browser sends to /api/v1/auth only;
 * - on page load the session is restored with POST /auth/refresh;
 * - only a 401 from that call ends the session: a server that cannot answer (restart, release being copied) does not.
 */
const REFRESH_RETRY_DELAYS_MS = [1_000, 2_000, 4_000, 8_000];
const REFRESH_AGAIN_WHEN_UNAVAILABLE_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly base = `${environment.apiBaseUrl}/api/v1/auth`;

  private accessToken: string | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;
  private refreshing: Promise<boolean> | null = null;

  readonly user = signal<UserProfile | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly passwordPolicy = signal<PasswordPolicy>(DEFAULT_PASSWORD_POLICY);
  /** The last refresh could not reach a working server, so whether the session is still valid is unknown. */
  readonly serverUnavailable = signal(false);

  getAccessToken(): string | null {
    return this.accessToken;
  }

  async login(login: string, password: string, rememberMe: boolean): Promise<void> {
    const response = await firstValueFrom(this.http.post<AuthResponse>(`${this.base}/login`, { login, password, rememberMe }));
    this.setSession(response);
  }

  /** Signed in, or able to restore the session from the refresh cookie. */
  async ensureSession(): Promise<boolean> {
    if (this.accessToken) return true;
    // The retries have just run out: going to the login page should not wait through them again.
    return this.serverUnavailable() ? false : this.refresh();
  }

  /** Single flight: concurrent callers share one refresh request. */
  refresh(): Promise<boolean> {
    this.refreshing ??= this.doRefresh().finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  private async doRefresh(retry = true, unavailableDelays = REFRESH_RETRY_DELAYS_MS): Promise<boolean> {
    try {
      this.setSession(await firstValueFrom(this.http.post<AuthResponse>(`${this.base}/refresh`, null)));
      return true;
    } catch (e) {
      // 409: another tab rotated the token a moment ago; the browser now has the new cookie.
      if (retry && e instanceof HttpErrorResponse && e.status === 409) {
        await new Promise((r) => setTimeout(r, 250));
        return this.doRefresh(false, unavailableDelays);
      }
      if (e instanceof HttpErrorResponse && e.status !== 401 && e.status !== 409) {
        if (unavailableDelays.length > 0) {
          await new Promise((r) => setTimeout(r, unavailableDelays[0]));
          return this.doRefresh(retry, unavailableDelays.slice(1));
        }
        // The cookie is untouched. With the app open, stay signed in and keep trying.
        this.serverUnavailable.set(true);
        if (this.accessToken) this.scheduleRefresh(REFRESH_AGAIN_WHEN_UNAVAILABLE_MS);
        return false;
      }
      this.clearSession();
      return false;
    }
  }

  /** Revokes the session on the server, clears tokens and the Cache Storage, and goes to the login page (message: translation key). */
  async logout(message?: string): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${this.base}/logout`, null));
    } catch {
      // Signed out locally even if the API is unreachable.
    }
    await this.clearLocalData();
    await this.router.navigate(['/login'], message ? { state: { message } } : undefined);
  }

  /** Called when the API rejects the session (e.g. expired, revoked elsewhere). */
  async sessionExpired(): Promise<void> {
    if (this.serverUnavailable()) return;
    await this.clearLocalData();
    await this.router.navigate(['/login'], {
      queryParams: { returnUrl: this.router.url },
      state: { message: 'auth.messages.sessionExpired' },
    });
  }

  forgotPassword(email: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/forgot-password`, { email }));
  }

  validateResetToken(token: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/reset-password/validate`, { token }));
  }

  resetPassword(token: string, newPassword: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/reset-password`, { token, newPassword }));
  }

  /** On success the API signs out every session, so the caller should send the user to the login page. */
  changePassword(currentPassword: string, newPassword: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/change-password`, { currentPassword, newPassword }));
  }

  async loadPasswordPolicy(): Promise<void> {
    try {
      this.passwordPolicy.set(await firstValueFrom(this.http.get<PasswordPolicy>(`${this.base}/password-policy`)));
    } catch {
      // Keep the defaults; the API validates on submit anyway.
    }
  }

  private setSession(response: AuthResponse): void {
    this.accessToken = response.accessToken;
    this.user.set(response.user);
    this.serverUnavailable.set(false);

    // Refresh one minute before the access token expires.
    const ms = new Date(response.accessTokenExpiresUtc).getTime() - Date.now() - 60_000;
    this.scheduleRefresh(Math.max(ms, 5_000));
  }

  private scheduleRefresh(ms: number): void {
    clearTimeout(this.refreshTimer);
    this.refreshTimer = setTimeout(() => void this.refresh(), ms);
  }

  private clearSession(): void {
    clearTimeout(this.refreshTimer);
    this.accessToken = null;
    this.user.set(null);
    this.serverUnavailable.set(false);
  }

  private async clearLocalData(): Promise<void> {
    this.clearSession();
    if ('caches' in globalThis) {
      const keys = await caches.keys();
      await Promise.all(keys.map((key) => caches.delete(key)));
    }
  }
}

/** First message from an API error, for display. */
/** Server messages are shown as sent; the client's own fallbacks follow the UI language. */
export function problemMessage(error: unknown, fallback?: string): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return translate('errors.network');
    if (error.status === 429) return translate('errors.tooManyAttempts');
    const problem = error.error as ProblemDetails | null;
    if (problem?.errors) {
      const first = Object.values(problem.errors).flat()[0];
      if (first) return first;
    }
    if (problem?.detail) return problem.detail;
    if (problem?.title) return problem.title;
  }
  return fallback ?? translate('errors.generic');
}

/** Field errors from a ValidationProblemDetails (e.g. { newPassword: [...] }). */
export function problemFieldErrors(error: unknown): Record<string, string[]> {
  if (error instanceof HttpErrorResponse && error.status === 400) {
    const errors = (error.error as ProblemDetails | null)?.errors ?? {};
    // ASP.NET may send keys in either case.
    return Object.fromEntries(Object.entries(errors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v]));
  }
  return {};
}
