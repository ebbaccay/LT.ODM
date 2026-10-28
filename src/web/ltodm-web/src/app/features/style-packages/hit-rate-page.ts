import { DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChartColumn, lucideInfo } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { HitRate, PackagesService } from './packages.service';

type Group = 'byGarment' | 'byClient' | 'bySource';

/** yyyy-MM-dd of a date (UTC). */
function day(d: Date): string {
  return d.toISOString().slice(0, 10);
}

/**
 * Style Packages > Hit rate (roadmap 5.17): what came of the styles offered in packages sent to clients - answered through
 * share links (liked / passed) and the outcome set on the package page (sampling, ordered, dropped) - in total and by
 * garment, client and where the style was added from (does What to offer work?). Filters: customer's brand and the dates
 * the packages were sent (default: the last 12 months), kept in the address.
 */
@Component({
  selector: 'app-hit-rate-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, PercentPipe, NgIcon, HlmCardImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideChartColumn, lucideInfo })],
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
        <div hlmSkeleton class="h-28 w-full"></div>
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
          <!-- Totals -->
          <div class="grid grid-cols-2 gap-3 md:grid-cols-5">
            @for (tile of [
              { label: 'packages.hitRate.offered', value: (t.offered | number), sub: ('packages.hitRate.inPackages' | transloco: { count: t.packages }) },
              { label: 'packages.hitRate.answered', value: (ratio(t.answered, t.offered) | percent), sub: (t.answered | number) + ' / ' + (t.offered | number) },
              { label: 'packages.hitRate.positive', value: (ratio(t.likeVotes, t.likeVotes + t.passVotes) | percent), sub: ('packages.hitRate.votes' | transloco: { likes: t.likeVotes, passes: t.passVotes }) },
              { label: 'packages.hitRate.sampling', value: (ratio(t.sampling + t.ordered, t.offered) | percent), sub: ('packages.hitRate.samplingSub' | transloco: { count: t.sampling + t.ordered }) },
              { label: 'packages.hitRate.ordered', value: (ratio(t.ordered, t.offered) | percent), sub: ('packages.hitRate.orderedSub' | transloco: { count: t.ordered }) }
            ]; track tile.label) {
              <section hlmCard class="gap-1 p-4">
                <span class="text-muted-foreground text-xs">{{ tile.label | transloco }}</span>
                <span class="text-2xl font-semibold tabular-nums">{{ tile.value }}</span>
                <span class="text-muted-foreground text-xs">{{ tile.sub }}</span>
              </section>
            }
          </div>

          @for (g of groups; track g) {
            <section hlmCard class="gap-0 py-0">
              <h2 class="border-b px-5 py-3 font-semibold">{{ 'packages.hitRate.' + g | transloco }}</h2>
              <div class="overflow-x-auto">
                <table class="w-full text-sm">
                  <thead class="text-muted-foreground text-left text-xs tracking-wide uppercase">
                    <tr class="border-b">
                      <th class="px-5 py-2 font-medium">{{ 'packages.hitRate.col.' + g | transloco }}</th>
                      <th class="px-3 py-2 text-right font-medium">{{ 'packages.hitRate.offered' | transloco }}</th>
                      <th class="px-3 py-2 font-medium">{{ 'packages.hitRate.votesCol' | transloco }}</th>
                      <th class="px-3 py-2 font-medium">{{ 'packages.hitRate.stylesCol' | transloco }}</th>
                      <th class="px-3 py-2 text-right font-medium">{{ 'packages.hitRate.sampling' | transloco }}</th>
                      <th class="px-3 py-2 font-medium">{{ 'packages.hitRate.ordered' | transloco }}</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y">
                    @for (row of r[g]; track row.key) {
                      <tr>
                        <td class="px-5 py-2 font-medium">{{ g === 'bySource' ? ('packages.source.' + row.key | transloco) : row.key }}</td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ row.offered }}</td>
                        <td class="px-3 py-2 text-xs whitespace-nowrap tabular-nums">
                          {{ 'packages.hitRate.votes' | transloco: { likes: row.likeVotes, passes: row.passVotes } }}
                          @if (row.likeVotes + row.passVotes) { <span class="text-muted-foreground">({{ ratio(row.likeVotes, row.likeVotes + row.passVotes) | percent }})</span> }
                        </td>
                        <td class="px-3 py-2">
                          <!-- Each answered style: all liked / mixed / all passed -->
                          @if (row.answered) {
                            <span class="flex items-center gap-2">
                              <span class="bg-muted flex h-2.5 w-24 overflow-hidden rounded-full" role="img"
                                    [attr.aria-label]="'packages.hitRate.split' | transloco: { liked: row.allLiked, mixed: row.mixed, passed: row.allPassed }">
                                <span class="h-full bg-emerald-500" [style.width.%]="ratio(row.allLiked, row.answered) * 100"></span>
                                <span class="h-full bg-amber-400" [style.width.%]="ratio(row.mixed, row.answered) * 100"></span>
                                <span class="h-full bg-red-400" [style.width.%]="ratio(row.allPassed, row.answered) * 100"></span>
                              </span>
                              <span class="text-xs whitespace-nowrap tabular-nums">{{ 'packages.hitRate.split' | transloco: { liked: row.allLiked, mixed: row.mixed, passed: row.allPassed } }}</span>
                            </span>
                          } @else {
                            <span class="text-muted-foreground text-xs">{{ 'packages.hitRate.noAnswers' | transloco }}</span>
                          }
                        </td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ row.sampling + row.ordered }}</td>
                        <td class="px-3 py-2">
                          <span class="flex items-center gap-2">
                            <span class="bg-muted h-2.5 w-24 overflow-hidden rounded-full" role="img" [attr.aria-label]="ratio(row.ordered, row.offered) | percent">
                              <span class="block h-full rounded-full bg-emerald-500" [style.width.%]="ratio(row.ordered, row.offered) * 100"></span>
                            </span>
                            <span class="text-xs tabular-nums">{{ ratio(row.ordered, row.offered) | percent }} ({{ row.ordered }})</span>
                          </span>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </section>
          }
          <p class="text-muted-foreground flex items-start gap-1.5 text-xs"><ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />{{ 'packages.hitRate.rules' | transloco }}</p>
        }
      }
    </section>
  `,
})
export class HitRatePage implements OnInit {
  private readonly svc = inject(PackagesService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly groups: Group[] = ['byGarment', 'byClient', 'bySource'];
  readonly lookups = signal<StyleLookups | null>(null);
  readonly customer = signal('');
  readonly from = signal('');
  readonly to = signal('');
  readonly rate = signal<HitRate | null>(null);
  readonly loading = signal(false);

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

  ratio(part: number, whole: number): number {
    return whole ? part / whole : 0;
  }
}
