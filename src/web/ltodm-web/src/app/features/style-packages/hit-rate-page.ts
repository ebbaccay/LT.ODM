import { DecimalPipe, NgTemplateOutlet, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChartColumn, lucideInfo, lucideTable } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { HitRate, HitRateRow, PackagesService } from './packages.service';

type Group = 'byGarment' | 'byClient' | 'bySource';

/** A piece of a bar: a colour role (CSS variable) and how many styles. */
interface Segment {
  key: string;
  n: number;
}

interface Tip {
  x: number;
  y: number;
  title: string;
  rows: { label: string; value: string; color: string }[];
}

/** yyyy-MM-dd of a date (UTC). */
function day(d: Date): string {
  return d.toISOString().slice(0, 10);
}

/** Rows a breakdown shows before "Show all". */
const FIRST_ROWS = 8;

/**
 * Style Packages > Hit rate (roadmap 5.17): what came of the styles offered in packages sent to clients - answered through
 * share links (liked / passed) and the outcome set on the package page (sampling, ordered, dropped) - in total and by
 * garment, client and where the style was added from (does What to offer work?). Filters: customer's brand and the dates
 * the packages were sent (default: the last 12 months), kept in the address.
 *
 * Laid out as a dashboard: the hit rate (to sampling or ordered) as the headline, the steps from offer to order, client
 * answers and outcomes as part-to-whole bars, and per breakdown a bar per row (length = styles offered, split by outcome)
 * with a table view. Colours: blue = good (ordinal steps for the funnel), red = passed / dropped, grey = neutral.
 */
