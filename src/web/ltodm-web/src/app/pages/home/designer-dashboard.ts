import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleCheck,
  lucideGauge,
  lucideLightbulb,
  lucidePackage,
  lucidePlus,
  lucideSearch,
  lucideShirt,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { StyleFormDialog } from '../../features/style-library/style-form-dialog';
import { StyleLibraryService, StyleLookups, styleError } from '../../features/style-library/style-library.service';
import { AuthService } from '../../core/auth/auth.service';
import { DashboardsService, DesignerCheck, DesignerDashboard as Data } from './dashboards.service';

type Show = 'todo' | 'done' | 'all';
type Sort = 'score' | 'changed';

/** Which tab of the style page fixes each check (null = the style's header: picture, product type, gender). */
const FIX_TAB: Record<DesignerCheck, string | null> = {
  picture: null,
  productType: null,
  gender: null,
  colorways: 'colorways',
  bom: 'bom',
  colorwaysOnBom: 'bom',
  bomColours: 'bom',
  bomDetails: 'bom',
  consumption: 'bom',
  suppliers: 'bom',
};

const PAGE = 20;

/**
 * Home page for designers: the signed-in user's own active styles (the ones they created), how complete each is and
 * what is still missing (sketch, product type, gender, colorways, BOM and its colours, consumption, suppliers), plus
 * their concepts and where their styles were offered. The checks are worked out by the API (DesignerChecks).
 */
@Component({
  selector: 'app-designer-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    NgIcon,
    AuthImgDirective,
    StyleFormDialog,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSkeletonImports,
    TranslocoPipe,
  ],
  providers: [
    provideIcons({
      lucideCircleCheck,
          lucideGauge,
      lucideLightbulb,
      lucidePackage,
      lucidePlus,
      lucideSearch,
      lucideShirt,
      lucideX,
    }),
  ],
  templateUrl: './designer-dashboard.html',
})
export class DesignerDashboard {
  /** Bumped by the home page's Refresh button: loads again. */
  readonly reload = input(0);
  /** Tells the home page when a load starts and ends (its Refresh button spins). */
  readonly busy = output<boolean>();

