import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalculator, lucideImages, lucideListTree, lucideMoon, lucideSparkles, lucideSun, lucideTimer, lucideWifiOff } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { PwaService } from '../../core/pwa/pwa.service';
import { LanguageSwitcher } from '../../layout/component/language-switcher';
import { LayoutService } from '../../layout/service/layout.service';

/** Glassmorphism frame for the login, forgot-password and reset-password pages. */
@Component({
  selector: 'app-auth-shell',
  imports: [RouterOutlet, NgIcon, HlmButtonImports, TranslocoPipe, LanguageSwitcher],
  providers: [provideIcons({ lucideSun, lucideMoon, lucideImages, lucideListTree, lucideTimer, lucideCalculator, lucideSparkles, lucideWifiOff })],
  template: `
    <div class="relative isolate min-h-dvh overflow-hidden bg-gradient-to-br from-slate-100 via-blue-50 to-indigo-100 dark:from-slate-950 dark:via-[#06122e] dark:to-slate-950">
      <!-- Decorative background: soft colour blobs behind the frosted glass, plus a faint grid -->
      <div class="pointer-events-none absolute inset-0 -z-10" aria-hidden="true">
        <div class="bg-primary/45 motion-safe:animate-blob absolute -top-40 -left-32 size-[30rem] rounded-full blur-3xl dark:bg-primary/35"></div>
        <div class="motion-safe:animate-blob absolute top-1/4 -right-40 size-[34rem] rounded-full bg-violet-500/30 blur-3xl [animation-delay:-7s] dark:bg-violet-600/25"></div>
        <div class="motion-safe:animate-blob absolute -bottom-48 left-1/5 size-[32rem] rounded-full bg-cyan-400/35 blur-3xl [animation-delay:-14s] dark:bg-cyan-500/20"></div>
        <div class="auth-grid absolute inset-0 opacity-60 dark:opacity-40"></div>
      </div>

      <header class="flex items-center justify-between p-4 sm:px-8 sm:py-6">
        <div class="flex items-center gap-2.5 font-semibold">
          <img src="icons/icon-72x72.png" alt="" width="36" height="36" class="size-9 rounded-xl shadow-lg shadow-primary/30" />
          <span class="text-lg">LT ODM <span class="text-muted-foreground font-normal">{{ 'auth.shell.subtitle' | transloco }}</span></span>
        </div>
        <div class="flex items-center gap-2">
          <app-language-switcher buttonClass="size-11 rounded-full bg-white/40 backdrop-blur-md hover:bg-white/60 dark:bg-white/5 dark:hover:bg-white/10" />
          <button
            hlmBtn
            variant="ghost"
            size="icon"
            class="size-11 rounded-full bg-white/40 backdrop-blur-md hover:bg-white/60 dark:bg-white/5 dark:hover:bg-white/10"
            [attr.aria-label]="(layout.isDark() ? 'layout.switchToLight' : 'layout.switchToDark') | transloco"
            (click)="layout.toggleDarkMode()"
          >
            <ng-icon [name]="layout.isDark() ? 'lucideSun' : 'lucideMoon'" class="text-lg" />
          </button>
        </div>
      </header>

      <main class="mx-auto grid w-full max-w-6xl items-center gap-12 px-4 pb-12 sm:px-8 lg:min-h-[calc(100dvh-7rem)] lg:grid-cols-[1.1fr_1fr]">
        <section class="hidden lg:block">
          <span class="border-primary/20 bg-primary/10 text-primary mb-6 inline-flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-medium backdrop-blur">
            <ng-icon name="lucideSparkles" />
            {{ 'auth.shell.badge' | transloco }}
          </span>
          <h1 class="mb-4 text-5xl leading-tight font-semibold tracking-tight">
            {{ 'auth.shell.headline' | transloco }}
            <span class="from-primary bg-gradient-to-r to-violet-500 bg-clip-text text-transparent">{{ 'auth.shell.headlineAccent' | transloco }}</span>
          </h1>
          <p class="text-muted-foreground mb-10 max-w-lg text-lg">{{ 'auth.shell.intro' | transloco }}</p>
          <ul class="grid max-w-lg grid-cols-2 gap-3">
            @for (f of features; track f.label) {
              <li class="flex items-center gap-3 rounded-xl border border-white/50 bg-white/35 p-3 text-sm font-medium shadow-sm backdrop-blur-md dark:border-white/10 dark:bg-white/5">
                <span class="bg-primary/15 text-primary flex size-9 shrink-0 items-center justify-center rounded-lg"><ng-icon [name]="f.icon" class="text-lg" /></span>
                {{ f.label | transloco }}
              </li>
            }
          </ul>
        </section>

        <div
          class="glass-card w-full max-w-md justify-self-center rounded-3xl border border-white/60 bg-white/85 p-6 shadow-[0_8px_40px_rgba(15,23,42,0.18)] ring-1 ring-white/40 ring-inset sm:p-9 supports-[backdrop-filter]:bg-white/55 supports-[backdrop-filter]:backdrop-blur-2xl supports-[backdrop-filter]:backdrop-saturate-150 dark:border-white/10 dark:bg-slate-900/90 dark:shadow-[0_8px_40px_rgba(0,0,0,0.5)] dark:ring-white/5 dark:supports-[backdrop-filter]:bg-white/[0.06]"
        >
          @if (!pwa.online()) {
            <div class="mb-6 flex items-center gap-2 rounded-lg bg-amber-500/15 px-3 py-2 text-sm text-amber-700 dark:text-amber-300" role="status">
              <ng-icon name="lucideWifiOff" />
              {{ 'auth.shell.offline' | transloco }}
            </div>
          }
          <router-outlet />
        </div>
      </main>
    </div>
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
export class AuthShell {
  protected readonly layout = inject(LayoutService);
  protected readonly pwa = inject(PwaService);

  protected readonly features = [
    { label: 'auth.shell.featureStyles', icon: 'lucideImages' },
    { label: 'auth.shell.featureMaterials', icon: 'lucideListTree' },
    { label: 'auth.shell.featureWorkmanship', icon: 'lucideTimer' },
    { label: 'auth.shell.featureCosting', icon: 'lucideCalculator' },
  ];
}
