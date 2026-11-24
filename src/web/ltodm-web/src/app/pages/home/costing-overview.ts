import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalculator, lucideCircleAlert, lucideCircleCheck, lucidePackage, lucideTrendingDown } from '@ng-icons/lucide';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { toneBadge } from '@tms/shared/ui/tones';
import { CostingOverview as Overview, CostingService } from '../../features/costing/costing.service';

/**
 * Library overview > Costing (Admin and Costing): whether the price data can be trusted (data health), where net balance stands by garment and which IOs
 * fall below their style's, and the package quotes that need attention. Loaded when scrolled to.
 */
@Component({
  selector: 'app-costing-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, RouterLink, NgIcon, HlmCardImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideCalculator, lucideCircleAlert, lucideCircleCheck, lucidePackage, lucideTrendingDown })],
  styles: `:host { display: block; min-width: 0; }`,
  template: `
    <div class="flex flex-col gap-3">
      <h2 class="flex items-center gap-2 text-lg font-semibold"><ng-icon name="lucideCalculator" />{{ 'home.costing.overviewTitle' | transloco }}</h2>
      @if (data(); as d) {
        <div class="grid grid-cols-1 gap-4 xl:grid-cols-3">
          <!-- Data health -->
          <section hlmCard class="min-w-0">
            <div hlmCardHeader>
              <h3 hlmCardTitle>{{ 'home.costing.healthTitle' | transloco }}</h3>
              <p hlmCardDescription>
                @if (d.health.lastImportUtc) {
                  {{ 'home.costing.lastLoad' | transloco: { file: d.health.lastImportFile, user: d.health.lastImportBy, date: (d.health.lastImportUtc | date: 'medium') } }}
                } @else {
                  {{ 'home.costing.neverLoaded' | transloco }}
                }
              </p>
            </div>
            <div hlmCardContent class="flex flex-col gap-3">
              <div>
                <p class="flex items-baseline justify-between gap-2 text-sm">
                  <span>{{ 'home.costing.coverage' | transloco }}</span>
                  <span class="font-semibold tabular-nums">{{ d.health.stylesWithPrices | number }} / {{ d.health.activeStyles | number }}</span>
                </p>
                <span class="bg-muted mt-1 block h-1.5 overflow-hidden rounded-full"><span class="bg-primary block h-full rounded-full" [style.width.%]="coverage(d)"></span></span>
              </div>
              <ul class="flex flex-col gap-1.5 text-sm" role="list">
                @for (row of healthRows(d); track row.key) {
                  <li class="flex items-center justify-between gap-2">
                    <span class="flex items-center gap-1.5">
                      <ng-icon [name]="row.count ? 'lucideCircleAlert' : 'lucideCircleCheck'" [class]="row.count ? 'text-amber-600 dark:text-amber-400' : 'text-emerald-600 dark:text-emerald-400'" />
                      {{ 'home.costing.health.' + row.key | transloco }}
                    </span>
                    <span class="tabular-nums" [title]="row.title ?? ''">{{ row.count | number }}</span>
                  </li>
                }
              </ul>
              <a routerLink="/costing" class="text-primary text-xs underline-offset-2 hover:underline">{{ 'home.costing.openCosting' | transloco }}</a>
            </div>
          </section>

          <!-- Net balance watch -->
          <section hlmCard class="min-w-0">
            <div hlmCardHeader>
              <h3 hlmCardTitle>{{ 'home.costing.netTitle' | transloco }}</h3>
              <p hlmCardDescription>{{ 'home.costing.netHint' | transloco }}</p>
            </div>
            <div hlmCardContent class="flex flex-col gap-3">
              @if (!d.netByGarment.length) {
                <p class="text-muted-foreground text-sm">{{ 'home.costing.netNone' | transloco }}</p>
              } @else {
                <table class="w-full text-sm">
                  <thead class="text-muted-foreground text-left text-xs">
                    <tr><th class="py-1 font-medium">{{ 'home.costing.garment' | transloco }}</th><th class="py-1 text-right font-medium">{{ 'home.costing.pieces' | transloco }}</th><th class="py-1 text-right font-medium">{{ 'home.costing.netPct' | transloco }}</th></tr>
                  </thead>
                  <tbody>
                    @for (g of d.netByGarment; track g.category + g.currencyCode) {
                      <tr class="border-t">
                        <td class="py-1.5">{{ g.category }}</td>
                        <td class="py-1.5 text-right tabular-nums">{{ g.qty | number }}</td>
                        <td class="py-1.5 text-right font-semibold tabular-nums">{{ g.netPct | number: '1.1-1' }}%</td>
                      </tr>
                    }
                  </tbody>
                </table>
              }
              <div class="border-t pt-3">
                <p class="flex items-center gap-1.5 text-sm font-medium"><ng-icon name="lucideTrendingDown" />{{ 'home.costing.dropsTitle' | transloco: { points: d.netGap } }}</p>
                @if (!d.netDrops.length) {
                  <p class="text-muted-foreground text-xs">{{ 'home.costing.dropsNone' | transloco }}</p>
                } @else {
                  <ul class="mt-1 flex flex-col gap-1 text-xs" role="list">
                    @for (x of d.netDrops; track x.ioNo) {
                      <li class="flex justify-between gap-2">
                        <a [routerLink]="['/styles', x.styleId]" [queryParams]="{ tab: 'prices' }" class="text-primary underline-offset-2 hover:underline">{{ x.styleNo }} · {{ x.ioNo }}</a>
                        <span class="tabular-nums">{{ x.ioPct | number: '1.1-1' }}% <span class="text-muted-foreground">({{ 'home.costing.styleUsual' | transloco: { pct: (x.stylePct | number: '1.1-1') } }})</span></span>
                      </li>
                    }
                  </ul>
                }
              </div>
            </div>
          </section>

          <!-- Quotes needing attention -->
          <section hlmCard class="min-w-0">
            <div hlmCardHeader>
              <h3 hlmCardTitle>{{ 'home.costing.quotesTitle' | transloco }}</h3>
              <p hlmCardDescription>{{ 'home.costing.quotesHint' | transloco: { packages: d.quotes.packages } }}</p>
            </div>
            <div hlmCardContent class="flex flex-col gap-3">
              <div class="grid grid-cols-3 gap-2 text-center">
                @for (t of quoteTiles(d); track t.key) {
                  <div class="rounded-md border px-2 py-2">
                    <p class="text-xl font-semibold">{{ t.count | number }}</p>
                    <p class="text-muted-foreground text-[11px] leading-tight">{{ 'home.costing.quoteKind.' + t.key | transloco }}</p>
                  </div>
                }
              </div>
              @if (d.quoteItems.length) {
                <ul class="divide-y text-xs" role="list">
                  @for (q of d.quoteItems; track q.packageId + q.styleNo) {
                    <li class="flex items-center justify-between gap-2 py-1.5">
                      <a [routerLink]="['/packages', q.packageId]" class="text-primary min-w-0 truncate underline-offset-2 hover:underline">{{ q.packageName }} · {{ q.styleNo }}</a>
                      <span class="shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge(q.kind === 'OutOfBand' ? 'amber' : q.kind === 'Estimate' ? 'violet' : 'neutral')">
                        {{ 'home.costing.quoteKind.' + q.kind | transloco }}
                      </span>
                    </li>
                  }
                </ul>
              } @else {
                <p class="text-muted-foreground text-sm">{{ 'home.costing.quotesNone' | transloco }}</p>
              }
            </div>
          </section>
        </div>
      } @else {
        <div class="grid grid-cols-1 gap-4 xl:grid-cols-3">@for (i of [1, 2, 3]; track i) { <hlm-skeleton class="h-56 w-full" /> }</div>
      }
    </div>
  `,
})
export class CostingOverview {
  private readonly svc = inject(CostingService);
  protected readonly toneBadge = toneBadge;
  /** Changes when the dashboard is refreshed. */
  readonly reload = input(0);
  readonly data = signal<Overview | null>(null);

  constructor() {
    effect(() => {
      this.reload();
      this.svc.overview().subscribe({ next: (d) => this.data.set(d), error: () => this.data.set(null) });
    });
  }

  coverage(d: Overview): number {
    return d.health.activeStyles ? Math.min(100, (d.health.stylesWithPrices / d.health.activeStyles) * 100) : 0;
  }

  healthRows(d: Overview): { key: string; count: number; title?: string | null }[] {
    return [
      { key: 'unmatched', count: d.health.lastImportUnmatched },
      { key: 'unfinished', count: d.health.iosUnfinished },
      { key: 'finalPending', count: d.health.finalPending },
      { key: 'smallIos', count: d.health.smallIos },
      { key: 'plantsWithoutRate', count: d.health.plantsWithoutRate, title: d.health.plantsWithoutRateList },
    ];
  }

  quoteTiles(d: Overview): { key: string; count: number }[] {
    return [
      { key: 'NoQuote', count: d.quotes.noQuote },
      { key: 'OutOfBand', count: d.quotes.outOfBand },
      { key: 'Estimate', count: d.quotes.estimated },
    ];
  }
}
