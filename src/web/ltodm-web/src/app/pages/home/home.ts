import { DatePipe, DecimalPipe, LowerCasePipe, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideFileSpreadsheet,
  lucideGitBranch,
  lucideImage,
  lucideListTree,
  lucidePackage,
  lucidePalette,
  lucideRefreshCw,
  lucideServer,
  lucideShirt,
  lucideTriangleAlert,
  lucideUpload,
} from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { AuthService } from '../../core/auth/auth.service';
import { HealthService } from '../../core/health/health.service';
import {
  DashboardCount,
  StyleDashboard,
  StyleLibraryService,
} from '../../features/style-library/style-library.service';

/** Translation key plus its parameters (translated in the template, so it follows language changes). */
interface Text {
  key: string;
  params?: Record<string, unknown>;
}

/** A ranked bar row: label, value and the bar's share of the largest value. Empty label = not set. */
interface RankRow {
  key: string;
  label: string;
  hint: Text | null;
  value: number;
  pct: number;
  link: { path: string; query: Record<string, string> | null } | null;
}

/**
 * Landing page: the Style Library at a glance (styles, colorways, BOM lines; where they are concentrated, how
 * complete the data is, and what changed last). Users who cannot read the library (factories) get the welcome only.
 */
@Component({
  selector: 'app-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    DecimalPipe,
    LowerCasePipe,
    NgTemplateOutlet,
    RouterLink,
    NgIcon,
    AuthImgDirective,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    TranslocoPipe,
  ],
  providers: [
    provideIcons({
      lucideArrowRight,
      lucideFileSpreadsheet,
      lucideGitBranch,
      lucideImage,
      lucideListTree,
      lucidePackage,
      lucidePalette,
      lucideRefreshCw,
      lucideServer,
      lucideShirt,
      lucideTriangleAlert,
      lucideUpload,
    }),
  ],
  templateUrl: './home.html',
})
export class Home implements OnInit {
  private readonly svc = inject(StyleLibraryService);
  private readonly auth = inject(AuthService);

  protected readonly health = toSignal(inject(HealthService).getHealth());
  protected readonly canRead = this.svc.canRead;
  protected readonly isAdmin = computed(() =>
    (this.auth.user()?.roles ?? []).some((r) => r.toLowerCase() === 'admin'),
  );
  protected readonly firstName = computed(
    () => (this.auth.user()?.displayName ?? '').trim().split(/\s+/)[0] ?? '',
  );
  /** Translation key. */
  protected readonly greeting = (() => {
    const h = new Date().getHours();
    return h < 12 ? 'home.goodMorning' : h < 18 ? 'home.goodAfternoon' : 'home.goodEvening';
  })();

  protected readonly data = signal<StyleDashboard | null>(null);
  protected readonly loading = signal(false);
  /** Translation key of the load error. */
  protected readonly error = signal<string | null>(null);
  protected readonly brokenImages = signal<ReadonlySet<number>>(new Set());

  protected readonly isEmpty = computed(() => this.data()?.totals.styles === 0);

  /** label and hint are translation keys / Text; iconBg and glow give each tile one of the --dash-* colours. */
  protected readonly kpis = computed(() => {
    const t = this.data()?.totals;
    if (!t) return [];
    return [
      {
        label: 'home.kpi.styles',
        value: t.styles,
        icon: 'lucideShirt',
        iconBg: 'from-dash-1 to-dash-2',
        glow: 'bg-dash-1',
        hint: {
          key: 'home.kpi.stylesHint',
          params: { customers: n(t.customers), seasons: n(t.seasons) },
        },
        link: '/styles',
      },
      {
        label: 'home.kpi.colorways',
        value: t.colorways,
        icon: 'lucidePalette',
        iconBg: 'from-dash-2 to-dash-1',
        glow: 'bg-dash-2',
        hint: {
          key: 'home.kpi.colorwaysHint',
          params: { inRange: n(t.colorwaysInRange), dropped: n(t.colorways - t.colorwaysInRange) },
        },
        link: null,
      },
      {
        label: 'home.kpi.bomLines',
        value: t.bomLines,
        icon: 'lucideListTree',
        iconBg: 'from-dash-3 to-dash-4',
        glow: 'bg-dash-3',
        hint: {
          key: 'home.kpi.bomLinesHint',
          params: { avg: this.avg(t.bomLines, t.stylesWithBom) },
        },
        link: null,
      },
      {
        label: 'home.kpi.materials',
        value: t.materials,
        icon: 'lucidePackage',
        iconBg: 'from-dash-4 to-dash-1',
        glow: 'bg-dash-4',
        hint: { key: 'home.kpi.materialsHint', params: { suppliers: n(t.suppliers) } },
        link: null,
      },
    ];
  });