  private readonly svc = inject(DashboardsService);
  private readonly styles = inject(StyleLibraryService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly me = computed(() => this.auth.user()?.userName ?? '');

  protected readonly canEdit = this.styles.canEdit;
  protected readonly data = signal<Data | null>(null);
  readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly season = signal<string | null>(null);

  // Table filters (in the browser: the API sends every style in the season).
  protected readonly check = signal<DesignerCheck | null>(null);
  protected readonly show = signal<Show>('todo');
  protected readonly sort = signal<Sort>('score');
  protected readonly search = signal('');
  protected readonly limit = signal(PAGE);
  /** Business unit teams: only the styles the user created (the team's are listed too by default). */
  protected readonly mineOnly = signal(false);
  protected readonly team = computed(() => this.data()?.teamBusinessUnits ?? []);

  protected readonly brokenImages = signal<ReadonlySet<number>>(new Set());
  protected readonly createOpen = signal(false);
  protected readonly lookups = signal<StyleLookups | null>(null);

  protected readonly isEmpty = computed(() => this.data()?.seasons.length === 0);

  protected readonly kpis = computed(() => {
    const t = this.data()?.totals;
    if (!t) return [];
    const pct = t.styles ? Math.floor((t.complete / t.styles) * 100) : 0;
    return [
      {
        label: 'home.designer.kpi.styles',
        value: t.styles,
        suffix: '',
        icon: 'lucideShirt',
        iconBg: 'from-dash-1 to-dash-2',
        glow: 'bg-dash-1',
        hint: { key: 'home.designer.kpi.stylesHint', params: { colorways: n(t.colorways), lines: n(t.bomLines) } },
      },
      {
        label: 'home.designer.kpi.complete',
        value: t.complete,
        suffix: '',
        icon: 'lucideCircleCheck',
        iconBg: 'from-dash-2 to-dash-1',
        glow: 'bg-dash-2',
        hint: { key: 'home.designer.kpi.completeHint', params: { pct, todo: n(t.styles - t.complete) } },
      },
      {
        label: 'home.designer.kpi.average',
        value: t.averageScore,
        suffix: '%',
        icon: 'lucideGauge',
        iconBg: 'from-dash-3 to-dash-4',
        glow: 'bg-dash-3',
        hint: { key: 'home.designer.kpi.averageHint', params: {} },
      },
      {
        label: 'home.designer.kpi.offered',
        value: t.inPackages,
        suffix: '',
        icon: 'lucidePackage',
        iconBg: 'from-dash-4 to-dash-1',
        glow: 'bg-dash-4',
        hint: { key: 'home.designer.kpi.offeredHint', params: { sampling: n(t.sampling), ordered: n(t.ordered) } },
      },
    ];
  });

  /** "What's missing" bars, largest first; checks nobody misses are left out. */
  protected readonly gaps = computed(() => {
    const d = this.data();
    if (!d) return [];
    const max = Math.max(1, ...d.checks.map((c) => c.styles));
    return d.checks
      .filter((c) => c.styles > 0)
      .sort((a, b) => b.styles - a.styles)
      .map((c) => ({ ...c, pct: (c.styles / max) * 100 }));
  });

  protected readonly filtered = computed(() => {
    const d = this.data();
    if (!d) return [];
    const check = this.check();
    const show = this.show();
    const words = this.search().trim().toLowerCase().split(/\s+/).filter(Boolean);
    const me = this.me().toLowerCase();
    const mineOnly = this.mineOnly() && this.team().length > 0;
    const rows = d.styles.filter(
      (s) =>
        (!mineOnly || (s.createdBy ?? '').toLowerCase() === me) &&
        (show === 'all' || (show === 'done') === (s.score === 100)) &&
        (!check || s.missing.some((m) => m.check === check)) &&
        words.every((w) =>
          `${s.styleNo} ${s.description ?? ''} ${s.modelName ?? ''} ${s.customerCode}`.toLowerCase().includes(w),
        ),
    );
    // The API sends the latest change first; "least complete" keeps that order among equal scores.
    return this.sort() === 'score' ? [...rows].sort((a, b) => a.score - b.score) : rows;
  });

  protected readonly visible = computed(() => this.filtered().slice(0, this.limit()));

  constructor() {
    // First load, and again whenever the home page asks.
    effect(() => {
      this.reload();
      untracked(() => this.load());
    });
  }

  load(): void {
    this.loading.set(true);
    this.busy.emit(true);
    this.failed.set(false);
    this.svc.designer(this.season()).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
        this.busy.emit(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
        this.busy.emit(false);
      },
    });
  }

  setSeason(value: string): void {
    this.season.set(value || null);
    this.limit.set(PAGE);
    this.load();
  }

  /** Clicking a "What's missing" bar filters the table to the styles that miss it (again = clear). */
  toggleCheck(check: DesignerCheck): void {
    this.check.update((c) => (c === check ? null : check));
    if (this.check()) this.show.set('todo');
    this.limit.set(PAGE);
  }

  setShow(value: Show): void {
    this.show.set(value);
    this.limit.set(PAGE);
  }

  toggleMine(): void {
    this.mineOnly.update((v) => !v);
    this.limit.set(PAGE);
  }

  setSearch(value: string): void {
    this.search.set(value);
    this.limit.set(PAGE);
  }

  setSort(value: string): void {
    this.sort.set(value === 'changed' ? 'changed' : 'score');
  }

  showMore(): void {
    this.limit.update((l) => l + PAGE);
  }

  /** The style page, on the tab where the gap is fixed. */
  fixTab(check: DesignerCheck): Record<string, string> | null {
    const tab = FIX_TAB[check];
    return tab ? { tab } : null;
  }

  scoreTone(score: number): string {
    return score === 100 ? 'bg-emerald-500' : score >= 70 ? 'bg-dash-1' : score >= 40 ? 'bg-amber-500' : 'bg-destructive';
  }

  markBroken(styleId: number): void {
    this.brokenImages.update((s) => new Set(s).add(styleId));
  }

  openCreate(): void {
    if (!this.lookups()) {
      this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: (e) => toast.error(styleError(e).message) });
    }
    this.createOpen.set(true);
  }

  onCreated(styleId: number): void {
    this.createOpen.set(false);
    void this.router.navigate(['/styles', styleId]);
  }
}

/** Thousands separators for numbers inside translated text. */
function n(value: number): string {
  return value.toLocaleString();
}
