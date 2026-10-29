import { DatePipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft, lucideCircleAlert, lucideImage, lucideImagePlus, lucideLibrary, lucidePackage, lucidePalette, lucidePencil, lucidePlus, lucideRefreshCw,
  lucideSearch, lucideShieldAlert, lucideShieldCheck, lucideSparkles, lucideTrash2, lucideWandSparkles, lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { compressImage } from '@tms/shared/image-compress';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { toneBadge } from '@tms/shared/ui/tones';
import { Subject, debounceTime, switchMap, of } from 'rxjs';
import { problemMessage } from '../../core/auth/auth.service';
import { GENDERS, StyleLibraryService, StyleListItem, StyleLookups, styleError } from '../style-library/style-library.service';
import { AddToPackageDialog } from '../style-packages/add-to-package-dialog';
import { CLOSENESS_CLASS } from '../style-packages/packages.service';
import { ConceptFormDialog } from './concept-form-dialog';
import { ConceptLibraryMatches } from './concept-library-matches';
import { DevelopStyleDialog } from './develop-style-dialog';
import {
  CONCEPT_STATUS_TONE, ConceptCloseness, ConceptDetail, ConceptImageCloseness, ConceptLookups, ConceptStyle, ConceptStyleSource, ConceptsService,
} from './concepts.service';

const MAX_DIMENSION = 1200;   // the concept image store keeps at most 1200 px
/** What the API takes (it checks the content too). */
const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];

/**
 * A concept: its details and trend tags, inspiration pictures with the brand closeness check (roadmap 5.2: each picture against
 * every library picture; another brand's look-alike is flagged), the AI brief, the library styles it takes from (design
 * provenance, 5.1; another brand's style is know-how only) and the styles offered in packages for it. "Offer in a package" adds
 * the inspiration styles to a package (another brand's as a copy) with the concept recorded on each item.
 */
