import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { SignInAppearanceDto } from '../../core/branding/sign-in-appearance';

const FOCUS_POSITION: Record<string, string> = { center: '50% 50%', top: '50% 15%', bottom: '50% 85%', left: '15% 50%', right: '85% 50%' };

/**
 * Background of the sign-in pages: the default gradient, the uploaded picture (focus point, blur), a tint for
 * legibility and the soft colour glow with a faint grid. Fills its positioned parent; used by the sign-in frame and
 * by the preview in Settings > Sign-in page.
 */
@Component({
  selector: 'app-auth-backdrop',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'pointer-events-none absolute inset-0 -z-10 overflow-hidden', 'aria-hidden': 'true' },
  template: `
    <div class="absolute inset-0 bg-gradient-to-br from-slate-100 via-blue-50 to-indigo-100 dark:from-slate-950 dark:via-[#06122e] dark:to-slate-950"></div>
    @if (appearance().backgroundUrl; as url) {
      <img
        [src]="url"
        alt=""
        decoding="async"
        fetchpriority="high"
        class="absolute inset-0 size-full object-cover"
        [style.object-position]="position()"
        [style.filter]="appearance().backgroundBlur ? 'blur(' + appearance().backgroundBlur + 'px)' : null"
        [style.transform]="appearance().backgroundBlur ? 'scale(1.06)' : null"
      />
    }
    @if (appearance().overlay !== 'none') {
      <div class="absolute inset-0" [class]="overlayClass()" [style.opacity]="appearance().overlayStrength / 100"></div>
    }
    @if (appearance().showGlow) {
      <div class="bg-primary/45 motion-safe:animate-blob absolute -top-40 -left-32 size-[30rem] rounded-full blur-3xl dark:bg-primary/35"></div>
      <div class="motion-safe:animate-blob absolute top-1/4 -right-40 size-[34rem] rounded-full bg-violet-500/30 blur-3xl [animation-delay:-7s] dark:bg-violet-600/25"></div>
      <div class="motion-safe:animate-blob absolute -bottom-48 left-1/5 size-[32rem] rounded-full bg-cyan-400/35 blur-3xl [animation-delay:-14s] dark:bg-cyan-500/20"></div>
      <div class="auth-grid absolute inset-0 opacity-60 dark:opacity-40"></div>
    }
  `,
  styles: `
    /* Faint grid that fades out towards the edges. */
    .auth-grid {
      background-image:
        linear-gradient(to right, color-mix(in oklch, var(--foreground) 6%, transparent) 1px, transparent 1px),
        linear-gradient(to bottom, color-mix(in oklch, var(--foreground) 6%, transparent) 1px, transparent 1px);
      background-size: 48px 48px;
      mask-image: radial-gradient(ellipse at center, black 30%, transparent 75%);
    }
  `,
})
export class AuthBackdrop {
  readonly appearance = input.required<SignInAppearanceDto>();

  protected readonly position = computed(() => FOCUS_POSITION[this.appearance().focus] ?? FOCUS_POSITION['center']);
  protected readonly overlayClass = computed(() => {
    switch (this.appearance().overlay) {
      case 'dark':
        return 'bg-slate-950';
      case 'light':
        return 'bg-white';
      default:
        return 'bg-gradient-to-br from-primary to-violet-700';
    }
  });
}

/** CSS variables for the frosted card (see .auth-glass in the sign-in frame and the preview). */
export function glassVars(a: SignInAppearanceDto): Record<string, string> {
  return { '--glass-alpha': String(a.glassOpacity / 100), '--glass-blur': `${a.glassBlur}px` };
}

/** Text colour classes for the headline panel when it sits on a picture. */
export function introTextClass(a: SignInAppearanceDto): string {
  if (a.introText === 'light') return 'auth-intro-light';
  if (a.introText === 'dark') return 'auth-intro-dark';
  return '';
}
