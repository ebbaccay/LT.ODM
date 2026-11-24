import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalculator, lucideInfo } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { styleError } from '../style-library/style-library.service';
import { CostingService, StylePriceSummary, StylePrices } from './costing.service';
import { fobRange } from './price-summary';

/** A piece of the FOB: a colour role and the amount. */
interface Piece {
  key: 'fabric' | 'trims' | 'making' | 'other' | 'net';
  amount: number;
}

/**
 * Style page > Prices by IO (roadmap 3.16): what this garment has cost across its IOs at a glance (latest FOB, range, average weighted by
 * quantity), where the FOB goes (Admin and Costing only), then the IOs. A style has no single price: each IO has its own.
 */
@Component({
  selector: 'app-style-prices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, PercentPipe, NgIcon, HlmBadgeImports, HlmCardImports, HlmSkeletonImports, HlmTooltipImports, TranslocoPipe],
  providers: [DecimalPipe, provideIcons({ lucideCalculator, lucideInfo })],
  styles: `
    :host {
      --ip-fabric: #2a78d6;
      --ip-trims: #eb6834;
      --ip-making: #1baf7a;
      --ip-other: #eda100;
      --ip-net: #c3c2b7;
    }
    :host-context(.dark) {
      --ip-fabric: #3987e5;
      --ip-trims: #d95926;
      --ip-making: #199e70;
      --ip-other: #c98500;
      --ip-net: #5a5955;
    }
  `,
  template: `
    @if (loading() && !prices()) {
      <div hlmSkeleton class="h-32 w-full"></div>
    } @else if (prices(); as p) {
      @if (!p.ios.length) {
        <section hlmCard class="items-center gap-2 px-5 py-12 text-center">
          <ng-icon name="lucideCalculator" class="text-muted-foreground text-4xl" />
          <p class="font-medium">{{ 'ioPrices.none' | transloco }}</p>
          <p class="text-muted-foreground max-w-md text-sm">{{ 'ioPrices.noneHint' | transloco }}</p>
        </section>
      } @else {
        <div class="flex flex-col gap-4">
          @for (s of p.summaries; track s.currencyCode) {
            <div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
              <section hlmCard class="gap-1 p-4">
                <span class="text-muted-foreground text-xs">{{ 'ioPrices.latestFob' | transloco }}</span>
                <span class="text-3xl font-semibold tracking-tight">{{ s.latestFob | number: '1.2-2' }} <span class="text-muted-foreground text-sm font-normal">{{ s.currencyCode }}</span></span>
                <span class="text-muted-foreground text-xs">{{ s.latestCostDate | date: 'mediumDate' }}</span>
              </section>
              <section hlmCard class="gap-1 p-4">
                <span class="text-muted-foreground text-xs">{{ 'ioPrices.range' | transloco }}</span>
                <span class="text-xl font-semibold">{{ rangeText(s) }}</span>
                <span class="text-muted-foreground text-xs">{{ s.currencyCode }}</span>
              </section>
              <section hlmCard class="gap-1 p-4">
                <span class="text-muted-foreground text-xs">{{ 'ioPrices.average' | transloco }}</span>
                <span class="text-xl font-semibold">{{ s.avgFob | number: '1.2-2' }}</span>
                <span class="text-muted-foreground text-xs">{{ 'ioPrices.weighted' | transloco }}</span>
              </section>
              <section hlmCard class="gap-1 p-4">
                <span class="text-muted-foreground text-xs">{{ 'ioPrices.ios' | transloco }}</span>
                <span class="text-xl font-semibold">{{ s.ios | number }}</span>
                <span class="text-muted-foreground text-xs">{{ 'ioPrices.pieces' | transloco: { qty: (s.totalQty | number) } }}</span>
              </section>
            </div>

            <!-- Where the FOB goes (Admin and Costing) -->
            @if (p.showCost && pieces(s); as parts) {
              <section hlmCard class="gap-3 p-4">
                <div>
                  <h2 class="text-sm font-semibold">{{ 'ioPrices.whereTitle' | transloco }}</h2>
                  <p class="text-muted-foreground text-xs">{{ 'ioPrices.whereHint' | transloco }}</p>
                </div>
                <div class="flex h-6 w-full gap-[2px] overflow-hidden rounded-[4px]" role="img" [attr.aria-label]="'ioPrices.whereTitle' | transloco">
                  @for (x of parts; track x.key) {
                    @if (x.amount > 0) { <span class="h-full" [style.flex-grow]="x.amount" [style.background]="'var(--ip-' + x.key + ')'"></span> }
                  }
                </div>
                <ul class="grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs sm:grid-cols-5" role="list">
                  @for (x of parts; track x.key) {
                    <li class="flex items-start gap-1.5">
                      <span class="mt-0.5 size-2.5 shrink-0 rounded-[3px]" [style.background]="'var(--ip-' + x.key + ')'"></span>
                      <span class="min-w-0">
                        <span class="text-muted-foreground block">{{ 'ioPrices.group.' + x.key | transloco }}</span>
                        <span class="font-semibold tabular-nums">{{ x.amount | number: '1.2-2' }}</span><span class="text-muted-foreground tabular-nums">{{ ' · ' + (x.amount / s.avgFob | percent) }}</span>
                      </span>
                    </li>
                  }
                </ul>
              </section>
            }
          }

          <section hlmCard class="gap-0 py-0">
            <h2 class="border-b px-5 py-3 font-semibold">{{ 'ioPrices.tableTitle' | transloco: { count: p.ios.length } }}</h2>
            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead class="text-muted-foreground text-left text-xs tracking-wide uppercase">
                  <tr class="border-b">
                    <th class="px-5 py-2 font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.io' | transloco">{{ 'ioPrices.col.io' | transloco }}</span></th>
                    <th class="px-3 py-2 font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.stage' | transloco">{{ 'ioPrices.col.stage' | transloco }}</span></th>
                    <th class="px-3 py-2 font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.costed' | transloco">{{ 'ioPrices.col.costed' | transloco }}</span></th>
                    <th class="px-3 py-2 font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.plant' | transloco">{{ 'ioPrices.col.plant' | transloco }}</span></th>
                    <th class="px-3 py-2 font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.delivery' | transloco">{{ 'ioPrices.col.delivery' | transloco }}</span></th>
                    <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.ioQty' | transloco">{{ 'ioPrices.col.qty' | transloco }}</span></th>
                    <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.fob' | transloco">{{ 'ioPrices.col.fob' | transloco }}</span></th>
                    @if (p.showCost) {
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.fabric' | transloco">{{ 'ioPrices.group.fabric' | transloco }}</span></th>
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.trims' | transloco">{{ 'ioPrices.group.trims' | transloco }}</span></th>
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.making' | transloco">{{ 'ioPrices.group.making' | transloco }}</span></th>
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.other' | transloco">{{ 'ioPrices.group.other' | transloco }}</span></th>
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.ioNet' | transloco">{{ 'ioPrices.group.net' | transloco }}</span></th>
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.percent' | transloco">{{ 'ioPrices.col.percent' | transloco }}</span></th>
                    }
                  </tr>
                </thead>
                <tbody class="divide-y">
                  @for (i of p.ios; track i.ioNo) {
                    <tr>
                      <td class="px-5 py-2 font-medium whitespace-nowrap">{{ i.ioNo }}</td>
                      <td class="px-3 py-2 whitespace-nowrap">
                        <span hlmBadge [variant]="i.costType === 'FCOSTING' ? 'default' : 'secondary'">{{ 'ioPrices.stage.' + i.costType | transloco }}</span>
                        @if (i.isSmall) { <span hlmBadge variant="outline" class="ml-1" [title]="'ioPrices.smallHint' | transloco">{{ 'ioPrices.small' | transloco }}</span> }
                        @if (i.finalPending && p.showCost) { <span hlmBadge variant="outline" class="ml-1" [title]="'ioPrices.finalPendingHint' | transloco">{{ 'ioPrices.finalPending' | transloco }}</span> }
                      </td>
                      <td class="px-3 py-2 whitespace-nowrap">{{ i.costDate | date: 'mediumDate' }}</td>
                      <td class="px-3 py-2" [title]="i.plantCode ?? ''">{{ i.plantName ?? i.plantCode }}</td>
                      <td class="px-3 py-2 whitespace-nowrap">{{ i.planDeliveryDate | date: 'mediumDate' }}</td>
                      <td class="px-3 py-2 text-right tabular-nums">{{ i.orderQty | number }}</td>
                      <td class="px-3 py-2 text-right font-medium whitespace-nowrap tabular-nums">{{ i.fob | number: '1.5-5' }} <span class="text-muted-foreground text-xs font-normal">{{ i.currencyCode }}</span></td>
                      @if (p.showCost) {
                        <td class="px-3 py-2 text-right tabular-nums">{{ i.fabric | number: '1.2-2' }}</td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ i.trims | number: '1.2-2' }}</td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ i.making | number: '1.2-2' }}</td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ i.other | number: '1.2-2' }}</td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ i.netBalance | number: '1.2-2' }}</td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ i.percToFob | number: '1.1-1' }}%</td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>

          <p class="text-muted-foreground flex items-start gap-1.5 text-xs">
            <ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />
            <span>{{ 'ioPrices.rules' | transloco: { qty: p.smallOrderQty } }}@if (p.unfinished) { {{ ' ' + ('ioPrices.unfinished' | transloco: { count: p.unfinished }) }} }</span>
          </p>
        </div>
      }
    }
  `,
})
export class StylePricesPanel {
  private readonly svc = inject(CostingService);
  private readonly number = inject(DecimalPipe);

  readonly styleId = input.required<number>();
  readonly prices = signal<StylePrices | null>(null);
  readonly loading = signal(true);

  constructor() {
    effect(() => {
      const id = this.styleId();
      this.loading.set(true);
      this.svc.style(id).subscribe({
        next: (p) => {
          this.prices.set(p);
          this.loading.set(false);
        },
        error: (e) => {
          this.loading.set(false);
          toast.error(styleError(e).message);
        },
      });
    });
  }

  /** One amount when every IO is at the same price. */
  rangeText(s: StylePriceSummary): string {
    return fobRange(this.number, s);
  }

  pieces(s: StylePriceSummary): Piece[] {
    return [
      { key: 'fabric', amount: s.avgFabric ?? 0 },
      { key: 'trims', amount: s.avgTrims ?? 0 },
      { key: 'making', amount: s.avgMaking ?? 0 },
      { key: 'other', amount: s.avgOther ?? 0 },
      { key: 'net', amount: Math.max(0, s.avgNet ?? 0) },
    ];
  }
}