@Component({
  selector: 'app-concept-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, PercentPipe, RouterLink, NgIcon, AuthImgDirective, ConceptFormDialog, ConceptLibraryMatches, DevelopStyleDialog, AddToPackageDialog, HlmBadgeImports, HlmButtonImports,
    HlmCardImports, HlmInputImports, HlmSkeletonImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft, lucideCircleAlert, lucideImage, lucideImagePlus, lucideLibrary, lucidePackage, lucidePalette, lucidePencil, lucidePlus, lucideRefreshCw,
      lucideSearch, lucideShieldAlert, lucideShieldCheck, lucideSparkles, lucideTrash2, lucideWandSparkles, lucideX,
    }),
  ],
  template: `
    <section class="flex flex-col gap-5" [attr.aria-busy]="loading()">
      <a routerLink="/concepts" class="text-muted-foreground hover:text-foreground flex w-fit items-center gap-1 text-sm">
        <ng-icon name="lucideArrowLeft" />{{ 'concepts.back' | transloco }}
      </a>

      @if (loading() && !detail()) {
        <div hlmSkeleton class="h-40 w-full"></div>
        <div hlmSkeleton class="h-72 w-full"></div>
      } @else if (notFound()) {
        <section hlmCard class="items-center gap-2 px-5 py-14 text-center">
          <p class="font-medium">{{ 'concepts.notFound' | transloco }}</p>
          <a routerLink="/concepts" class="text-primary text-sm underline-offset-2 hover:underline">{{ 'concepts.back' | transloco }}</a>
        </section>
      } @else if (detail(); as d) {
        @let c = d.concept;
        <!-- Details -->
        <section hlmCard class="gap-4 p-5">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="min-w-0">
              <h1 class="flex flex-wrap items-center gap-2 text-2xl font-semibold tracking-tight">
                <span class="break-words">{{ c.name }}</span>
                <span class="rounded-full px-2 py-0.5 text-xs font-medium" [class]="toneBadge(statusTone[c.status])">{{ 'concepts.status.' + c.status | transloco }}</span>
              </h1>
              <div class="mt-2 flex flex-wrap gap-1.5">
                <span hlmBadge variant="secondary">{{ c.customerName || ('concepts.noCustomer' | transloco) }}</span>
                @if (c.brandCode && c.brandCode !== c.customerCode) { <span hlmBadge variant="outline">{{ 'packages.brand' | transloco: { brand: c.brandCode } }}</span> }
                @if (c.seasonCode) { <span hlmBadge variant="secondary">{{ c.seasonCode }}</span> }
                @if (c.productCategoryName) { <span hlmBadge variant="secondary">{{ c.productCategoryName }}</span> }
                @if (c.gender) { <span hlmBadge variant="secondary">{{ genderLabel(c.gender) | transloco }}</span> }
                @if (c.targetMarketName) { <span hlmBadge variant="outline">{{ c.targetMarketName }}</span> }
                @if (c.targetPrice) { <span hlmBadge variant="outline">{{ c.targetPrice }}</span> }
              </div>
              @if (d.tags.length) {
                <div class="mt-2 flex flex-wrap gap-1.5">
                  @for (t of d.tags; track t.code) { <span class="bg-primary/10 text-primary rounded-full px-2 py-0.5 text-[11px] font-medium">{{ t.name }}</span> }
                </div>
              }
            </div>
            <div class="flex w-full flex-wrap gap-2 sm:w-auto">
              @if (canEdit()) {
                <button hlmBtn type="button" class="h-11 flex-1 sm:flex-initial lg:h-9" (click)="developOpen.set(true)" [title]="'concepts.develop.hint' | transloco">
                  <ng-icon name="lucideWandSparkles" />{{ 'concepts.develop.button' | transloco }}
                </button>
                @let designs = d.developedStyles ?? [];
                <button hlmBtn variant="outline" type="button" class="h-11 flex-1 sm:flex-initial lg:h-9" [disabled]="!designs.length && !d.styles.length"
                        (click)="offer(designs.length ? designIds() : styleIds())"
                        [title]="(designs.length ? 'concepts.offerDesignsHint' : d.styles.length ? 'concepts.offerHint' : 'concepts.offerNeedsStyles') | transloco">
                  <ng-icon name="lucidePackage" />{{ 'concepts.offer' | transloco }}
                </button>
                <button hlmBtn variant="outline" type="button" class="h-11 flex-1 sm:flex-initial lg:h-9" (click)="editOpen.set(true)">
                  <ng-icon name="lucidePencil" />{{ 'concepts.edit' | transloco }}
                </button>
                @if (!d.packageItems.length && !d.developedStyles?.length) {
                  <button hlmBtn variant="ghost" type="button" class="text-destructive h-11 lg:h-9" [attr.aria-label]="'concepts.delete' | transloco" (click)="deleteConcept()">
                    <ng-icon name="lucideTrash2" />
                  </button>
                }
              }
            </div>
          </div>
          @if (c.description) { <p class="text-sm whitespace-pre-line">{{ c.description }}</p> }
          <p class="text-muted-foreground text-xs">
            {{ 'packages.ownedBy' | transloco: { owner: c.ownerName } }}
            · {{ 'packages.createdBy' | transloco: { user: c.createdBy, date: (c.createdUtc | date: 'medium') } }}
            @if (c.updatedBy) { · {{ 'packages.changedBy' | transloco: { user: c.updatedBy, date: (c.updatedUtc | date: 'medium') } }} }
          </p>
        </section>

        <!-- LT designs developed from this concept (roadmap 5.21): styles of the customer, offered like any style -->
        @if (d.developedStyles?.length) {
          <section hlmCard class="gap-0 py-0">
            <h2 class="flex items-center gap-2 border-b px-5 py-3 font-semibold"><ng-icon name="lucideWandSparkles" />{{ 'concepts.develop.listTitle' | transloco: { count: d.developedStyles!.length } }}</h2>
            <ul class="divide-y" role="list">
              @for (s of d.developedStyles!; track s.styleId) {
                <li class="flex flex-wrap items-center gap-3 px-4 py-3">
                  <a [routerLink]="['/styles', s.styleId]" class="bg-muted flex size-16 shrink-0 items-center justify-center overflow-hidden rounded-md border" [attr.aria-label]="s.styleNo">
                    @if (s.sketchUrl || s.imageUrl) {
                      <img [appAuthSrc]="(s.imageUrl || s.sketchUrl)! + '?w=160'" alt="" class="size-full object-contain" loading="lazy" />
                    } @else {
                      <ng-icon name="lucideImage" class="text-muted-foreground" />
                    }
                  </a>
                  <div class="min-w-0 flex-1 text-sm">
                    <a [routerLink]="['/styles', s.styleId]" class="font-mono font-medium underline-offset-2 hover:underline">{{ s.styleNo }}</a>
                    <p class="text-muted-foreground text-xs">{{ s.customerCode }} · {{ s.seasonCode }} · {{ 'packages.createdBy' | transloco: { user: s.createdBy, date: (s.createdUtc | date: 'mediumDate') } }}</p>
                    @if (s.originStyleNo) {
                      <p class="text-xs">{{ 'concepts.develop.knowHowFrom' | transloco }}
                        <a [routerLink]="['/styles', s.originStyleId]" class="font-mono underline-offset-2 hover:underline">{{ s.originStyleNo }}</a></p>
                    }
                    @if (s.packages) { <p class="text-muted-foreground text-xs">{{ 'concepts.develop.inPackages' | transloco: { count: s.packages } }}</p> }
                  </div>
                  @if (canEdit()) {
                    <button hlmBtn variant="outline" type="button" class="h-11 lg:h-8" (click)="offer([s.styleId])"><ng-icon name="lucidePackage" />{{ 'concepts.develop.addToPackage' | transloco }}</button>
                  }
                </li>
              }
            </ul>
          </section>
        }

        <!-- Inspiration pictures and the brand closeness check (roadmap 5.2); editors can drop pictures anywhere on the card -->
        <section hlmCard class="relative gap-0 py-0" (dragenter)="onDragEnter($event)" (dragover)="onDragOver($event)" (dragleave)="onDragLeave($event)"
                 (drop)="onDrop($event)">
          @if (dragging()) {
            <div class="border-primary bg-primary/10 text-primary pointer-events-none absolute inset-0 z-20 flex items-center justify-center gap-2 rounded-xl border-2 border-dashed text-sm font-medium">
              <ng-icon name="lucideImagePlus" class="text-xl" />{{ 'concepts.pictures.drop' | transloco }}
            </div>
          }
          <div class="flex flex-wrap items-center justify-between gap-2 border-b px-5 py-3">
            <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideImage" />{{ 'concepts.pictures.title' | transloco }}</h2>
            <div class="flex flex-wrap gap-2">
              @if (d.images.length) {
                <button hlmBtn variant="outline" type="button" class="h-11 lg:h-8" [disabled]="checking()" (click)="check()">
                  @if (checking()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideRefreshCw" /> }
                  {{ 'concepts.closeness.again' | transloco }}
                </button>
              }
              @if (canEdit()) {
                <label class="inline-flex h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm font-medium transition-colors hover:bg-accent/40 lg:h-8"
                       [class.pointer-events-none]="uploading()" [class.opacity-50]="uploading()">
                  @if (uploading()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideImagePlus" /> }
                  {{ 'concepts.pictures.add' | transloco }}
                  <input type="file" class="sr-only" accept="image/jpeg,image/png,image/gif,image/webp" multiple (change)="upload($event)" />
                </label>
              }
            </div>
          </div>
          @let cl = closeness();
          @if (d.images.length) {
            <div class="flex items-start gap-2 border-b px-5 py-2.5 text-sm" [class]="closenessBanner(cl?.level)" role="status">
              <ng-icon [name]="cl?.level === 'Close' || cl?.level === 'Check' ? 'lucideShieldAlert' : 'lucideShieldCheck'" class="mt-0.5 shrink-0" />
              <div>
                <p class="font-medium">{{ 'concepts.closeness.summary.' + (cl?.level ?? 'NotChecked') | transloco }}</p>
                @if (cl && !cl.thresholds.modelReady) { <p class="mt-0.5 text-xs">{{ 'concepts.closeness.modelOff' | transloco }}</p> }
                @else if (cl) {
                  <p class="mt-0.5 text-xs opacity-80">{{ 'concepts.closeness.rule' | transloco: { close: (cl.thresholds.close | percent: '1.0-0'), watch: (cl.thresholds.watch | percent: '1.0-0') } }}</p>
                }
              </div>
            </div>
            <ul class="divide-y" role="list">
              @for (img of d.images; track img.conceptImageId) {
                @let r = closenessOf(img.conceptImageId);
                <li class="flex flex-col gap-3 p-4 sm:flex-row">
                  <div class="flex shrink-0 flex-col gap-1.5 sm:w-44">
                    <div class="bg-muted relative aspect-square w-full overflow-hidden rounded-md border sm:size-44">
                      <img [appAuthSrc]="img.imageUrl + '?w=320'" [alt]="img.caption ?? ''" class="size-full object-contain" loading="lazy" />
                      @if (canEdit()) {
                        <button hlmBtn variant="secondary" size="icon" type="button" class="absolute top-1.5 right-1.5 size-9"
                                [attr.aria-label]="'concepts.pictures.remove' | transloco" (click)="removeImage(img.conceptImageId)">
                          <ng-icon name="lucideX" />
                        </button>
                      }
                    </div>
                    @if (img.caption) { <p class="text-muted-foreground text-xs">{{ img.caption }}</p> }
                    <!-- On the story page for the client (roadmap 5.22): only pictures clear of other brands' designs -->
                    @if (canEdit()) {
                      <label class="flex min-h-9 items-center gap-2 text-xs">
                        <input type="checkbox" class="accent-primary size-4" [checked]="img.forClient" [disabled]="savingImage() === img.conceptImageId"
                               (change)="setForClient(img.conceptImageId, $any($event.target).checked, img.caption)" />
                        {{ 'concepts.pictures.forClient' | transloco }}
                      </label>
                    } @else if (img.forClient) {
                      <p class="text-muted-foreground text-xs">{{ 'concepts.pictures.shownToClient' | transloco }}</p>
                    }
                  </div>
                  <div class="min-w-0 flex-1">
                    @if (!r) {
                      <p class="text-muted-foreground text-xs">{{ (checking() ? 'concepts.closeness.checking' : 'concepts.closeness.notYet') | transloco }}</p>
                    } @else if (r.status !== 'Checked') {
                      <p class="text-muted-foreground flex items-start gap-1.5 text-xs">
                        <ng-icon name="lucideCircleAlert" class="mt-0.5 shrink-0" />{{ r.status === 'Failed' ? r.error : ('concepts.closeness.notRead' | transloco) }}
                      </p>
                    } @else {
                      <p class="mb-2 flex items-center gap-2 text-xs">
                        <span class="rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="closenessClass[r.level]">{{ 'closeness.level.' + r.level | transloco }}</span>
                        <span class="text-muted-foreground">{{ 'concepts.closeness.looksLike' | transloco }}</span>
                      </p>
                      <ul class="grid gap-2 sm:grid-cols-2 xl:grid-cols-3" role="list">
                        @for (m of r.matches; track m.styleId) {
                          <li class="flex gap-2 rounded-md border p-2" [class.border-red-500/50]="m.level === 'Close'" [class.border-amber-500/50]="m.level === 'Check'">
                            <a [routerLink]="['/styles', m.styleId]" class="bg-muted flex size-14 shrink-0 items-center justify-center overflow-hidden rounded border" [attr.aria-label]="m.styleNo">
                              <img [appAuthSrc]="m.imageUrl + '?w=160'" alt="" class="size-full object-contain" loading="lazy" />
                            </a>
                            <div class="min-w-0 flex-1 text-xs">
                              <a [routerLink]="['/styles', m.styleId]" class="block truncate font-mono font-medium underline-offset-2 hover:underline">{{ m.styleNo }}</a>
                              <p class="text-muted-foreground truncate">{{ m.customerCode }} · {{ m.seasonCode }}</p>
                              <p class="flex flex-wrap items-center gap-1">
                                <span class="tabular-nums">{{ m.similarity | percent: '1.0-0' }}</span>
                                @if (m.sameBrand) {
                                  <span class="text-muted-foreground">· {{ 'concepts.closeness.ownBrand' | transloco }}</span>
                                } @else if (m.level !== 'Clear') {
                                  <span class="rounded-full px-1.5 text-[10px] font-medium" [class]="closenessClass[m.level]">{{ 'closeness.level.' + m.level | transloco }}</span>
                                }
                              </p>
                              @if (canEdit() && !hasStyle(m.styleId)) {
                                <button type="button" class="text-primary mt-0.5 min-h-6 text-left underline-offset-2 hover:underline" (click)="addStyles([m.styleId], 'LookAlike')">
                                  {{ 'concepts.styles.addAsInspiration' | transloco }}
                                </button>
                              }
                            </div>
                          </li>
                        } @empty {
                          <li class="text-muted-foreground text-xs">{{ 'concepts.closeness.noMatches' | transloco }}</li>
                        }
                      </ul>
                    }
                  </div>
                </li>
              }
            </ul>
          } @else {
            <p class="text-muted-foreground px-5 py-8 text-center text-sm">
              {{ 'concepts.pictures.empty' | transloco }}
              @if (canEdit()) { <span class="mt-1 block text-xs">{{ 'concepts.pictures.dropHint' | transloco }}</span> }
            </p>
          }
        </section>

        <!-- AI brief -->
        <section hlmCard class="gap-0 py-0">
          <div class="flex flex-wrap items-center justify-between gap-2 border-b px-5 py-3">
            <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideSparkles" />{{ 'concepts.brief.title' | transloco }}</h2>
            @if (canEdit()) {
              <button hlmBtn variant="outline" type="button" class="h-11 lg:h-8" [disabled]="drafting()" (click)="draftBrief()">
                @if (drafting()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideSparkles" /> }
                {{ (c.brief ? 'concepts.brief.again' : 'concepts.brief.draft') | transloco }}
              </button>
            }
          </div>
          @if (c.brief; as b) {
            <div class="flex flex-col gap-3 px-5 py-4 text-sm">
              @if (b.summary) { <p class="whitespace-pre-line">{{ b.summary }}</p> }
              <div class="grid gap-4 md:grid-cols-3">
                @for (part of [{ key: 'products', items: b.products }, { key: 'fabrics', items: b.fabrics }, { key: 'notes', items: b.notes }]; track part.key) {
                  @if (part.items.length) {
                    <div>
                      <h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{{ 'concepts.brief.' + part.key | transloco }}</h3>
                      <ul class="list-disc space-y-0.5 pl-4 text-sm">
                        @for (x of part.items; track $index) { <li>{{ x }}</li> }
                      </ul>
                    </div>
                  }
                }
              </div>
              <p class="text-muted-foreground text-xs">{{ 'concepts.brief.by' | transloco: { user: b.by, date: (b.utc | date: 'medium') } }}</p>
            </div>
          } @else {
            <p class="text-muted-foreground px-5 py-8 text-center text-sm">{{ 'concepts.brief.empty' | transloco }}</p>
          }
        </section>

        <!-- Inspiration styles: design provenance (roadmap 5.1) -->
        <section hlmCard class="gap-0 py-0">
          <div class="flex flex-wrap items-center justify-between gap-2 border-b px-5 py-3">
            <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideLibrary" />{{ 'concepts.styles.title' | transloco: { count: d.styles.length } }}</h2>
          </div>
          <p class="text-muted-foreground border-b px-5 py-2 text-xs">{{ 'concepts.styles.rule' | transloco }}</p>
          @if (canEdit()) {
            <div class="relative border-b px-5 py-3">
              <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-8 -translate-y-1/2" />
              <input hlmInput type="search" class="h-11 w-full pl-9 lg:h-9" [attr.aria-label]="'concepts.styles.search' | transloco"
                     [placeholder]="'concepts.styles.search' | transloco" [value]="styleSearch()" (input)="onStyleSearch($any($event.target).value)" />
              @if (found().length) {
                <ul class="bg-popover absolute right-5 left-5 z-10 mt-1 max-h-72 overflow-y-auto rounded-md border shadow-md" role="listbox">
                  @for (s of found(); track s.styleId) {
                    <li>
                      <button type="button" role="option" class="hover:bg-accent/50 flex min-h-11 w-full items-center gap-3 px-3 py-1.5 text-left text-sm"
                              [disabled]="hasStyle(s.styleId)" [class.opacity-50]="hasStyle(s.styleId)" (click)="pickStyle(s)">
                        <span class="font-mono font-medium">{{ s.styleNo }}</span>
                        <span class="text-muted-foreground truncate text-xs">{{ s.customerCode }} · {{ s.seasonCode }} · {{ s.modelName || s.productTypeName || '—' }}</span>
                      </button>
                    </li>
                  }
                </ul>
              }
            </div>
          }
          @if (d.styles.length) {
            <ul class="divide-y" role="list">
              @for (s of d.styles; track s.styleId) {
                <li class="flex gap-3 px-4 py-3">
                  <a [routerLink]="['/styles', s.styleId]" class="bg-muted flex size-16 shrink-0 items-center justify-center overflow-hidden rounded-md border" [attr.aria-label]="s.styleNo">
                    @if (s.imageUrl || s.sketchUrl) {
                      <img [appAuthSrc]="(s.imageUrl || s.sketchUrl)! + '?w=160'" alt="" class="size-full object-contain" loading="lazy" />
                    } @else {
                      <ng-icon name="lucideImage" class="text-muted-foreground" />
                    }
                  </a>
                  <div class="min-w-0 flex-1 text-sm">
                    <p class="flex flex-wrap items-center gap-2">
                      <a [routerLink]="['/styles', s.styleId]" class="font-mono font-medium underline-offset-2 hover:underline">{{ s.styleNo }}</a>
                      <span class="text-muted-foreground text-xs">{{ s.customerCode }} · {{ s.seasonCode }}</span>
                      @if (s.sameBrand === false) {
                        <span class="rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge('amber')">{{ 'concepts.styles.knowHowOnly' | transloco }}</span>
                      } @else if (s.sameBrand) {
                        <span class="rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge('green')">{{ 'concepts.styles.ownBrand' | transloco }}</span>
                      }
                      @if (s.addedFrom) { <span class="text-muted-foreground text-[11px]">{{ 'concepts.styles.from.' + s.addedFrom | transloco }}</span> }
                    </p>
                    <p class="text-muted-foreground truncate text-xs">{{ s.modelName || s.description || '—' }} · {{ s.productTypeName || '—' }}</p>
                    @if (canEdit()) {
                      <textarea hlmTextarea rows="1" maxlength="500" class="mt-1.5 min-h-9 w-full text-xs" [attr.aria-label]="'concepts.styles.note' | transloco"
                                [placeholder]="'concepts.styles.notePlaceholder' | transloco" [value]="s.note ?? ''" (change)="saveNote(s, $any($event.target).value)"></textarea>
                    } @else if (s.note) {
                      <p class="mt-1 text-xs">{{ s.note }}</p>
                    }
                  </div>
                  @if (canEdit()) {
                    <button hlmBtn variant="ghost" size="icon" type="button" class="size-11 shrink-0 lg:size-9" [attr.aria-label]="'concepts.styles.remove' | transloco"
                            (click)="removeStyle(s)">
                      <ng-icon name="lucideX" />
                    </button>
                  }
                </li>
              }
            </ul>
          } @else {
            <p class="text-muted-foreground px-5 py-8 text-center text-sm">{{ 'concepts.styles.empty' | transloco }}</p>
          }
        </section>

        <!-- Proven library styles to start from: found at once from the concept's fields; Explain with AI is optional -->
        <app-concept-library-matches [conceptId]="c.conceptId" [version]="c.rowVer" [canAdd]="canEdit()" [addedIds]="styleIds()" (add)="addStyles([$event], 'Match')" />

        <!-- Styles offered in packages for this concept -->
        @if (d.packageItems.length) {
          <section hlmCard class="gap-0 py-0">
            <h2 class="flex items-center gap-2 border-b px-5 py-3 font-semibold"><ng-icon name="lucidePackage" />{{ 'concepts.packages.title' | transloco }}</h2>
            <ul class="divide-y text-sm" role="list">
              @for (p of d.packageItems; track p.packageItemId) {
                <li class="flex flex-wrap items-center gap-x-3 gap-y-1 px-5 py-2.5">
                  <a [routerLink]="['/packages', p.packageId]" class="font-medium underline-offset-2 hover:underline">{{ p.packageName }}</a>
                  <span class="text-muted-foreground text-xs">{{ p.customerCode }} · {{ 'packages.status.' + p.packageStatus | transloco }}</span>
                  <a [routerLink]="['/styles', p.styleId]" class="font-mono text-xs underline-offset-2 hover:underline">{{ p.styleNo }}</a>
                  @if (p.outcome) { <span class="text-muted-foreground text-xs">{{ 'packages.outcome.' + p.outcome | transloco }}</span> }
                </li>
              }
            </ul>
          </section>
        }

        <app-concept-form-dialog [open]="editOpen()" [concept]="d" [lookups]="lookups()" [conceptLookups]="conceptLookups()" (closed)="editOpen.set(false)" (saved)="onSaved()" />
        <app-develop-style-dialog [open]="developOpen()" [concept]="d" [lookups]="lookups()" (closed)="developOpen.set(false)" (developed)="onDeveloped($event)" />
        <app-add-to-package-dialog [open]="offerOpen()" [styleIds]="offerIds()" addedFrom="Concept" [conceptId]="c.conceptId"
                                   [customerCode]="c.customerCode" [seasonCode]="c.seasonCode" (closed)="offerOpen.set(false)" (stylesAdded)="load()" />
      }
    </section>
  `,
})
export class ConceptDetailPage implements OnInit {
  private readonly svc = inject(ConceptsService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly canEdit = this.styles.canEdit;
  readonly toneBadge = toneBadge;
  readonly statusTone = CONCEPT_STATUS_TONE;
  readonly closenessClass = CLOSENESS_CLASS;
  readonly conceptId = signal(0);
  readonly detail = signal<ConceptDetail | null>(null);
  readonly closeness = signal<ConceptCloseness | null>(null);
  readonly lookups = signal<StyleLookups | null>(null);
  readonly conceptLookups = signal<ConceptLookups | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly checking = signal(false);
  readonly uploading = signal(false);
  /** Files are being dragged over the pictures card. */
  readonly dragging = signal(false);
  readonly drafting = signal(false);
  readonly editOpen = signal(false);
  readonly offerOpen = signal(false);
  /** The styles Offer in a package adds: the LT designs, one design, or (without designs) the inspiration styles. */
  readonly offerIds = signal<number[]>([]);
  readonly developOpen = signal(false);
  /** The picture whose client setting is being saved. */
  readonly savingImage = signal<number | null>(null);
  readonly styleSearch = signal('');
  readonly found = signal<StyleListItem[]>([]);
  private readonly styleSearch$ = new Subject<string>();

  readonly styleIds = computed(() => (this.detail()?.styles ?? []).map((s) => s.styleId));
  readonly designIds = computed(() => (this.detail()?.developedStyles ?? []).map((s) => s.styleId));

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l) });
    this.svc.lookups().subscribe({ next: (l) => this.conceptLookups.set(l) });
    this.styleSearch$
      .pipe(
        debounceTime(300),
        switchMap((q) => (q.trim().length >= 2 ? this.styles.list({ search: q.trim(), take: 12 }) : of({ items: [], total: 0 }))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (page) => this.found.set(page.items), error: () => this.found.set([]) });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.conceptId.set(Number(params.get('id')) || 0);
      this.detail.set(null);
      this.closeness.set(null);
      this.load(true);
    });
  }

  load(withCheck = false): void {
    this.loading.set(true);
    this.notFound.set(false);
    this.svc.get(this.conceptId()).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
        if (withCheck && d.images.length) this.check();
      },
      error: (e) => {
        this.loading.set(false);
        if (e?.status === 404) this.notFound.set(true);
        else toast.error(styleError(e).message);
      },
    });
  }

  /** Reads pictures not read yet and compares every picture with the library (no AI server: the image model in the API). */
  check(): void {
    if (this.checking()) return;
    this.checking.set(true);
    this.svc.closeness(this.conceptId()).subscribe({
      next: (r) => {
        this.closeness.set(r);
        this.checking.set(false);
      },
      error: (e) => {
        this.checking.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  closenessOf(conceptImageId: number): ConceptImageCloseness | null {
    return this.closeness()?.images.find((i) => i.conceptImageId === conceptImageId) ?? null;
  }

  closenessBanner(level: string | undefined): string {
    if (level === 'Close') return 'bg-red-500/10 text-red-800 dark:text-red-300';
    if (level === 'Check') return 'bg-amber-500/10 text-amber-800 dark:text-amber-300';
    if (level === 'Clear') return 'bg-emerald-500/10 text-emerald-800 dark:text-emerald-300';
    return 'bg-muted/50 text-muted-foreground';
  }

  async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    await this.uploadFiles(files);
  }

  // ----- Drag and drop (editors): files dragged from the desktop onto the pictures card -----

  /** dragenter / dragleave fire for every child element; counting them keeps the highlight steady. */
  private dragDepth = 0;

  private carriesFiles(e: DragEvent): boolean {
    return this.canEdit() && !!e.dataTransfer && Array.from(e.dataTransfer.types).includes('Files');
  }

  onDragEnter(e: DragEvent): void {
    if (!this.carriesFiles(e)) return;
    e.preventDefault();
    this.dragDepth++;
    this.dragging.set(true);
  }

  onDragOver(e: DragEvent): void {
    if (!this.carriesFiles(e)) return;
    e.preventDefault();   // allows the drop
    e.dataTransfer!.dropEffect = this.uploading() ? 'none' : 'copy';
  }

  onDragLeave(e: DragEvent): void {
    if (!this.carriesFiles(e)) return;
    if (--this.dragDepth <= 0) {
      this.dragDepth = 0;
      this.dragging.set(false);
    }
  }

  async onDrop(e: DragEvent): Promise<void> {
    if (!this.carriesFiles(e)) return;
    e.preventDefault();
    this.dragDepth = 0;
    this.dragging.set(false);
    const all = Array.from(e.dataTransfer!.files);
    const images = all.filter((f) => IMAGE_TYPES.includes(f.type));
    if (images.length < all.length) toast.error(this.transloco.translate('concepts.pictures.notImages', { count: all.length - images.length }));
    await this.uploadFiles(images);
  }

  private async uploadFiles(files: File[]): Promise<void> {
    if (!files.length || this.uploading()) return;
    this.uploading.set(true);
    try {
      for (const file of files) {
        const blob = await compressImage(file, MAX_DIMENSION);
        await new Promise<void>((resolve) =>
          this.svc.addImage(this.conceptId(), blob, file.name.replace(/\.[^.]+$/, '') + '.jpg', null).subscribe({
            next: () => resolve(),
            error: (e) => {
              toast.error(styleError(e).message);
              resolve();
            },
          }),
        );
      }
    } catch (e) {
      toast.error(problemMessage(e));
    } finally {
      this.uploading.set(false);
      this.load(true);
    }
  }

  async removeImage(conceptImageId: number): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('concepts.pictures.removeTitle'), message: this.transloco.translate('concepts.pictures.removeMessage'),
      confirmLabel: this.transloco.translate('concepts.pictures.remove'), cancelLabel: this.transloco.translate('actions.cancel'), variant: 'danger',
    });
    if (!ok) return;
    this.svc.removeImage(this.conceptId(), conceptImageId).subscribe({
      next: () => {
        this.closeness.update((c) => (c ? { ...c, images: c.images.filter((i) => i.conceptImageId !== conceptImageId) } : c));
        this.load();
      },
      error: (e) => toast.error(styleError(e).message),
    });
  }

  /** Shows the picture on the story page for the client; the API refuses one that looks like another brand's design. */
  setForClient(conceptImageId: number, forClient: boolean, caption: string | null): void {
    this.savingImage.set(conceptImageId);
    this.svc.saveImage(this.conceptId(), conceptImageId, forClient, caption).subscribe({
      next: () => {
        this.savingImage.set(null);
        this.detail.update((d) => (d ? { ...d, images: d.images.map((x) => (x.conceptImageId === conceptImageId ? { ...x, forClient } : x)) } : d));
      },
      error: (e) => {
        this.savingImage.set(null);
        toast.error(styleError(e).message);
        this.load();   // puts the box back as saved
      },
    });
  }

  draftBrief(): void {
    if (this.drafting()) return;
    this.drafting.set(true);
    this.svc.draftBrief(this.conceptId()).subscribe({
      next: (r) => {
        this.drafting.set(false);
        this.detail.update((d) => (d ? { ...d, concept: { ...d.concept, brief: r.brief, rowVer: r.rowVer } } : d));
      },
      error: (e) => {
        this.drafting.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }

  hasStyle(styleId: number): boolean {
    return this.styleIds().includes(styleId);
  }

  onStyleSearch(value: string): void {
    this.styleSearch.set(value);
    this.styleSearch$.next(value);
  }

  pickStyle(s: StyleListItem): void {
    this.styleSearch.set('');
    this.found.set([]);
    this.addStyles([s.styleId], 'Pick');
  }

  addStyles(styleIds: number[], from: ConceptStyleSource): void {
    this.svc.addStyles(this.conceptId(), styleIds, from).subscribe({
      next: (r) => {
        const added = r.filter((x) => x.result === 'Added');
        if (added.length) toast.success(this.transloco.translate('concepts.styles.added', { styles: added.map((x) => x.styleNo).join(', ') }));
        this.load();
      },
      error: (e) => toast.error(styleError(e).message),
    });
  }

  saveNote(s: ConceptStyle, value: string): void {
    const note = value.trim() || null;
    if (note === (s.note ?? null)) return;
    this.svc.saveStyleNote(this.conceptId(), s.styleId, note).subscribe({
      next: () => this.detail.update((d) => (d ? { ...d, styles: d.styles.map((x) => (x.styleId === s.styleId ? { ...x, note } : x)) } : d)),
      error: (e) => toast.error(styleError(e).message),
    });
  }

  removeStyle(s: ConceptStyle): void {
    this.svc.removeStyle(this.conceptId(), s.styleId).subscribe({ next: () => this.load(), error: (e) => toast.error(styleError(e).message) });
  }

  offer(styleIds: number[]): void {
    this.offerIds.set(styleIds);
    this.offerOpen.set(true);
  }

  onDeveloped(styleId: number): void {
    this.developOpen.set(false);
    toast.success(this.transloco.translate('concepts.develop.done'));
    this.load();
    this.offer([styleId]);
  }

  onSaved(): void {
    this.editOpen.set(false);
    this.load(true);
  }

  async deleteConcept(): Promise<void> {
    const c = this.detail()?.concept;
    if (!c) return;
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('concepts.deleteTitle'), message: this.transloco.translate('concepts.deleteMessage', { name: c.name }),
      confirmLabel: this.transloco.translate('concepts.delete'), cancelLabel: this.transloco.translate('actions.cancel'), variant: 'danger',
    });
    if (!ok) return;
    this.svc.delete(c.conceptId, c.rowVer).subscribe({
      next: () => void this.router.navigate(['/concepts']),
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  genderLabel = (code: string) => (GENDERS.some((g) => g.code === code) ? `styles.gender.${code}` : code);
}
