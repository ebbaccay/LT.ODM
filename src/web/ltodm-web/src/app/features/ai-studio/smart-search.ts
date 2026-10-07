import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideImage, lucideScanSearch, lucideSparkles } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { problemMessage } from '../../core/auth/auth.service';
import { AiProviderBanner } from './ai-provider-banner';
import { AiStudioService, StyleSearchResult } from './ai-studio.service';

const EXAMPLES = ['ai.search.example1', 'ai.search.example2', 'ai.search.example3', 'ai.search.example4'];

/**
 * AI Studio > Smart search: a request in plain words becomes Styles filters. The AI only fills filters with the
 * library's own codes (the API drops anything else), so results are always real styles; "Open in Styles" shows the
 * same filters on the Styles list.
 */
@Component({
  selector: 'app-smart-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, NgIcon, AuthImgDirective, AiProviderBanner, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports,
    HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideArrowRight, lucideImage, lucideScanSearch, lucideSparkles })],
  template: `
    <section class="flex flex-col gap-5">
      <header>
        <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideScanSearch" />{{ 'ai.search.title' | transloco }}</h1>
        <p class="text-muted-foreground mt-1 text-sm">{{ 'ai.search.subtitle' | transloco }}</p>
      </header>
      <app-ai-provider-banner uses="text" />

      <form class="flex flex-col gap-3" (submit)="run($event)">
        <div class="flex flex-col gap-2 sm:flex-row">
          <input
            hlmInput
            id="ai-search"
            class="h-11 w-full lg:h-10"
            maxlength="300"
            [attr.aria-label]="'ai.search.label' | transloco"
            [placeholder]="'ai.search.placeholder' | transloco"
            [value]="query()"
            (input)="query.set($any($event.target).value)"
          />
          <button hlmBtn type="submit" class="h-11 shrink-0 lg:h-10" [disabled]="busy() || query().trim().length < 3">
            @if (busy()) {
              <hlm-spinner class="size-4" />
            } @else {
              <ng-icon name="lucideSparkles" />
            }
            {{ 'ai.search.run' | transloco }}
          </button>
        </div>
        <div class="flex flex-wrap gap-2">
          @for (e of examples; track e) {
            <button type="button" class="bg-muted hover:bg-accent rounded-full px-3 py-1 text-xs" (click)="useExample(e)">{{ e | transloco }}</button>
          }
        </div>
      </form>

      @if (result(); as r) {
        <section hlmCard class="gap-3 p-5">
          <p class="text-sm"><span class="text-muted-foreground">{{ 'ai.search.readAs' | transloco }}</span> {{ r.explanation || '—' }}</p>
          <div class="flex flex-wrap gap-1.5">
            @for (c of chips(); track c.label + c.value) {
              <span hlmBadge variant="secondary"><span class="text-muted-foreground">{{ c.label | transloco }}:</span>&nbsp;{{ c.value }}</span>
            } @empty {
              <span class="text-muted-foreground text-xs">{{ 'ai.search.noFilters' | transloco }}</span>
            }
          </div>
          <div class="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
            <p class="text-sm font-medium">{{ 'ai.search.found' | transloco: { count: (r.results.total | number) } }}</p>
            <a hlmBtn variant="outline" class="h-11 lg:h-9" routerLink="/styles" [queryParams]="stylesParams()">
              {{ 'ai.search.openInStyles' | transloco }}<ng-icon name="lucideArrowRight" />
            </a>
          </div>
        </section>

        @if (r.results.items.length) {
          <section hlmCard class="gap-0 py-0">
            <ul class="divide-y" role="list">
              @for (s of r.results.items; track s.styleId) {
                <li>
                  <a [routerLink]="['/styles', s.styleId]" class="hover:bg-accent/40 flex items-center gap-4 px-5 py-3 transition-colors">
                    <span class="bg-muted flex size-12 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                      @if (s.imageUrl || s.sketchUrl) {
                        <img [appAuthSrc]="(s.imageUrl || s.sketchUrl)!" alt="" class="size-full object-cover" loading="lazy" />
                      } @else {
                        <ng-icon name="lucideImage" class="text-muted-foreground" />
                      }
                    </span>
                    <span class="min-w-0 flex-1">
                      <span class="block truncate font-mono text-sm font-medium">{{ s.styleNo }}</span>
                      <span class="text-muted-foreground block truncate text-xs">{{ s.modelName || s.description || '—' }}</span>
                    </span>
                    <span class="text-muted-foreground hidden text-right text-xs sm:block">
                      {{ s.customerCode }} · {{ s.seasonCode }}<br />{{ s.productTypeName || '—' }}
                    </span>
                  </a>
                </li>
              }
            </ul>
            @if (r.results.total > r.results.items.length) {
              <p class="text-muted-foreground border-t px-5 py-3 text-center text-xs">
                {{ 'ai.search.firstOnly' | transloco: { shown: r.results.items.length, total: (r.results.total | number) } }}
              </p>
            }
          </section>
        }
      }
    </section>
  `,
})
export class SmartSearch {
  private readonly svc = inject(AiStudioService);
  private readonly transloco = inject(TranslocoService);

  readonly examples = EXAMPLES;
  readonly query = signal('');
  readonly busy = signal(false);
  readonly result = signal<StyleSearchResult | null>(null);

  readonly chips = computed(() => {
    const f = this.result()?.filters;
    if (!f) return [];
    const list: { label: string; value: string }[] = [];
    const add = (label: string, value: string | null | undefined) => value && list.push({ label, value });
    add('ai.search.chip.customer', f.customer);
    add('ai.search.chip.seasons', f.seasons.join(', '));
    add('ai.search.chip.productTypes', f.productTypes.join(', '));
    add('ai.search.chip.businessUnit', f.businessUnit);
    add('ai.search.chip.gender', f.gender);
    add('ai.search.chip.weave', f.weaveType);
    add('ai.search.chip.material', f.material);
    add('ai.search.chip.keywords', f.search);
    return list;
  });

  /** The same filters as Styles list query parameters (season and product type may hold several codes). */
  readonly stylesParams = computed(() => {
    const f = this.result()?.filters;
    if (!f) return {};
    const p: Record<string, string> = {};
    const set = (key: string, value: string | null | undefined) => value && (p[key] = value);
    set('search', f.search);
    set('customer', f.customer);
    set('season', f.seasons.join(','));
    set('businessUnit', f.businessUnit);
    set('productType', f.productTypes.join(','));
    set('weaveType', f.weaveType);
    set('gender', f.gender);
    set('material', f.material);
    return p;
  });

  useExample(key: string): void {
    this.query.set(this.transloco.translate(key));
    this.run();
  }

  run(event?: Event): void {
    event?.preventDefault();
    const q = this.query().trim();
    if (q.length < 3 || this.busy()) return;
    this.busy.set(true);
    this.svc.search(q).subscribe({
      next: (r) => {
        this.result.set(r);
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }
}
