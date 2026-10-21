import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideCalculator,
  lucideCalendarRange,
  lucideCheck,
  lucideChevronLeft,
  lucideChevronRight,
  lucideShirt,
  lucideSparkles,
  lucideSquare,
  lucideUsers,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { firstValueFrom } from 'rxjs';
import {
  GapField,
  GapItem,
  GapPage,
  GapSeason,
  GapShow,
  GapSummary,
  LcoGroup,
  StyleGapsService,
} from './style-gaps.service';
import { GENDERS, StyleLibraryService, StyleLookups, styleError } from './style-library.service';

type Tab = GapField | 'seasons' | 'lco';
const TABS: Tab[] = ['gender', 'productType', 'seasons', 'lco'];
const PAGE = 50;

/**
 * Style Library > Fill the gaps: what the library is missing, with suggestions to accept. Gender and product type come
 * from rules (description words, style number, account name, other seasons of the style, garment words), and from AI for
 * the styles the rules cannot read; free-form seasons merge into year-term seasons; empty LCO consumption is filled from
 * the brand consumption. Nothing changes a style until someone accepts. Admins and merchandisers change; others look.
 */
@Component({
  selector: 'app-gaps-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports,
    HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft, lucideCalculator, lucideCalendarRange, lucideCheck, lucideChevronLeft, lucideChevronRight, lucideShirt, lucideSparkles,
      lucideSquare, lucideUsers, lucideX,
    }),
  ],
  templateUrl: './gaps-page.html',
})
export class GapsPage implements OnInit {
  private readonly svc = inject(StyleGapsService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly canEdit = this.styles.canEdit;
  readonly genders = GENDERS;
  readonly tabs = TABS;

  readonly tab = signal<Tab>('gender');
  readonly summary = signal<GapSummary | null>(null);
  readonly lookups = signal<StyleLookups | null>(null);

  // ── Gender / product type ──
  readonly customer = signal('');
  readonly season = signal('');
  readonly show = signal<GapShow>('suggested');
  readonly minConfidence = signal(1);
  readonly skip = signal(0);
  readonly page = signal<GapPage | null>(null);
  readonly loadingPage = signal(false);
  /** Ticked rows and the value chosen for each (the suggestion unless changed by hand). */
  readonly ticked = signal<ReadonlySet<number>>(new Set());
  readonly chosen = signal<ReadonlyMap<number, string>>(new Map());
  readonly busy = signal(false);
  /** Ask AI: runs batch after batch until nothing is left or Stop. */
  readonly asking = signal(false);
  readonly askProgress = signal<{ saved: number; unclear: number; remaining: number } | null>(null);
  private stopAsking = false;

  readonly field = computed<GapField | null>(() => (this.tab() === 'gender' || this.tab() === 'productType' ? (this.tab() as GapField) : null));
  readonly items = computed(() => this.page()?.items ?? []);
  readonly allTicked = computed(() => {
    const rows = this.items().filter((i) => this.valueFor(i));
    return rows.length > 0 && rows.every((i) => this.ticked().has(i.styleId));
  });
  readonly counts = computed(() => this.page()?.counts ?? {});
  readonly highInScope = computed(() => this.counts()['3'] ?? 0);
  readonly noneInScope = computed(() => this.counts()['0'] ?? 0);
  readonly productTypeOptions = computed(() => this.lookups()?.productTypes ?? []);

  // ── Seasons ──
  readonly seasons = signal<GapSeason[] | null>(null);
  readonly seasonTargets = signal<ReadonlyMap<string, string>>(new Map());
  readonly yearTermSeasons = computed(() => (this.lookups()?.seasons ?? []).filter((s) => /^\d{4}-[A-Z]{2,4}$/.test(s.code)));

  // ── LCO ──
  readonly withAllowance = signal(false);
  readonly lcoGroups = signal<LcoGroup[] | null>(null);
  readonly lcoTotals = computed(() =>
    (this.lcoGroups() ?? []).reduce((t, g) => ({ lines: t.lines + g.lines, suggested: t.suggested + g.suggested }), { lines: 0, suggested: 0 }),
  );

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((q) => {
      const tab = q.get('tab') as Tab | null;
      this.tab.set(tab && TABS.includes(tab) ? tab : 'gender');
      this.customer.set(q.get('customer') ?? '');
      this.season.set(q.get('season') ?? '');
      this.loadTab();
    });
    this.loadSummary();
  }

  openTab(tab: Tab): void {
    void this.router.navigate([], { queryParams: { tab, customer: null, season: null }, queryParamsHandling: 'merge' });
  }

  loadSummary(): void {
    this.svc.summary().subscribe({ next: (s) => this.summary.set(s), error: (e) => toast.error(styleError(e).message) });
  }

