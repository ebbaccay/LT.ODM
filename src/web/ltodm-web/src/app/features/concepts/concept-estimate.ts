import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalculator, lucideCircleAlert, lucideCircleCheck } from '@ng-icons/lucide';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { CostingService, Estimate } from '../costing/costing.service';
import { estimateBasisText, parseTargetPrice } from '../costing/price-summary';

/**
 * Concept page > Estimated cost (Admin, Merchandiser, Costing): about what a garment of the concept's category costs for its client, from similar
 * priced styles and the plant's cost per minute, against the target price. Admin and Costing also see the parts and what would close a gap.
 */
@Component({
  selector: 'app-concept-estimate',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, NgIcon, HlmCardImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideCalculator, lucideCircleAlert, lucideCircleCheck })],
  template: `
    <section hlmCard class="gap-3 p-5">
      <div>
        <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideCalculator" />{{ 'ioPrices.estimate.title' | transloco }}</h2>
        <p class="text-muted-foreground text-xs">{{ 'ioPrices.estimate.hint' | transloco }}</p>
      </div>
      @if (!category()) {
        <p class="text-muted-foreground text-sm">{{ 'ioPrices.estimate.noCategory' | transloco }}</p>
      } @else if (loading()) {
        <div hlmSkeleton class="h-16 w-full"></div>
      } @else if (estimate(); as e) {
        <p class="flex flex-wrap items-baseline gap-x-3 gap-y-1">
          <span class="text-2xl font-semibold tracking-tight">{{ 'ioPrices.estimate.fob' | transloco: { fob: (e.estimatedFob | number: '1.2-2'), currency: e.currencyCode } }}</span>
        </p>
        @if (verdict(); as v) {
          <p class="flex items-start gap-1.5 text-sm" [class]="v.kind === 'over' ? 'text-amber-800 dark:text-amber-300' : v.kind === 'noCompare' ? 'text-muted-foreground' : 'text-emerald-700 dark:text-emerald-400'">
            <ng-icon [name]="v.kind === 'over' ? 'lucideCircleAlert' : 'lucideCircleCheck'" class="mt-0.5 shrink-0" />
            <span>{{ 'ioPrices.estimate.' + v.kind | transloco: { target: targetPrice(), amount: (v.amount | number: '1.2-2') } }}
              @if (v.kind === 'over' && lever(); as l) { {{ ' ' + ('ioPrices.estimate.lever' | transloco: { minutes: (l.minutes | number: '1.1-1'), cpm: (l.cpm | number: '1.2-2'), amount: (l.costs | number: '1.2-2') }) }} }
            </span>
          </p>
        }
        @if (e.making !== null) {
          <div class="flex flex-col gap-1">
            <p class="text-muted-foreground text-xs">{{ 'ioPrices.estimate.partsTitle' | transloco }}</p>
            <ul class="grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-5" role="list">
              @for (x of parts(); track x.key) {
                <li class="flex flex-col">
                  <span class="text-muted-foreground">{{ 'ioPrices.estimate.' + x.key | transloco }}</span>
                  <span class="font-semibold tabular-nums">{{ x.amount | number: '1.2-2' }}</span>
                </li>
              }
            </ul>
          </div>
        }
        <p class="text-muted-foreground text-xs">{{ basis() }}</p>
      } @else {
        <p class="text-muted-foreground text-sm">{{ 'ioPrices.estimate.none' | transloco }}</p>
      }
    </section>
  `,
})
export class ConceptEstimate {
  private readonly svc = inject(CostingService);
  private readonly i18n = inject(TranslocoService);

  /** The concept's product category code (style.fn_GarmentCategory codes). */
  readonly category = input<string | null>(null);
  readonly categoryName = input<string | null>(null);
  readonly customerCode = input<string | null>(null);
  readonly targetPrice = input<string | null>(null);

  readonly estimate = signal<Estimate | null>(null);
  readonly loading = signal(false);

  constructor() {
    effect(() => {
      const category = this.category();
      const customer = this.customerCode();
      this.estimate.set(null);
      if (!category) return;
      this.loading.set(true);
      this.svc.estimate(category, customer).subscribe({
        next: (e) => {
          this.estimate.set(e);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
    });
  }

  readonly basis = computed(() => {
    const e = this.estimate();
    // The parts are listed above: the line names only the styles it comes from.
    return e ? estimateBasisText(this.i18n, { ...e, category: this.categoryName() ?? e.category, making: null }) : '';
  });

  /** Within, below (room) or over the target; not compared when the target names another currency. */
  readonly verdict = computed(() => {
    const e = this.estimate();
    const t = parseTargetPrice(this.targetPrice());
    if (!e || !t) return null;
    if (t.currency && t.currency !== e.currencyCode) return { kind: 'noCompare' as const, amount: 0 };
    const fob = Math.round(e.estimatedFob * 100) / 100;
    if (fob > t.high) return { kind: 'over' as const, amount: fob - t.high };
    if (fob < t.low) return { kind: 'under' as const, amount: t.low - fob };
    return { kind: 'within' as const, amount: 0 };
  });

  /** Minutes of making that would close a gap (only with the plant's rate, Admin and Costing). */
  readonly lever = computed(() => {
    const e = this.estimate();
    const v = this.verdict();
    if (!e || v?.kind !== 'over' || !e.cpmFromRate || !e.cpm || e.netShare === null) return null;
    // The estimate keeps the garment's net balance share: a FOB gap needs gap x (1 - share) off the costs, in minutes of making or on fabric and trims.
    const costs = v.amount * (1 - e.netShare);
    return { minutes: costs / e.cpm, cpm: e.cpm, costs };
  });

  readonly parts = computed(() => {
    const e = this.estimate();
    if (!e || e.making === null) return [];
    return [
      { key: 'making', amount: e.making },
      { key: 'fabric', amount: e.fabric ?? 0 },
      { key: 'trims', amount: e.trims ?? 0 },
      { key: 'other', amount: e.other ?? 0 },
      { key: 'net', amount: e.estimatedFob - (e.making + (e.fabric ?? 0) + (e.trims ?? 0) + (e.other ?? 0)) },
    ];
  });
}
