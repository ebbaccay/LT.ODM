import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { environment } from '@env/environment';
import { Observable, firstValueFrom, tap } from 'rxjs';

// Look of the sign-in pages (Settings > Sign-in page, /api/v1/sign-in-page). Read without sign-in; the texts are
// translation keys (auth.shell.*, auth.login.*) changed in Settings > Translations.

export type SignInFocus = 'center' | 'top' | 'bottom' | 'left' | 'right';
export type SignInOverlay = 'none' | 'dark' | 'light' | 'brand';
export type SignInIntroText = 'auto' | 'light' | 'dark';

export interface SignInAppearance {
  /** Stored picture name, or null for the default colour glow. */
  backgroundImage: string | null;
  focus: SignInFocus;
  /** 0-24 px */
  backgroundBlur: number;
  overlay: SignInOverlay;
  /** 0-90 % */
  overlayStrength: number;
  showGlow: boolean;
  /** 10-95 %: how solid the card is. */
  glassOpacity: number;
  /** 0-40 px */
  glassBlur: number;
  showIntro: boolean;
  showFeatures: boolean;
  introText: SignInIntroText;
  /** A separate picture look for dark mode; null = dark mode uses the light-mode look. */
  dark: SignInModeLook | null;
}

/** The parts that depend on the picture, set separately for dark mode (backgroundImage null = no picture). */
export interface SignInModeLook {
  backgroundImage: string | null;
  focus: SignInFocus;
  backgroundBlur: number;
  overlay: SignInOverlay;
  overlayStrength: number;
  introText: SignInIntroText;
  glassOpacity: number;
}

export interface SignInModeLookDto extends SignInModeLook {
  backgroundUrl: string | null;
}

export interface SignInAppearanceDto extends Omit<SignInAppearance, 'dark'> {
  backgroundUrl: string | null;
  dark: SignInModeLookDto | null;
  updatedBy: string | null;
  updatedUtc: string | null;
}

/** The picture fields of one mode, as kept per mode. */
export function modeLookOf(a: SignInAppearanceDto): SignInModeLookDto {
  const { backgroundImage, backgroundUrl, focus, backgroundBlur, overlay, overlayStrength, introText, glassOpacity } = a;
  return { backgroundImage, backgroundUrl, focus, backgroundBlur, overlay, overlayStrength, introText, glassOpacity };
}

/** The look shown in a mode: the dark-mode picture look laid over the rest when dark and one is set. */
export function lookFor(a: SignInAppearanceDto, dark: boolean): SignInAppearanceDto {
  return dark && a.dark ? { ...a, ...a.dark } : a;
}

/** Same as SignInAppearance.Default in the API: the original look. */
export const DEFAULT_SIGN_IN_APPEARANCE: SignInAppearanceDto = {
  backgroundImage: null,
  backgroundUrl: null,
  focus: 'center',
  backgroundBlur: 0,
  overlay: 'none',
  overlayStrength: 30,
  showGlow: true,
  glassOpacity: 55,
  glassBlur: 40,
  showIntro: true,
  showFeatures: true,
  introText: 'auto',
  dark: null,
  updatedBy: null,
  updatedUtc: null,
};

@Injectable({ providedIn: 'root' })
export class SignInAppearanceService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/sign-in-page`;
  private loaded = false;

  /** What the sign-in pages show; the defaults until (or unless) the saved look arrives. */
  readonly appearance = signal<SignInAppearanceDto>(DEFAULT_SIGN_IN_APPEARANCE);

  /** Loads the saved look once per visit; on any failure the default look stays. */
  async load(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;
    try {
      this.appearance.set({ ...DEFAULT_SIGN_IN_APPEARANCE, ...(await firstValueFrom(this.http.get<SignInAppearanceDto>(this.api))) });
    } catch {
      this.loaded = false;
    }
  }

  // ----- Settings > Sign-in page (Admin) -----

  get(): Observable<SignInAppearanceDto> {
    return this.http.get<SignInAppearanceDto>(this.api);
  }

  /** Stores a picture; it is used (and earlier unused uploads removed) once the appearance is saved. */
  upload(file: File): Observable<{ backgroundImage: string; backgroundUrl: string }> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<{ backgroundImage: string; backgroundUrl: string }>(`${this.api}/images`, form);
  }

  save(appearance: SignInAppearance): Observable<SignInAppearanceDto> {
    return this.http.put<SignInAppearanceDto>(this.api, appearance).pipe(tap((saved) => this.appearance.set(saved)));
  }
}