  private loadTab(): void {
    const t = this.tab();
    if (t === 'seasons') this.loadSeasons();
    else if (t === 'lco') this.loadLco();
    else {
      this.skip.set(0);
      this.loadPage();
    }
  }

  // ── Gender / product type ───────────────────────────────

  setFilter(key: 'customer' | 'season', value: string): void {
    void this.router.navigate([], { queryParams: { [key]: value || null }, queryParamsHandling: 'merge' });
  }

  setShow(value: GapShow): void {
    this.show.set(value);
    this.skip.set(0);
    this.loadPage();
  }

  setMinConfidence(value: number): void {
    this.minConfidence.set(value);
    this.skip.set(0);
    this.loadPage();
  }

  goPage(delta: number): void {
    this.skip.set(Math.max(0, this.skip() + delta * PAGE));
    this.loadPage();
  }

  loadPage(): void {
    const field = this.field();
    if (!field) return;
    this.loadingPage.set(true);
    this.svc
      .list(field, {
        customer: this.customer() || undefined, season: this.season() || undefined, show: this.show(), minConfidence: this.minConfidence(),
        skip: this.skip(), take: PAGE,
      })
      .subscribe({
        next: (p) => {
          this.page.set(p);
          this.chosen.set(new Map(p.items.filter((i) => i.value).map((i) => [i.styleId, i.value!])));
          // Start with the confident suggestions ticked; Check rows wait for a look.
          this.ticked.set(new Set(p.items.filter((i) => i.value && i.confidence >= 2).map((i) => i.styleId)));
          this.loadingPage.set(false);
        },
        error: (e) => {
          this.loadingPage.set(false);
          toast.error(styleError(e).message);
        },
      });
  }

  valueFor = (i: GapItem) => this.chosen().get(i.styleId) ?? '';

  choose(i: GapItem, value: string): void {
    this.chosen.update((m) => {
      const next = new Map(m);
      if (value) next.set(i.styleId, value);
      else next.delete(i.styleId);
      return next;
    });
    this.ticked.update((s) => {
      const next = new Set(s);
      if (value) next.add(i.styleId);
      else next.delete(i.styleId);
      return next;
    });
  }

  toggle(styleId: number): void {
    this.ticked.update((s) => {
      const next = new Set(s);
      if (!next.delete(styleId)) next.add(styleId);
      return next;
    });
  }

  toggleAll(): void {
    this.ticked.set(this.allTicked() ? new Set() : new Set(this.items().filter((i) => this.valueFor(i)).map((i) => i.styleId)));
  }

  /** Translation key for the confidence badge. */
  confidenceKey = (c: number) => (c >= 3 ? 'gaps.confidence.high' : c === 2 ? 'gaps.confidence.medium' : c === 1 ? 'gaps.confidence.check' : 'gaps.confidence.none');
  confidenceTone = (c: number) => (c >= 3 ? 'default' : c === 2 ? 'secondary' : 'outline');
  genderLabel = (code: string | null) => (GENDERS.some((g) => g.code === code) ? `styles.gender.${code}` : (code ?? ''));

  acceptTicked(): void {
    const field = this.field();
    const items = [...this.ticked()].map((id) => ({ styleId: id, value: this.chosen().get(id) ?? '' })).filter((i) => i.value);
    if (!field || !items.length || this.busy()) return;
    this.busy.set(true);
    this.svc.accept(field, { items }).subscribe({
      next: (r) => this.done('gaps.accepted', { count: r.applied }),
      error: (e) => this.failed(e),
    });
  }

  async acceptAllHigh(): Promise<void> {
    const field = this.field();
    if (!field || this.busy()) return;
    const ok = await this.confirmDlg.confirm({
      title: this.t('gaps.acceptAllHighTitle'),
      message: this.t('gaps.acceptAllHighMessage', { count: this.highInScope(), field: this.t(`gaps.tabs.${field}`).toLowerCase() }),
      confirmLabel: this.t('gaps.acceptAllHighConfirm'),
      cancelLabel: this.t('actions.cancel'),
    });
    if (!ok) return;
    this.busy.set(true);
    this.svc.accept(field, { customer: this.customer() || undefined, season: this.season() || undefined, minConfidence: 3 }).subscribe({
      next: (r) => this.done('gaps.accepted', { count: r.applied }),
      error: (e) => this.failed(e),
    });
  }

  dismissTicked(): void {
    const field = this.field();
    const items = [...this.ticked()]
      .map((id) => this.items().find((i) => i.styleId === id))
      .filter((i): i is GapItem => !!i?.value)
      .map((i) => ({ styleId: i.styleId, value: i.value! }));
    if (!field || !items.length || this.busy()) return;
    this.busy.set(true);
    this.svc.dismiss(field, items).subscribe({
      next: (r) => this.done('gaps.dismissed', { count: r.dismissed }),
      error: (e) => this.failed(e),
    });
  }

