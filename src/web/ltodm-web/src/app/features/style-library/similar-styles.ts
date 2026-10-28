import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGitCompareArrows, lucideImage, lucideInfo, lucidePackagePlus } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { AddToPackageDialog } from '../style-packages/add-to-package-dialog';
import { SimilarStyle, StyleGapsService } from './style-gaps.service';
import { StyleLibraryService } from './style-library.service';

/**
 * Style page > Similar: styles that share this one's materials (fabrics and rare materials count most), garment, weave and
 * gender, for "have we made this before, and what did we use?". Same brand first; other brands' styles are listed apart as
 * know-how only (never pitched). Loaded when the tab opens; rules only, no AI.
 */
@Component({
  selector: 'app-similar-styles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, RouterLink, NgIcon, AuthImgDirective, AddToPackageDialog, HlmBadgeImports, HlmCardImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideGitCompareArrows, lucideImage, lucideInfo, lucidePackagePlus })],
  template: `
    <section hlmCard class="gap-0 py-0">
      <div class="border-b px-5 py-3">
        <h2 class="text-sm font-semibold">{{ 'similar.title' | transloco }}</h2>
        <p class="text-muted-foreground text-xs">{{ 'similar.hint' | transloco }}</p>
      </div>
      @if (loading()) {
        <div class="grid gap-3 p-5 sm:grid-cols-2 xl:grid-cols-4">
          @for (i of [1, 2, 3, 4]; track i) { <hlm-skeleton class="h-36 w-full" /> }
        </div>
      } @else if (failed()) {
        <p class="text-destructive px-5 py-10 text-center text-sm">{{ 'similar.loadFailed' | transloco }}</p>
      } @else if (!items().length) {
        <p class="text-muted-foreground px-5 py-10 text-center text-sm">{{ 'similar.none' | transloco }}</p>
      } @else {
        @if (sameBrand().length) {
          <h3 class="text-muted-foreground px-5 pt-4 text-xs font-medium tracking-wide uppercase">{{ 'similar.sameBrand' | transloco }}</h3>
          <ul class="grid gap-3 p-5 sm:grid-cols-2 xl:grid-cols-4" role="list">
            @for (s of sameBrand(); track s.styleId) { <ng-container *ngTemplateOutlet="card; context: { $implicit: s }" /> }
          </ul>
        }
        @if (otherBrands().length) {
          <div class="border-t px-5 pt-4">
            <h3 class="text-muted-foreground text-xs font-medium tracking-wide uppercase">{{ 'similar.otherBrands' | transloco }}</h3>
            <p class="mt-1 flex items-start gap-1.5 text-xs text-amber-700 dark:text-amber-400">
              <ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />{{ 'similar.otherBrandsRule' | transloco }}
            </p>
          </div>
          <ul class="grid gap-3 p-5 sm:grid-cols-2 xl:grid-cols-4" role="list">
            @for (s of otherBrands(); track s.styleId) { <ng-container *ngTemplateOutlet="card; context: { $implicit: s }" /> }
          </ul>
        }
      }
    </section>

    <ng-template #card let-s>
      <li class="hover:bg-accent/30 flex min-w-0 flex-col gap-2 rounded-lg border p-3 transition-colors">
        <a [routerLink]="['/styles', s.styleId]" class="flex min-w-0 gap-3">
          <span class="bg-muted flex size-16 shrink-0 items-center justify-center overflow-hidden rounded-md border">
            @let thumb = thumbFor(s);
            @if (thumb) {
              <img [appAuthSrc]="thumb" alt="" class="size-full object-cover" loading="lazy" (loadError)="markBroken(thumb)" />
            } @else {
              <ng-icon name="lucideImage" class="text-muted-foreground" />
            }
          </span>
          <span class="min-w-0">
            <span class="block truncate font-mono text-sm font-medium underline-offset-2 hover:underline">{{ s.styleNo }}</span>
            <span class="text-muted-foreground block truncate text-xs">{{ s.customerCode }} · {{ s.seasonCode }}</span>
            <span class="text-muted-foreground block truncate text-xs">{{ s.productTypeName || s.description || '—' }}</span>
          </span>
        </a>
        <div class="flex flex-wrap items-center gap-1.5 text-xs">
          <span hlmBadge variant="secondary" class="tabular-nums" [title]="'similar.scoreHint' | transloco">{{ 'similar.score' | transloco: { score: s.score } }}</span>
          @if (s.sharedMaterials) {
            <span class="text-muted-foreground">{{ 'similar.shared' | transloco: { materials: s.sharedMaterials, fabrics: s.sharedFabrics } }}</span>
          } @else if (s.sameGarment) {
            <span class="text-muted-foreground">{{ 'similar.sameGarmentOnly' | transloco }}</span>
          }
          @if (!s.isActive) { <span hlmBadge variant="outline">{{ 'similar.inactive' | transloco }}</span> }
        </div>
        @if (s.mainFabric) {
          <p class="text-muted-foreground line-clamp-2 text-xs" [title]="s.mainFabric">{{ 'similar.mainFabric' | transloco: { fabric: s.mainFabric } }}</p>
        }
        <div class="mt-auto flex flex-wrap gap-x-4">
          <a [routerLink]="['/styles', styleId(), 'compare']" [queryParams]="{ from: s.styleId }"
             class="text-primary flex min-h-11 items-center gap-1 text-xs underline-offset-2 hover:underline lg:min-h-0">
            <ng-icon name="lucideGitCompareArrows" />{{ 'similar.compare' | transloco }}
          </a>
          <!-- Another brand's style is not added as it is: the dialog offers it as a copy with its own style number -->
          @if (canEdit()) {
            <button type="button" class="text-primary flex min-h-11 items-center gap-1 text-xs underline-offset-2 hover:underline lg:min-h-0" (click)="packageStyle.set(s)">
              <ng-icon name="lucidePackagePlus" />{{ 'packages.pick.addToPackage' | transloco }}
            </button>
          }
        </div>
      </li>
    </ng-template>

    @if (packageStyle(); as p) {
      <app-add-to-package-dialog [open]="true" [styleIds]="[p.styleId]" addedFrom="Similar" [customerCode]="p.customerCode" [seasonCode]="p.seasonCode"
                                 (closed)="packageStyle.set(null)" />
    }
  `,
})
export class SimilarStyles {
  private readonly svc = inject(StyleGapsService);

  readonly canEdit = inject(StyleLibraryService).canEdit;
  /** The style being added to a package. */
  readonly packageStyle = signal<SimilarStyle | null>(null);

  readonly styleId = input.required<number>();

  readonly items = signal<SimilarStyle[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly broken = signal<ReadonlySet<string>>(new Set());
  readonly sameBrand = computed(() => this.items().filter((s) => s.sameBrand));
  readonly otherBrands = computed(() => this.items().filter((s) => !s.sameBrand));

  constructor() {
    effect(() => {
      const id = this.styleId();
      if (!id) return;
      this.loading.set(true);
      this.failed.set(false);
      this.svc.similar(id).subscribe({
        next: (items) => {
          this.items.set(items);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
    });
  }

  thumbFor(s: SimilarStyle): string | null {
    const broken = this.broken();
    return [s.imageUrl, s.sketchUrl].find((u): u is string => !!u && !broken.has(u)) ?? null;
  }

  markBroken(url: string): void {
    this.broken.update((s) => new Set(s).add(url));
  }
}
