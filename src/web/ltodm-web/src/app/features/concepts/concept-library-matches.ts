import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideLibrary, lucidePlus, lucideSparkles } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { toneBadge } from '@tms/shared/ui/tones';
import { problemMessage } from '../../core/auth/auth.service';
import { styleError } from '../style-library/style-library.service';
import { ConceptsService, LibraryMatches, MatchExplanations } from './concepts.service';

/**
 * Concept page: proven library styles to start the concept from. Found at once without AI (the concept's category, gender,
 * customer and fabric words, scored in SQL); **Explain with AI** then lets the Text job rank them and say why each fits, what
 * can be reused and what differs (one AI call; minutes on an in-house CPU server). Matches can be recorded as inspiration styles.
 */
@Component({
  selector: 'app-concept-library-matches',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, AuthImgDirective, HlmButtonImports, HlmCardImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideImage, lucideLibrary, lucidePlus, lucideSparkles })],
  template: `
    <section hlmCard class="gap-0 py-0">
      <div class="flex flex-wrap items-center justify-between gap-2 border-b px-5 py-3">
        <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideLibrary" />{{ 'csMatch.title' | transloco }}</h2>
        @if (result()?.matches?.length) {
          <button hlmBtn variant="outline" type="button" class="h-11 lg:h-8" [disabled]="explaining()" (click)="explain()">
            @if (explaining()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideSparkles" /> }
            {{ (explained() ? 'concepts.matches.explainAgain' : 'concepts.matches.explain') | transloco }}
          </button>
        }
      </div>

      @if (loading() && !result()) {
        <div class="flex flex-col gap-2 p-4">
          @for (i of [1, 2, 3]; track i) { <div hlmSkeleton class="h-16 w-full"></div> }
        </div>
      } @else if (result(); as r) {
        <div class="flex flex-col gap-1 border-b px-5 py-3 text-xs">
          <p><span class="text-muted-foreground">{{ 'csMatch.lookedFor' | transloco }}</span> {{ r.criteria.explanation }}</p>
          @if (explaining()) { <p class="text-muted-foreground">{{ 'concepts.matches.explaining' | transloco }}</p> }
          @if (stale()) { <p class="text-amber-700 dark:text-amber-400">{{ 'concepts.matches.stale' | transloco }}</p> }
        </div>
        @if (rows().length) {
          <ul class="divide-y" role="list">
            @for (row of rows(); track row.style.styleId) {
              @let m = row.style;
              @let x = row.why;
              <li class="flex gap-3 px-4 py-3">
                <a [routerLink]="['/styles', m.styleId]" class="bg-muted flex size-14 shrink-0 items-center justify-center overflow-hidden rounded-md border" [attr.aria-label]="m.styleNo">
                  @if (m.imageUrl || m.sketchUrl) {
                    <img [appAuthSrc]="(m.imageUrl || m.sketchUrl)! + '?w=160'" alt="" class="size-full object-contain" loading="lazy" />
                  } @else {
                    <ng-icon name="lucideImage" class="text-muted-foreground" />
                  }
                </a>
                <div class="min-w-0 flex-1 text-sm">
                  <p class="flex flex-wrap items-center gap-2">
                    <a [routerLink]="['/styles', m.styleId]" class="text-primary font-mono font-medium underline-offset-2 hover:underline">{{ m.styleNo }}</a>
                    @if (x) { <span class="rounded px-1.5 py-0.5 text-[11px] font-semibold tabular-nums" [class]="fitClass(x.fit)">{{ x.fit }}%</span> }
                    @if (row.sameBrand === false) {
                      <span class="rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge('amber')">{{ 'concepts.styles.knowHowOnly' | transloco }}</span>
                    } @else if (row.sameBrand) {
                      <span class="rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge('green')">{{ 'concepts.styles.ownBrand' | transloco }}</span>
                    }
                  </p>
                  <p class="text-muted-foreground truncate text-xs">{{ m.modelName || '—' }} · {{ m.productTypeName || '—' }} · {{ m.customerCode }} {{ m.seasonCode }}</p>
                  @if (m.mainFabrics) { <p class="text-muted-foreground truncate text-xs" [title]="m.mainFabrics">{{ m.mainFabrics }}</p> }
                  @if (m.matchedKeywords) { <p class="text-xs">{{ 'concepts.matches.matched' | transloco: { words: m.matchedKeywords } }}</p> }
                  @if (x) {
                    <p class="mt-1 text-xs">{{ x.why }}</p>
                    @if (x.reuse) { <p class="text-xs"><span class="text-muted-foreground">{{ 'csMatch.reuse' | transloco }}</span> {{ x.reuse }}</p> }
                    @if (x.differs) { <p class="text-xs"><span class="text-muted-foreground">{{ 'csMatch.differs' | transloco }}</span> {{ x.differs }}</p> }
                  }
                  @if (canAdd()) {
                    @if (addedIds().includes(m.styleId)) {
                      <p class="text-muted-foreground mt-1 text-xs">{{ 'csMatch.added' | transloco }}</p>
                    } @else {
                      <button type="button" class="text-primary mt-1 min-h-6 text-left text-xs underline-offset-2 hover:underline" (click)="add.emit(m.styleId)">
                        {{ 'csMatch.addInspiration' | transloco }}
                      </button>
                    }
                  }
                </div>
              </li>
            }
          </ul>
        } @else {
          <p class="text-muted-foreground px-5 py-6 text-center text-sm">{{ 'csMatch.none' | transloco }}</p>
        }
        <p class="text-muted-foreground border-t px-5 py-2 text-[11px]">
          {{ (explained() ? 'concepts.matches.footerExplained' : 'concepts.matches.footer') | transloco }}
        </p>
      }
    </section>
  `,
})
export class ConceptLibraryMatches {
  private readonly svc = inject(ConceptsService);
  private readonly transloco = inject(TranslocoService);