  /** Asks the AI about the styles the rules could not read, 25 at a time, until none are left or Stop. */
  async askAi(): Promise<void> {
    const field = this.field();
    if (!field || this.asking()) return;
    this.asking.set(true);
    this.stopAsking = false;
    let saved = 0;
    let unclear = 0;
    this.askProgress.set({ saved, unclear, remaining: this.noneInScope() });
    try {
      while (!this.stopAsking) {
        const r = await firstValueFrom(this.svc.askAi(field, this.customer() || undefined, this.season() || undefined));
        saved += r.saved;
        unclear += r.unclear;
        this.askProgress.set({ saved, unclear, remaining: r.remaining });
        if (r.remaining === 0 || r.saved + r.unclear === 0) break;
      }
      toast.success(this.t('gaps.ai.finished', { saved, unclear }));
    } catch (e) {
      toast.error(styleError(e).message);
    } finally {
      this.asking.set(false);
      this.askProgress.set(null);
      this.loadSummary();
      this.loadPage();
    }
  }

  stopAi(): void {
    this.stopAsking = true;
  }

  // ── Seasons ─────────────────────────────────────────────

  private loadSeasons(): void {
    this.seasons.set(null);
    this.svc.seasons().subscribe({
      next: (rows) => {
        this.seasons.set(rows);
        this.seasonTargets.set(new Map(rows.map((r) => [r.seasonCode, r.suggested ?? ''])));
      },
      error: (e) => toast.error(styleError(e).message),
    });
  }

  targetFor = (code: string) => this.seasonTargets().get(code) ?? '';

  setTarget(code: string, value: string): void {
    this.seasonTargets.update((m) => new Map(m).set(code, value.trim().toUpperCase()));
  }

  validTarget = (code: string) => /^\d{4}-[A-Z]{2,4}$/.test(this.targetFor(code));

  async merge(row: GapSeason): Promise<void> {
    const to = this.targetFor(row.seasonCode);
    if (!this.validTarget(row.seasonCode) || this.busy()) return;
    const ok = await this.confirmDlg.confirm({
      title: this.t('gaps.seasons.mergeTitle', { from: row.seasonCode, to }),
      message: this.t('gaps.seasons.mergeMessage', { from: row.seasonCode, to, count: row.styles }),
      confirmLabel: this.t('gaps.seasons.merge'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'warning',
    });
    if (!ok) return;
    this.busy.set(true);
    this.svc.mergeSeason(row.seasonCode, to).subscribe({
      next: (r) => {
        this.styles.refreshLookups();
        this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
        this.done(r.conflicts ? 'gaps.seasons.mergedWithConflicts' : 'gaps.seasons.merged', { moved: r.moved, conflicts: r.conflicts, to });
      },
      error: (e) => this.failed(e),
    });
  }

  // ── LCO ─────────────────────────────────────────────────

  private loadLco(): void {
    this.lcoGroups.set(null);
    this.svc.lcoSummary(this.withAllowance()).subscribe({
      next: (rows) => this.lcoGroups.set(rows),
      error: (e) => toast.error(styleError(e).message),
    });
  }

  toggleAllowance(): void {
    this.withAllowance.update((v) => !v);
    this.loadLco();
  }

  async fillLco(group: LcoGroup | null): Promise<void> {
    if (this.busy()) return;
    const count = group ? group.suggested : this.lcoTotals().suggested;
    const ok = await this.confirmDlg.confirm({
      title: this.t('gaps.lco.fillTitle'),
      message: this.t(group ? 'gaps.lco.fillGroupMessage' : 'gaps.lco.fillAllMessage', {
        count, account: group?.account, season: group?.seasonCode,
        how: this.t(this.withAllowance() ? 'gaps.lco.howAllowance' : 'gaps.lco.howBrand'),
      }),
      confirmLabel: this.t('gaps.lco.fillConfirm'),
      cancelLabel: this.t('actions.cancel'),
    });
    if (!ok) return;
    this.busy.set(true);
    this.svc
      .applyLco(group ? { customer: group.customerCode, season: group.seasonCode, withAllowance: this.withAllowance() } : { all: true, withAllowance: this.withAllowance() })
      .subscribe({
        next: (r) => this.done('gaps.lco.filledStyles', { count: r.updated, styles: r.styles }),
        error: (e) => this.failed(e),
      });
  }

  // ── Helpers ─────────────────────────────────────────────

  private done(key: string, params: Record<string, unknown>): void {
    this.busy.set(false);
    toast.success(this.t(key, params));
    this.loadSummary();
    this.loadTab();
  }

  private failed(e: unknown): void {
    this.busy.set(false);
    toast.error(styleError(e).message);
    this.loadTab();
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(key, params);
  }
}