@Component({
  selector: 'app-hit-rate-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, NgTemplateOutlet, PercentPipe, NgIcon, HlmCardImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideChartColumn, lucideInfo, lucideTable })],
  styles: `
    :host {
      --hr-step1: #86b6ef;
      --hr-step2: #5598e7;
      --hr-step3: #2a78d6;
      --hr-step4: #1c5cab;
      --hr-step5: #104281;
      --hr-good: #2a78d6;
      --hr-ordered: #1c5cab;
      --hr-sampling: #6da7ec;
      --hr-bad: #e34948;
      --hr-mid: #b5b4ac;
      --hr-open: #e1e0d9;
      --hr-track: #f0efec;
    }
    :host-context(.dark) {
      --hr-step1: #184f95;
      --hr-step2: #2a78d6;
      --hr-step3: #5598e7;
      --hr-step4: #9ec5f4;
      --hr-step5: #cde2fb;
      --hr-good: #3987e5;
      --hr-ordered: #9ec5f4;
      --hr-sampling: #2a78d6;
      --hr-bad: #e66767;
      --hr-mid: #6b6a65;
      --hr-open: #383835;
      --hr-track: #2c2c2a;
    }
    .hr-bar {
      display: flex;
      gap: 2px;
      overflow: hidden;
      border-radius: 0 4px 4px 0;
    }
    .hr-bar > span {
      height: 100%;
    }
    .hr-row:hover .hr-bar,
    .hr-row:focus-visible .hr-bar {
      filter: brightness(1.08);
    }
  `,
  template: `
    <section class="flex flex-col gap-5" [attr.aria-busy]="loading()">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideChartColumn" />{{ 'packages.hitRate.title' | transloco }}</h1>
          <p class="text-muted-foreground mt-1 max-w-3xl text-sm">{{ 'packages.hitRate.subtitle' | transloco }}</p>
        </div>
        <div class="grid w-full grid-cols-2 gap-2 sm:flex sm:w-auto sm:items-end">
          <hlm-native-select selectId="hr-customer" class="col-span-2" selectClass="h-11 w-full sm:w-56 lg:h-9" [attr.aria-label]="'packages.allCustomers' | transloco"
                             [value]="customer()" (valueChange)="setFilter('customer', $event ?? '')">
            <option hlmNativeSelectOption value="">{{ 'packages.allCustomers' | transloco }}</option>
            @for (c of lookups()?.customers ?? []; track c.code) { <option hlmNativeSelectOption [value]="c.code">{{ c.code === c.name ? c.code : c.code + ' · ' + c.name }}</option> }
          </hlm-native-select>
          <label class="flex flex-col gap-1 text-xs">
            <span class="text-muted-foreground">{{ 'packages.hitRate.from' | transloco }}</span>
            <input hlmInput type="date" class="h-11 lg:h-9" [value]="from()" (change)="setFilter('from', $any($event.target).value)" />
          </label>
          <label class="flex flex-col gap-1 text-xs">
            <span class="text-muted-foreground">{{ 'packages.hitRate.to' | transloco }}</span>
            <input hlmInput type="date" class="h-11 lg:h-9" [value]="to()" (change)="setFilter('to', $any($event.target).value)" />
          </label>
        </div>
      </header>

      @if (loading() && !rate()) {
        <div class="grid gap-4 lg:grid-cols-3"><div hlmSkeleton class="h-64 w-full"></div><div hlmSkeleton class="h-64 w-full lg:col-span-2"></div></div>
        <div hlmSkeleton class="h-64 w-full"></div>
      } @else if (rate(); as r) {
        @let t = r.totals;
        @if (!t.offered) {
          <section hlmCard class="items-center gap-2 px-5 py-14 text-center">
            <ng-icon name="lucideChartColumn" class="text-muted-foreground text-4xl" />
            <p class="font-medium">{{ 'packages.hitRate.empty' | transloco }}</p>
            <p class="text-muted-foreground max-w-md text-sm">{{ 'packages.hitRate.emptyHint' | transloco }}</p>
          </section>
        } @else {
          <!-- A refetch keeps the last picture, dimmed -->
          <div class="flex flex-col gap-4 transition-opacity" [class.opacity-60]="loading()">
            <div class="grid gap-4 lg:grid-cols-3">
              <!-- Headline: the hit rate, and the four figures behind it -->
              <section hlmCard class="gap-4 p-5">
                <div>
                  <h2 class="text-muted-foreground text-sm">{{ 'packages.hitRate.hero' | transloco }}</h2>
                  <p class="mt-1 text-5xl font-semibold tracking-tight">{{ ratio(t.sampling + t.ordered, t.offered) | percent }}</p>
                  <p class="text-muted-foreground mt-1 text-xs">{{ 'packages.hitRate.heroSub' | transloco: { count: t.sampling + t.ordered, offered: t.offered } }}</p>
                </div>
                <dl class="grid grid-cols-2 gap-x-4 gap-y-3 border-t pt-4">
                  @for (k of [
                    { label: 'packages.hitRate.offered', value: (t.offered | number), sub: ('packages.hitRate.inPackages' | transloco: { count: t.packages }), share: null },
                    { label: 'packages.hitRate.answered', value: (ratio(t.answered, t.offered) | percent), sub: (t.answered | number) + ' / ' + (t.offered | number), share: ratio(t.answered, t.offered) },
                    { label: 'packages.hitRate.positive', value: (ratio(t.likeVotes, t.likeVotes + t.passVotes) | percent), sub: ('packages.hitRate.votes' | transloco: { likes: t.likeVotes, passes: t.passVotes }), share: ratio(t.likeVotes, t.likeVotes + t.passVotes) },
                    { label: 'packages.hitRate.ordered', value: (ratio(t.ordered, t.offered) | percent), sub: ('packages.hitRate.orderedSub' | transloco: { count: t.ordered }), share: ratio(t.ordered, t.offered) }
                  ]; track k.label) {
                    <div class="flex min-w-0 flex-col gap-0.5">
                      <dt class="text-muted-foreground text-xs">{{ k.label | transloco }}</dt>
                      <dd class="text-xl font-semibold">{{ k.value }}</dd>
                      @if (k.share !== null) {
                        <span class="block h-1.5 overflow-hidden rounded-full" style="background: var(--hr-track)" aria-hidden="true">
                          <span class="block h-full rounded-full" style="background: var(--hr-good)" [style.width.%]="k.share * 100"></span>
                        </span>
                      }
                      <span class="text-muted-foreground truncate text-xs" [title]="k.sub">{{ k.sub }}</span>
                    </div>
                  }
                </dl>
              </section>

              <!-- From offer to order: each step as a share of the styles offered -->
              <section hlmCard class="gap-4 p-5 lg:col-span-2">
                <div>
                  <h2 class="font-semibold">{{ 'packages.hitRate.funnel' | transloco }}</h2>
                  <p class="text-muted-foreground text-xs">{{ 'packages.hitRate.funnelHint' | transloco }}</p>
                </div>
                <ol class="flex flex-col gap-3">
                  @for (s of funnel(t); track s.key; let i = $index) {
                    <li class="grid grid-cols-[7.5rem_1fr] items-center gap-3 sm:grid-cols-[9rem_1fr]">
                      <span class="text-sm">{{ 'packages.hitRate.stage.' + s.key | transloco }}</span>
                      <span class="flex items-center gap-2">
                        @if (s.n) {
                          <span class="block h-6 rounded-r-[4px]" [style.width.%]="ratio(s.n, t.offered) * 85" [style.background]="'var(--hr-step' + (i + 1) + ')'"></span>
                        } @else {
                          <span class="block h-6 w-px" style="background: var(--hr-mid)"></span>
                        }
                        <span class="text-sm whitespace-nowrap tabular-nums"><span class="font-semibold">{{ s.n | number }}</span>
                          <span class="text-muted-foreground"> · {{ ratio(s.n, t.offered) | percent }}</span></span>
                      </span>
                    </li>
                  }
                </ol>
              </section>
            </div>

            <!-- Part-to-whole: how clients answered, and what LT decided -->
            <div class="grid gap-4 lg:grid-cols-2">
              <section hlmCard class="gap-4 p-5">
                <div>
                  <h2 class="font-semibold">{{ 'packages.hitRate.answersTitle' | transloco }}</h2>
                  <p class="text-muted-foreground text-xs">{{ 'packages.hitRate.answersHint' | transloco }}</p>
                </div>
                <ng-container [ngTemplateOutlet]="whole" [ngTemplateOutletContext]="{ segs: answerSegments(t), total: t.offered, title: 'packages.hitRate.answersTitle' }" />
                <div class="flex flex-col gap-2 border-t pt-4">
                  <p class="flex items-baseline justify-between gap-2 text-sm">
                    <span class="font-medium">{{ 'packages.hitRate.votesTitle' | transloco }}</span>
                    <span class="text-muted-foreground text-xs">{{ 'packages.hitRate.positiveShare' | transloco: { pct: (ratio(t.likeVotes, t.likeVotes + t.passVotes) | percent) } }}</span>
                  </p>
                  <ng-container [ngTemplateOutlet]="whole" [ngTemplateOutletContext]="{ segs: [{ key: 'likes', n: t.likeVotes }, { key: 'passes', n: t.passVotes }], total: t.likeVotes + t.passVotes, title: 'packages.hitRate.votesTitle' }" />
                </div>
              </section>
              <section hlmCard class="gap-4 p-5">
                <div>
                  <h2 class="font-semibold">{{ 'packages.hitRate.outcomesTitle' | transloco }}</h2>
                  <p class="text-muted-foreground text-xs">{{ 'packages.hitRate.outcomesHint' | transloco }}</p>
                </div>
                <ng-container [ngTemplateOutlet]="whole" [ngTemplateOutletContext]="{ segs: outcomeSegments(t), total: t.offered, title: 'packages.hitRate.outcomesTitle' }" />
              </section>
            </div>

            <!-- Breakdowns: one bar per row (length = styles offered, split by outcome), or the table -->
            @for (g of groups; track g) {
              @let rows = r[g];
              <section hlmCard class="gap-0 py-0">
                <div class="flex flex-wrap items-center justify-between gap-2 border-b px-5 py-3">
                  <h2 class="font-semibold">{{ 'packages.hitRate.' + g | transloco }}</h2>
                  <div class="flex rounded-md border p-0.5 text-xs" role="group" [attr.aria-label]="'packages.hitRate.' + g | transloco">
                    <button type="button" class="flex h-9 items-center gap-1 rounded px-2.5 lg:h-7" [class.bg-muted]="view()[g] !== 'table'" [attr.aria-pressed]="view()[g] !== 'table'" (click)="setView(g, 'chart')">
                      <ng-icon name="lucideChartColumn" />{{ 'packages.hitRate.view.chart' | transloco }}</button>
                    <button type="button" class="flex h-9 items-center gap-1 rounded px-2.5 lg:h-7" [class.bg-muted]="view()[g] === 'table'" [attr.aria-pressed]="view()[g] === 'table'" (click)="setView(g, 'table')">
                      <ng-icon name="lucideTable" />{{ 'packages.hitRate.view.table' | transloco }}</button>
                  </div>
                </div>
                @if (view()[g] !== 'table') {
                  <div class="flex flex-col gap-1 px-5 py-4">
                    <ng-container [ngTemplateOutlet]="legend" [ngTemplateOutletContext]="{ keys: ['ordered', 'sampling', 'dropped', 'open'] }" />
                    <div class="text-muted-foreground mt-2 hidden grid-cols-[11rem_1fr_5rem] gap-3 text-xs sm:grid">
                      <span>{{ 'packages.hitRate.col.' + g | transloco }}</span>
                      <span>{{ 'packages.hitRate.offered' | transloco }}</span>
                      <span class="text-right">{{ 'packages.hitRate.hero' | transloco }}</span>
                    </div>
                    <ul class="flex flex-col" role="list">
                      @for (row of (expanded()[g] ? rows : rows.slice(0, firstRows)); track row.key) {
                        @let label = g === 'bySource' ? ('packages.source.' + row.key | transloco) : (row.key ?? '—');
                        <li class="hr-row grid cursor-default grid-cols-[1fr_4rem] items-center gap-x-3 gap-y-1 rounded-md py-2 outline-none focus-visible:ring-2 sm:grid-cols-[11rem_1fr_5rem]"
                            tabindex="0" [attr.aria-label]="rowSummary(label, row)"
                            (pointermove)="showTip($event, label, row)" (pointerleave)="tip.set(null)" (focus)="showTipAt($event, label, row)" (blur)="tip.set(null)">
                          <span class="truncate text-sm font-medium" [title]="label">{{ label }}</span>
                          <span class="text-right text-sm font-semibold tabular-nums sm:order-last">{{ ratio(row.sampling + row.ordered, row.offered) | percent }}</span>
                          <span class="col-span-2 flex items-center gap-2 sm:col-span-1">
                            <span class="hr-bar h-4" [style.width.%]="ratio(row.offered, maxOffered(rows)) * 85">
                              @for (s of outcomeSegments(row); track s.key) {
                                @if (s.n) { <span [style.flex-grow]="s.n" [style.background]="'var(--hr-' + colorOf(s.key) + ')'"></span> }
                              }
                            </span>
                            <span class="text-muted-foreground text-xs tabular-nums">{{ row.offered | number }}</span>
                          </span>
                        </li>
                      }
                    </ul>
                    @if (rows.length > firstRows) {
                      <button type="button" class="text-primary mt-1 self-start text-xs underline-offset-2 hover:underline" (click)="toggleExpanded(g)">
                        {{ (expanded()[g] ? 'packages.hitRate.showLess' : 'packages.hitRate.showAll') | transloco: { count: rows.length } }}
                      </button>
                    }
                  </div>
                } @else {
                  <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                      <thead class="text-muted-foreground text-left text-xs tracking-wide uppercase">
                        <tr class="border-b">
                          <th class="px-5 py-2 font-medium">{{ 'packages.hitRate.col.' + g | transloco }}</th>
                          <th class="px-3 py-2 text-right font-medium">{{ 'packages.hitRate.offered' | transloco }}</th>
                          <th class="px-3 py-2 font-medium">{{ 'packages.hitRate.votesCol' | transloco }}</th>
                          <th class="px-3 py-2 font-medium">{{ 'packages.hitRate.stylesCol' | transloco }}</th>
                          <th class="px-3 py-2 text-right font-medium">{{ 'packages.hitRate.sampling' | transloco }}</th>
                          <th class="px-3 py-2 text-right font-medium">{{ 'packages.hitRate.ordered' | transloco }}</th>
                          <th class="px-3 py-2 text-right font-medium">{{ 'packages.hitRate.legend.dropped' | transloco }}</th>
                        </tr>
                      </thead>
                      <tbody class="divide-y">
                        @for (row of rows; track row.key) {
                          <tr>
                            <td class="px-5 py-2 font-medium">{{ g === 'bySource' ? ('packages.source.' + row.key | transloco) : row.key }}</td>
                            <td class="px-3 py-2 text-right tabular-nums">{{ row.offered }}</td>
                            <td class="px-3 py-2 text-xs whitespace-nowrap tabular-nums">
                              {{ 'packages.hitRate.votes' | transloco: { likes: row.likeVotes, passes: row.passVotes } }}
                              @if (row.likeVotes + row.passVotes) { <span class="text-muted-foreground">({{ ratio(row.likeVotes, row.likeVotes + row.passVotes) | percent }})</span> }
                            </td>
                            <td class="px-3 py-2 text-xs whitespace-nowrap tabular-nums">
                              @if (row.answered) { {{ 'packages.hitRate.split' | transloco: { liked: row.allLiked, mixed: row.mixed, passed: row.allPassed } }} }
                              @else { <span class="text-muted-foreground">{{ 'packages.hitRate.noAnswers' | transloco }}</span> }
                            </td>
                            <td class="px-3 py-2 text-right tabular-nums">{{ row.sampling + row.ordered }} ({{ ratio(row.sampling + row.ordered, row.offered) | percent }})</td>
                            <td class="px-3 py-2 text-right tabular-nums">{{ row.ordered }} ({{ ratio(row.ordered, row.offered) | percent }})</td>
                            <td class="px-3 py-2 text-right tabular-nums">{{ row.dropped }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              </section>
            }
            <p class="text-muted-foreground flex items-start gap-1.5 text-xs"><ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />{{ 'packages.hitRate.rules' | transloco }}</p>
          </div>
        }
      }
    </section>

    <!-- A 100% bar with its legend (counts and shares); the legend carries every value, the bar is the picture -->
    <ng-template #whole let-segs="segs" let-total="total" let-title="title">
      @if (total) {
        <span class="hr-bar h-6 w-full rounded-l-[4px]" role="img" [attr.aria-label]="(title | transloco) + ': ' + wholeSummary(segs, total)"
              (pointermove)="showWholeTip($event, title, segs, total)" (pointerleave)="tip.set(null)">
          @for (s of segs; track s.key) {
            @if (s.n) { <span [style.flex-grow]="s.n" [style.background]="'var(--hr-' + colorOf(s.key) + ')'"></span> }
          }
        </span>
      } @else {
        <span class="text-muted-foreground text-xs">{{ 'packages.hitRate.noAnswers' | transloco }}</span>
      }
      <ul class="grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs sm:grid-cols-4" role="list">
        @for (s of segs; track s.key) {
          <li class="flex items-start gap-1.5">
            <span class="mt-0.5 size-2.5 shrink-0 rounded-[3px]" [style.background]="'var(--hr-' + colorOf(s.key) + ')'"></span>
            <span class="min-w-0">
              <span class="text-muted-foreground block">{{ 'packages.hitRate.legend.' + s.key | transloco }}</span>
              <!-- Count and share kept apart by a dot: side by side "1" and "20%" read as "120%" -->
              <span class="font-semibold tabular-nums">{{ s.n | number }}</span><span class="text-muted-foreground tabular-nums">{{ ' · ' + (ratio(s.n, total) | percent) }}</span>
            </span>
          </li>
        }
      </ul>
    </ng-template>

    <ng-template #legend let-keys="keys">
      <ul class="text-muted-foreground flex flex-wrap gap-x-4 gap-y-1 text-xs" role="list">
        @for (k of keys; track k) {
          <li class="flex items-center gap-1.5"><span class="size-2.5 rounded-[3px]" [style.background]="'var(--hr-' + colorOf(k) + ')'"></span>{{ 'packages.hitRate.legend.' + k | transloco }}</li>
        }
      </ul>
    </ng-template>

    <!-- Hover / focus read-out: values first, names after -->
    @if (tip(); as tp) {
      <div class="bg-popover text-popover-foreground pointer-events-none fixed z-50 min-w-44 rounded-md border px-3 py-2 text-xs shadow-md"
           [style.left.px]="tp.x" [style.top.px]="tp.y" role="tooltip">
        <p class="mb-1 font-medium">{{ tp.title }}</p>
        @for (l of tp.rows; track l.label) {
          <p class="flex items-center gap-2">
            <span class="h-0.5 w-3 shrink-0 rounded" [style.background]="l.color"></span>
            <span class="font-semibold tabular-nums">{{ l.value }}</span>
            <span class="text-muted-foreground">{{ l.label }}</span>
          </p>
        }
      </div>
    }
  `,
})
export class HitRatePage implements OnInit {
  private readonly svc = inject(PackagesService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(TranslocoService);

  readonly groups: Group[] = ['byGarment', 'byClient', 'bySource'];
  readonly firstRows = FIRST_ROWS;
  readonly lookups = signal<StyleLookups | null>(null);
  readonly customer = signal('');
  readonly from = signal('');
  readonly to = signal('');
  readonly rate = signal<HitRate | null>(null);
  readonly loading = signal(false);
  readonly view = signal<Partial<Record<Group, 'chart' | 'table'>>>({});
  readonly expanded = signal<Partial<Record<Group, boolean>>>({});
  readonly tip = signal<Tip | null>(null);

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: (e) => toast.error(styleError(e).message) });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const yearAgo = new Date();
      yearAgo.setUTCFullYear(yearAgo.getUTCFullYear() - 1);
      this.customer.set(params.get('customer') ?? '');
      this.from.set(params.get('from') ?? day(yearAgo));
      this.to.set(params.get('to') ?? day(new Date()));
      this.load();
    });
  }

  setFilter(key: 'customer' | 'from' | 'to', value: string): void {
    void this.router.navigate([], { queryParams: { [key]: value || null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  load(): void {
    this.loading.set(true);
    this.svc.hitRate(this.customer() || null, this.from() || null, this.to() || null).subscribe({
      next: (r) => {
        this.rate.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  setView(g: Group, v: 'chart' | 'table'): void {
    this.view.update((m) => ({ ...m, [g]: v }));
  }

  toggleExpanded(g: Group): void {
    this.expanded.update((m) => ({ ...m, [g]: !m[g] }));
  }

  ratio(part: number, whole: number): number {
    return whole ? part / whole : 0;
  }

  /** Offered -> answered -> liked by someone -> to sampling -> ordered (each also counted on its own, so not strictly nested). */
  funnel(t: HitRateRow): Segment[] {
    return [
      { key: 'offered', n: t.offered },
      { key: 'answered', n: t.answered },
      { key: 'liked', n: t.allLiked + t.mixed },
      { key: 'sampling', n: t.sampling + t.ordered },
      { key: 'ordered', n: t.ordered },
    ];
  }

  answerSegments(r: HitRateRow): Segment[] {
    return [
      { key: 'allLiked', n: r.allLiked },
      { key: 'mixed', n: r.mixed },
      { key: 'allPassed', n: r.allPassed },
      { key: 'noAnswer', n: r.offered - r.answered },
    ];
  }

  /** The outcome set on the package page: one per style, so the pieces add up to the styles offered. */
  outcomeSegments(r: HitRateRow): Segment[] {
    return [
      { key: 'ordered', n: r.ordered },
      { key: 'sampling', n: r.sampling },
      { key: 'dropped', n: r.dropped },
      { key: 'open', n: Math.max(0, r.offered - r.ordered - r.sampling - r.dropped) },
    ];
  }

  /** The colour role of a legend key. */
  colorOf(key: string): string {
    switch (key) {
      case 'allLiked':
      case 'likes':
        return 'good';
      case 'mixed':
        return 'mid';
      case 'allPassed':
      case 'passes':
      case 'dropped':
        return 'bad';
      case 'noAnswer':
        return 'open';
      default:
        return key;
    }
  }

  maxOffered(rows: HitRateRow[]): number {
    return rows.reduce((m, r) => Math.max(m, r.offered), 0);
  }

  rowSummary(label: string, row: HitRateRow): string {
    return `${label}: ${this.tipRows(row)
      .map((l) => `${l.label} ${l.value}`)
      .join(', ')}`;
  }

  wholeSummary(segs: Segment[], total: number): string {
    return segs.map((s) => `${this.i18n.translate('packages.hitRate.legend.' + s.key)} ${s.n} (${this.pct(s.n, total)})`).join(', ');
  }

  showTip(e: PointerEvent, title: string, row: HitRateRow): void {
    this.tip.set(this.place(e.clientX, e.clientY, { title, rows: this.tipRows(row) }));
  }

  showTipAt(e: FocusEvent, title: string, row: HitRateRow): void {
    const r = (e.target as HTMLElement).getBoundingClientRect();
    this.tip.set(this.place(r.left + Math.min(r.width, 240), r.top, { title, rows: this.tipRows(row) }));
  }

  showWholeTip(e: PointerEvent, title: string, segs: Segment[], total: number): void {
    const rows = segs.map((s) => ({ label: this.i18n.translate('packages.hitRate.legend.' + s.key), value: `${s.n} · ${this.pct(s.n, total)}`, color: `var(--hr-${this.colorOf(s.key)})` }));
    this.tip.set(this.place(e.clientX, e.clientY, { title: this.i18n.translate(title), rows }));
  }

  private tipRows(row: HitRateRow): Tip['rows'] {
    const t = (k: string) => this.i18n.translate('packages.hitRate.' + k);
    return [
      { label: t('offered'), value: `${row.offered}`, color: 'var(--hr-mid)' },
      { label: t('legend.ordered'), value: `${row.ordered} · ${this.pct(row.ordered, row.offered)}`, color: 'var(--hr-ordered)' },
      { label: t('legend.sampling'), value: `${row.sampling} · ${this.pct(row.sampling, row.offered)}`, color: 'var(--hr-sampling)' },
      { label: t('legend.dropped'), value: `${row.dropped}`, color: 'var(--hr-bad)' },
      { label: t('answered'), value: `${row.answered} · ${this.pct(row.answered, row.offered)}`, color: 'var(--hr-good)' },
      { label: t('votesTitle'), value: this.i18n.translate('packages.hitRate.votes', { likes: row.likeVotes, passes: row.passVotes }), color: 'var(--hr-mid)' },
    ];
  }

  private pct(part: number, whole: number): string {
    return `${Math.round(this.ratio(part, whole) * 100)}%`;
  }

  /** Beside the pointer, kept inside the window. */
  private place(x: number, y: number, tip: Omit<Tip, 'x' | 'y'>): Tip {
    const w = 220;
    const h = 30 + tip.rows.length * 18;
    return { ...tip, x: Math.min(x + 14, window.innerWidth - w - 8), y: Math.min(y + 14, window.innerHeight - h - 8) };
  }
}
