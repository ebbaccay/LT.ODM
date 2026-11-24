import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTarget } from '@ng-icons/lucide';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { ConceptCost, CostingService } from '../../features/costing/costing.service';

/** The pill for each status: over the target needs work; within or under is fine; the rest need something set first. */
const TONE: Record<ConceptCost['status'], Tone> = {
  Over: 'amber', Within: 'green', Under: 'green', NoCompare: 'neutral', NoTarget: 'neutral', NoCategory: 'neutral', NoEstimate: 'neutral',
};

/** Designer dashboard > My concepts vs target: the user's concepts with their estimated FOB against the target price (FOB only). */
@Component({
  selector: 'app-my-concepts-cost',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, RouterLink, NgIcon, HlmCardImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideTarget })],
  styles: `:host { display: block; min-width: 0; }`,
  template: `
    <section hlmCard class="h-full min-w-0">
      <div hlmCardHeader>
        <h2 hlmCardTitle class="flex items-center gap-2"><ng-icon name="lucideTarget" class="text-dash-3" />{{ 'home.costing.conceptsTitle' | transloco }}</h2>
        <p hlmCardDescription>{{ 'home.costing.conceptsHint' | transloco }}</p>
      </div>
      <div hlmCardContent>
        @if (rows() === null) {
          @for (i of [1, 2, 3]; track i) { <hlm-skeleton class="mb-3 h-10 w-full" /> }
        } @else if (!rows()!.length) {
          <p class="text-muted-foreground text-sm">{{ 'home.costing.conceptsNone' | transloco }}</p>
        } @else {
          <ul class="divide-y" role="list">
            @for (c of rows(); track c.conceptId) {
              <li class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 py-2.5 text-sm">
                <span class="min-w-0">
                  <a [routerLink]="['/concepts', c.conceptId]" class="text-primary block truncate font-medium underline-offset-2 hover:underline">{{ c.name }}</a>
                  <span class="text-muted-foreground text-xs">
                    {{ c.categoryName ?? '—' }}@if (c.customerCode) { · {{ c.customerCode }} }@if (c.targetPrice) { · {{ 'home.costing.target' | transloco: { target: c.targetPrice } }} }
                  </span>
                </span>
                <span class="flex flex-col items-end gap-0.5">
                  <span class="rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge(tone[c.status])">
                    {{ 'home.costing.status.' + c.status | transloco: { gap: (c.gap | number: '1.2-2'), currency: c.currencyCode } }}
                  </span>
                  @if (c.estimatedFob !== null) {
                    <span class="text-muted-foreground text-xs tabular-nums">{{ 'home.costing.estimate' | transloco: { fob: (c.estimatedFob | number: '1.2-2'), currency: c.currencyCode } }}</span>
                  }
                </span>
              </li>
            }
          </ul>
        }
      </div>
    </section>
  `,
})
export class MyConceptsCost {
  private readonly svc = inject(CostingService);
  protected readonly toneBadge = toneBadge;
  protected readonly tone = TONE;
  /** Changes when the dashboard is refreshed. */
  readonly reload = input(0);
  readonly rows = signal<ConceptCost[] | null>(null);

  constructor() {
    effect(() => {
      this.reload();
      this.svc.myConcepts().subscribe({ next: (r) => this.rows.set(r), error: () => this.rows.set([]) });
    });
  }
}
