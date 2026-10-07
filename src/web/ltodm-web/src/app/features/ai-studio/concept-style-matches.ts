import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { environment } from '@env/environment';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideLibrary, lucideSparkles } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { problemMessage } from '../../core/auth/auth.service';

/** A Concept Studio concept as the screen holds it (POST /api/v1/ai/concept-matches). */
export interface ConceptForMatch {
  name: string;
  customer: string;
  season: string;
  targetMarket: string;
  targetPrice: string;
  trends: string[];
  brief: string;
  products: string[];
  fabrics: string[];
  notes: string[];
}

interface ConceptMatchResult {
  criteria: {
    customer: string | null;
    productTypes: string[];
    gender: string | null;
    weaveType: string | null;
    keywords: string[];
    materialTypes: string[];
    explanation: string;
  };
  candidatesScored: number;
  matches: {
    style: {
      styleId: number;
      styleNo: string;
      seasonCode: string;
      customerCode: string;
      modelName: string | null;
      productTypeName: string | null;
      imageUrl: string | null;
      sketchUrl: string | null;
      mainFabrics: string | null;
    };
    fit: number;
    why: string;
    reuse: string;
    differs: string;
  }[];
}

/**
 * Concept Studio panel: proven library styles to start the concept from. The AI reads the concept into library
 * criteria, SQL scores every style with a BOM, and the AI ranks and explains the best few (AI Studio, Text job).
 * Results belong to the concept as it was when asked; changing the concept marks them as out of date.
 */
@Component({
  selector: 'app-concept-style-matches',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, AuthImgDirective, HlmButtonImports, HlmCardImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideImage, lucideLibrary, lucideSparkles })],
  template: `
    <section hlmCard class="gap-0 py-0">
      <div class="flex flex-wrap items-center justify-between gap-2 border-b px-5 py-3">
        <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideLibrary" />{{ 'csMatch.title' | transloco }}</h2>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-8" [disabled]="busy() || !ready()" (click)="find()">
          @if (busy()) {
            <hlm-spinner class="size-4" />
          } @else {
            <ng-icon name="lucideSparkles" />
          }
          {{ (result() ? 'csMatch.again' : 'csMatch.find') | transloco }}
        </button>
      </div>

      @if (busy()) {
        <div class="flex flex-col gap-2 p-4">
          <p class="text-muted-foreground text-xs">{{ 'csMatch.working' | transloco }}</p>
          @for (i of [1, 2, 3]; track i) {
            <div hlmSkeleton class="h-16 w-full"></div>
          }
        </div>
      } @else if (result(); as r) {
        @if (stale()) {
          <p class="border-b bg-amber-500/10 px-5 py-2 text-xs text-amber-800 dark:text-amber-300">{{ 'csMatch.stale' | transloco }}</p>
        }
        <div class="flex flex-col gap-1 border-b px-5 py-3 text-xs">
          <p><span class="text-muted-foreground">{{ 'csMatch.lookedFor' | transloco }}</span> {{ r.criteria.explanation }}</p>
          <p class="text-muted-foreground">{{ criteriaText() }}</p>
        </div>
        @if (r.matches.length) {
          <ul class="divide-y" role="list">
            @for (m of r.matches; track m.style.styleId) {
              <li class="flex gap-3 px-4 py-3">
                <a [routerLink]="['/styles', m.style.styleId]" class="bg-muted flex size-14 shrink-0 items-center justify-center overflow-hidden rounded-md border" [attr.aria-label]="m.style.styleNo">
                  @if (m.style.imageUrl || m.style.sketchUrl) {
                    <img [appAuthSrc]="(m.style.imageUrl || m.style.sketchUrl)!" alt="" class="size-full object-cover" loading="lazy" />
                  } @else {
                    <ng-icon name="lucideImage" class="text-muted-foreground" />
                  }
                </a>
                <div class="min-w-0 flex-1 text-sm">
                  <p class="flex flex-wrap items-center gap-2">
                    <a [routerLink]="['/styles', m.style.styleId]" class="text-primary font-mono font-medium underline-offset-2 hover:underline">{{ m.style.styleNo }}</a>
                    <span class="rounded px-1.5 py-0.5 text-[11px] font-semibold tabular-nums" [class]="fitClass(m.fit)">{{ m.fit }}%</span>
                  </p>
                  <p class="text-muted-foreground truncate text-xs">{{ m.style.modelName || '—' }} · {{ m.style.productTypeName || '—' }} · {{ m.style.customerCode }} {{ m.style.seasonCode }}</p>
                  <p class="mt-1 text-xs">{{ m.why }}</p>
                  @if (m.reuse) {
                    <p class="text-xs"><span class="text-muted-foreground">{{ 'csMatch.reuse' | transloco }}</span> {{ m.reuse }}</p>
                  }
                  @if (m.differs) {
                    <p class="text-xs"><span class="text-muted-foreground">{{ 'csMatch.differs' | transloco }}</span> {{ m.differs }}</p>
                  }
                </div>
              </li>
            }
          </ul>
        } @else {
          <p class="text-muted-foreground px-5 py-6 text-center text-sm">{{ 'csMatch.none' | transloco }}</p>
        }
        <p class="text-muted-foreground border-t px-5 py-2 text-[11px]">{{ 'csMatch.footer' | transloco: { count: r.candidatesScored } }}</p>
      } @else {
        <p class="text-muted-foreground px-5 py-6 text-center text-sm">{{ (ready() ? 'csMatch.hint' : 'csMatch.needDraft') | transloco }}</p>
      }
    </section>
  `,
})
export class ConceptStyleMatches {
  private readonly http = inject(HttpClient);
  private readonly transloco = inject(TranslocoService);

  readonly concept = input.required<ConceptForMatch>();

  readonly busy = signal(false);
  readonly result = signal<ConceptMatchResult | null>(null);
  private readonly askedFor = signal<string>('');

  /** Enough to match on: a drafted brief, or products / fabrics / trend tags. */
  readonly ready = computed(() => {
    const c = this.concept();
    return !!c.brief.trim() || c.products.length > 0 || c.fabrics.length > 0 || c.trends.length > 0;
  });
  readonly stale = computed(() => !!this.result() && this.askedFor() !== JSON.stringify(this.concept()));

  readonly criteriaText = computed(() => {
    const c = this.result()?.criteria;
    if (!c) return '';
    return [c.customer, c.gender, c.weaveType, c.productTypes.join(', '), c.keywords.join(', '), c.materialTypes.join(', ')]
      .filter((x) => !!x)
      .join(' · ');
  });

  constructor() {
    // A different concept loaded (new name and brief) clears the old results.
    let lastName = '';
    effect(() => {
      const name = this.concept().name;
      untracked(() => {
        if (name !== lastName && this.result() && this.stale()) this.result.set(null);
        lastName = name;
      });
    });
  }

  find(): void {
    if (this.busy() || !this.ready()) return;
    const concept = this.concept();
    this.busy.set(true);
    this.http.post<ConceptMatchResult>(`${environment.apiBaseUrl}/api/v1/ai/concept-matches`, concept).subscribe({
      next: (r) => {
        this.result.set(r);
        this.askedFor.set(JSON.stringify(concept));
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }

  fitClass = (fit: number) =>
    fit >= 85 ? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400' : fit >= 60 ? 'bg-amber-500/15 text-amber-700 dark:text-amber-400' : 'bg-muted text-muted-foreground';
}
