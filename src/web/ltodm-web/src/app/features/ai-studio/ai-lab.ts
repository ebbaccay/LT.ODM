import { PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideBot,
  lucideCheck,
  lucideClock,
  lucideCpu,
  lucideDollarSign,
  lucideFileScan,
  lucideFlaskConical,
  lucideImages,
  lucideLightbulb,
  lucideListChecks,
  lucideMessagesSquare,
  lucidePipette,
  lucidePlay,
  lucidePlug,
  lucideTriangleAlert,
  lucideWrench,
} from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { catchError, of } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { StyleDashboard, StyleLibraryService } from '../style-library/style-library.service';
import { AiStudioService } from './ai-studio.service';
import { LAB_CAPABILITIES, LabCapability, LabStatus, ReadinessMetric } from './ai-lab.data';

const STATUS_TONE: Record<LabStatus, Tone> = { ready: 'green', needsData: 'amber', needsInfra: 'violet' };

interface Readiness {
  key: string;
  params: Record<string, string | number>;
  ok: boolean;
}

/**
 * AI Studio > AI Lab: capabilities planned for the production build, each with a SAMPLE result (ai-lab.data.ts,
 * style numbers DEMO-...) next to LIVE numbers from the library that show how ready the data is for it.
 * Nothing on this page calls an AI model. When a capability is built it moves to its own page.
 */
@Component({
  selector: 'app-ai-lab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PercentPipe, RouterLink, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmSpinnerImports, TranslocoPipe],
  providers: [
    provideIcons({
      lucideArrowRight, lucideBot, lucideCheck, lucideClock, lucideCpu, lucideDollarSign, lucideFileScan, lucideFlaskConical, lucideImages, lucideLightbulb,
      lucideListChecks, lucideMessagesSquare, lucidePipette, lucidePlay, lucidePlug, lucideTriangleAlert, lucideWrench,
    }),
  ],
  templateUrl: './ai-lab.html',
})
export class AiLab {
  private readonly styles = inject(StyleLibraryService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ai = inject(AiStudioService);
  private readonly auth = inject(AuthService);

  readonly isAdmin = computed(() => (this.auth.user()?.roles ?? []).some((r) => r.toLowerCase() === 'admin'));
  /** Each job's service (Settings > AI connections), without endpoints or keys. */
  readonly aiStatus = toSignal(this.ai.status().pipe(catchError(() => of(null))), { initialValue: null });
  /** The built feature, when this user may open it. */
  liveFor = (c: LabCapability) => {
    const roles = (this.auth.user()?.roles ?? []).map((r) => r.toLowerCase());
    return c.live && c.live.roles.some((r) => roles.includes(r)) ? c.live : null;
  };

  purposeStatus = (purpose: string) => this.aiStatus()?.purposes.find((p) => p.purpose === purpose) ?? null;

  readonly capabilities = LAB_CAPABILITIES;
  readonly statusTone = (s: LabStatus) => toneBadge(STATUS_TONE[s]);
  readonly statuses: LabStatus[] = ['ready', 'needsData', 'needsInfra'];

  /** Live library numbers (null while loading or when the dashboard cannot be read). */
  readonly dashboard = toSignal(this.styles.dashboard().pipe(catchError(() => of(null))), { initialValue: undefined });

  readonly selectedId = signal(LAB_CAPABILITIES[0].id);
  readonly selected = computed(() => LAB_CAPABILITIES.find((c) => c.id === this.selectedId()) ?? LAB_CAPABILITIES[0]);
  readonly running = signal(false);
  readonly shown = signal<ReadonlySet<string>>(new Set());
  private timer?: ReturnType<typeof setTimeout>;

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.timer));
  }

  select(c: LabCapability): void {
    clearTimeout(this.timer);
    this.running.set(false);
    this.selectedId.set(c.id);
  }

  /** Shows the sample after a short pause, so the demo reads like the real feature would. */
  runDemo(): void {
    const id = this.selectedId();
    this.running.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.running.set(false);
      this.shown.update((s) => new Set(s).add(id));
    }, 900);
  }

  /** Table rows end with a confidence like "79%"; under 85% is flagged for review. */
  lowConfidence = (row: string[]) => parseFloat(row[row.length - 1]) < 85;

  needs = (c: LabCapability) => Array.from({ length: c.needs }, (_, i) => `ai.lab.cap.${c.id}.needs.${i + 1}`);

  readiness(metric: ReadinessMetric, d: StyleDashboard | null | undefined): Readiness | null {
    if (!d) return null;
    const t = d.totals;
    const share = (n: number) => (t.styles ? n / t.styles : 0);
    switch (metric) {
      case 'photos':
        return { key: 'ai.lab.ready.photos', params: { count: t.stylesWithImage, total: t.styles }, ok: share(t.stylesWithImage) >= 0.5 };
      case 'bom':
        return { key: 'ai.lab.ready.bom', params: { count: t.stylesWithBom, total: t.styles }, ok: share(t.stylesWithBom) >= 0.5 };
      case 'materials':
        return { key: 'ai.lab.ready.materials', params: { count: t.materials }, ok: t.materials > 0 };
      case 'colorways':
        return { key: 'ai.lab.ready.colorways', params: { count: t.colorways }, ok: t.colorways > 0 };
      case 'families':
        return { key: 'ai.lab.ready.families', params: { count: t.families, reused: t.reusedStyles }, ok: t.families > 0 };
      case 'library':
        return { key: 'ai.lab.ready.library', params: { styles: t.styles, lines: t.bomLines }, ok: t.styles > 0 };
      case 'lastImport':
        return d.lastImport
          ? { key: 'ai.lab.ready.lastImport', params: { file: d.lastImport.fileName }, ok: true }
          : { key: 'ai.lab.ready.noImport', params: {}, ok: false };
    }
  }
}