  readonly conceptId = input.required<number>();
  /** Changes when the concept is saved or its brief drafted: the matches are found again. */
  readonly version = input<string>('');
  readonly canAdd = input(false);
  readonly addedIds = input<number[]>([]);
  readonly add = output<number>();

  readonly toneBadge = toneBadge;
  readonly loading = signal(false);
  readonly explaining = signal(false);
  readonly result = signal<LibraryMatches | null>(null);
  readonly explained = signal<MatchExplanations | null>(null);
  /** The concept version the AI explanation was made for. */
  private readonly explainedFor = signal('');
  readonly stale = computed(() => !!this.explained() && this.explainedFor() !== this.version());

  /** Explained styles first, in the AI's order with its reasons; then the rest by score. */
  readonly rows = computed(() => {
    const r = this.result();
    if (!r) return [];
    const why = new Map((this.explained()?.matches ?? []).map((m, i) => [m.style.styleId, { ...m, order: i }]));
    const brand = new Map(r.matches.map((m) => [m.style.styleId, m.sameBrand]));
    const explainedRows = [...why.values()].sort((a, b) => a.order - b.order).map((m) => ({ style: m.style, sameBrand: brand.get(m.style.styleId) ?? null, why: m }));
    const rest = r.matches.filter((m) => !why.has(m.style.styleId)).map((m) => ({ ...m, why: null }));
    return [...explainedRows, ...rest];
  });

  constructor() {
    effect(() => {
      const id = this.conceptId();
      this.version();
      untracked(() => this.load(id));
    });
  }

  private load(conceptId: number): void {
    if (!conceptId) return;
    this.loading.set(true);
    this.svc.libraryMatches(conceptId).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  explain(): void {
    if (this.explaining()) return;
    const version = this.version();
    this.explaining.set(true);
    this.svc.explainMatches(this.conceptId()).subscribe({
      next: (r) => {
        this.explained.set(r);
        this.explainedFor.set(version);
        this.explaining.set(false);
      },
      error: (e) => {
        this.explaining.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }

  fitClass = (fit: number) =>
    fit >= 85 ? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400' : fit >= 60 ? 'bg-amber-500/15 text-amber-700 dark:text-amber-400' : 'bg-muted text-muted-foreground';
}