  protected readonly seasonMax = computed(() =>
    Math.max(1, ...(this.data()?.seasons ?? []).map((s) => s.styles)),
  );

  /** How complete the library is: each row is a share of styles or colorways (label: translation key). */
  protected readonly completeness = computed(() => {
    const t = this.data()?.totals;
    if (!t) return [];
    return [
      { label: 'home.withBom', done: t.stylesWithBom, of: t.styles },
      { label: 'home.withColorways', done: t.stylesWithColorways, of: t.styles },
      { label: 'home.colorwaysOnBom', done: t.colorwaysWithBom, of: t.colorways },
      { label: 'home.withImage', done: t.stylesWithImage, of: t.styles },
    ].map((r) => ({ ...r, pct: r.of ? Math.round((r.done / r.of) * 100) : 0 }));
  });

  protected readonly customers = computed(() =>
    this.rank(
      this.data()?.customers ?? [],
      (c) => c.styles,
      (c) => (c.colorways ? { key: 'home.nColorways', params: { count: n(c.colorways) } } : null),
      'customer',
    ),
  );
  protected readonly productTypes = computed(() =>
    this.rank(
      this.data()?.productTypes ?? [],
      (c) => c.styles,
      () => null,
      'productType',
    ),
  );
  protected readonly materialTypes = computed(() =>
    this.rank(
      this.data()?.materialTypes ?? [],
      (c) => c.bomLines ?? 0,
      (c) => ({ key: 'home.nStyles', params: { count: n(c.styles) } }),
      null,
    ),
  );
  protected readonly suppliers = computed(() =>
    this.rank(
      this.data()?.suppliers ?? [],
      (c) => c.styles,
      (c) => ({ key: 'home.nLines', params: { count: n(c.bomLines ?? 0) } }),
      null,
    ),
  );
  protected readonly materialMax = computed(() =>
    Math.max(1, ...(this.data()?.materials ?? []).map((m) => m.styles)),
  );

  ngOnInit(): void {
    if (this.canRead()) this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.dashboard().subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('home.loadFailed');
        this.loading.set(false);
      },
    });
  }

  markBroken(styleId: number): void {
    this.brokenImages.update((s) => new Set(s).add(styleId));
  }

  private avg(total: number, count: number): string {
    return count ? (total / count).toFixed(1) : '0';
  }

  /** Bars scaled to the largest value; rows with a code link to the styles list filtered by it. */
  private rank(
    rows: DashboardCount[],
    value: (c: DashboardCount) => number,
    hint: (c: DashboardCount) => Text | null,
    filterKey: string | null,
  ): RankRow[] {
    const max = Math.max(1, ...rows.map(value));
    return rows.map((c) => ({
      key: c.code || '(none)',
      label: c.code && c.name && c.name !== c.code ? c.name : c.name || c.code || '',
      hint: hint(c),
      value: value(c),
      pct: (value(c) / max) * 100,
      link: filterKey && c.code ? { path: '/styles', query: { [filterKey]: c.code } } : null,
    }));
  }
}

/** Thousands separators for numbers inside translated text. */
function n(value: number): string {
  return value.toLocaleString();
}
