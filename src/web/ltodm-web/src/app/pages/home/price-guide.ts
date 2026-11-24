import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTag } from '@ng-icons/lucide';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { CostingService, PriceGuideRow } from '../../features/costing/costing.service';

/**
 * Designer dashboard > Price guide by garment: the low, typical and high latest FOB of the styles with IO prices, and their typical SMV, so a design
 * can start from what that garment usually costs. FOB only (every price reader).
 */
@Component({
  selector: 'app-price-guide',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, RouterLink, NgIcon, HlmCardImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideTag })],
  styles: `
    :host { display: block; min-width: 0; }
    .range { position: relative; height: 6px; border-radius: 999px; background: var(--muted); }
    .range > span { position: absolute; top: 0; bottom: 0; border-radius: 999px; background: color-mix(in oklab, var(--primary) 45%, transparent); }
    .range > i { position: absolute; top: -3px; width: 4px; height: 12px; border-radius: 2px; background: var(--primary); transform: translateX(-2px); }
  `,
  template: `
    <section hlmCard class="h-full min-w-0">
      <div hlmCardHeader>
        <h2 hlmCardTitle class="flex items-center gap-2"><ng-icon name="lucideTag" class="text-dash-2" />{{ 'home.costing.guideTitle' | transloco }}</h2>
        <p hlmCardDescription>{{ 'home.costing.guideHint' | transloco }}</p>
      </div>
      <div hlmCardContent>
        @if (rows() === null) {
          @for (i of [1, 2, 3]; track i) { <hlm-skeleton class="mb-3 h-8 w-full" /> }
        } @else if (!rows()!.length) {
          <p class="text-muted-foreground text-sm">{{ 'home.costing.guideNone' | transloco }}</p>
        } @else {
          <ul class="flex flex-col gap-3" role="list">
            @for (r of rows(); track r.category + r.currencyCode) {
              <li class="grid grid-cols-[minmax(0,8rem)_1fr] items-center gap-x-3 gap-y-1 text-sm sm:grid-cols-[minmax(0,9rem)_1fr_auto]">
                <span class="min-w-0">
                  <span class="block truncate font-medium">{{ r.categoryName }}</span>
                  <span class="text-muted-foreground text-xs">{{ 'home.costing.styles' | transloco: { count: r.styles } }}</span>
                </span>
                <span class="flex min-w-0 flex-col gap-1">
                  <span class="range" role="img" [attr.aria-label]="'home.costing.guideRange' | transloco: { low: (r.lowFob | number: '1.2-2'), high: (r.highFob | number: '1.2-2'), typical: (r.typicalFob | number: '1.2-2') }">
                    <span [style.left.%]="pos(r.lowFob)" [style.width.%]="pos(r.highFob) - pos(r.lowFob)"></span>
                    <i [style.left.%]="pos(r.typicalFob)"></i>
                  </span>
                  <span class="text-muted-foreground text-xs tabular-nums">{{ r.lowFob | number: '1.2-2' }} – {{ r.highFob | number: '1.2-2' }} {{ r.currencyCode }}</span>
                </span>
                <span class="col-span-2 text-xs sm:col-span-1 sm:text-right">
                  <span class="font-semibold tabular-nums">{{ 'home.costing.typical' | transloco: { fob: (r.typicalFob | number: '1.2-2'), currency: r.currencyCode } }}</span>
                  @if (r.typicalSmv) { <span class="text-muted-foreground block tabular-nums">{{ 'home.costing.smv' | transloco: { smv: (r.typicalSmv | number: '1.1-1') } }}</span> }
                </span>
              </li>
            }
          </ul>
          <a routerLink="/costing" class="text-primary mt-4 inline-block text-xs underline-offset-2 hover:underline">{{ 'home.costing.openCosting' | transloco }}</a>
        }
      </div>
    </section>
  `,
})
export class PriceGuide {
  private readonly svc = inject(CostingService);
  /** Changes when the dashboard is refreshed. */
  readonly reload = input(0);
  readonly rows = signal<PriceGuideRow[] | null>(null);
  private readonly max = computed(() => Math.max(1, ...(this.rows() ?? []).map((r) => r.highFob)) * 1.05);

  constructor() {
    effect(() => {
      this.reload();
      this.svc.guide().subscribe({ next: (r) => this.rows.set(r), error: () => this.rows.set([]) });
    });
  }

  /** Where an amount sits on the shared scale (0 to the highest FOB). */
  pos(v: number): number {
    return (v / this.max()) * 100;
  }
}
