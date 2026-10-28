import { DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight, lucideImage, lucideImageUp, lucidePackagePlus, lucideScanEye, lucideSearch, lucideSparkles, lucideSquare, lucideTriangleAlert, lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { catchError, firstValueFrom, Observable, of } from 'rxjs';
import { problemMessage } from '../../core/auth/auth.service';
import { AddToPackageDialog } from '../style-packages/add-to-package-dialog';
import { StyleLibraryService } from '../style-library/style-library.service';
import { AiProviderBanner } from './ai-provider-banner';
import { AiStudioService, ImageSearchResult, ImageSearchStatus } from './ai-studio.service';

/** Pause between index calls when indexing everything (the server works about 45 s per call). */
const PAUSE_MS = 500;
const MAX_PHOTO_BYTES = 10 * 1024 * 1024;

/**
 * AI Studio > Image search: find library styles by an uploaded photo, by words, or by another style's picture
 * (?styleId=..; ?q=.. searches those words). Two indexes, both inside LT: look-alike vectors from the image model built into the API (photo, more
 * like this; under a second per picture) and descriptions from the in-house AI server (Vision + Embedding jobs; words).
 * Editors build both from this page with one button; uploaded photos are read and dropped, never stored.
 */
@Component({
  selector: 'app-image-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, PercentPipe, RouterLink, NgIcon, AuthImgDirective, AiProviderBanner, AddToPackageDialog, HlmBadgeImports, HlmButtonImports, HlmCardImports,
    HlmInputImports, HlmNativeSelectImports, HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [
    provideIcons({ lucideArrowRight, lucideImage, lucideImageUp, lucidePackagePlus, lucideScanEye, lucideSearch, lucideSparkles, lucideSquare, lucideTriangleAlert, lucideX }),
  ],
  template: `
    <section class="flex flex-col gap-5">
      <header>
        <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideScanEye" />{{ 'ai.imageSearch.title' | transloco }}</h1>
        <p class="text-muted-foreground mt-1 text-sm">{{ 'ai.imageSearch.subtitle' | transloco }}</p>
      </header>
      <app-ai-provider-banner uses="imageSearch" />

      <!-- Index -->
      @if (status(); as s) {
        <section hlmCard class="gap-3 p-5">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <h2 class="font-medium">{{ 'ai.imageSearch.index.title' | transloco }}</h2>
            @if (canEdit()) {
              <div class="flex flex-wrap gap-2">
                @if (indexing()) {
                  <button hlmBtn variant="outline" class="h-11 lg:h-9" (click)="stop()">
                    <ng-icon name="lucideSquare" />{{ 'ai.imageSearch.index.stop' | transloco }}
                  </button>
                } @else {
                  <button hlmBtn class="h-11 lg:h-9" [disabled]="!toIndex()" (click)="index()">
                    <ng-icon name="lucideSparkles" />{{ 'ai.imageSearch.index.run' | transloco: { count: (toIndex() | number) } }}
                  </button>
                }
              </div>
            }
          </div>
          @for (row of rows(); track row.key) {
            <div class="flex flex-col gap-1.5">
              <div class="flex flex-wrap items-baseline justify-between gap-x-3 text-sm">
                <span class="font-medium">{{ row.title | transloco }}</span>
                <span class="text-muted-foreground text-xs tabular-nums">
                  {{ 'ai.imageSearch.index.summary' | transloco: { indexed: (row.indexed | number), pictures: (row.pictures | number) } }}
                  @if (row.failed) {
                    · <span class="text-amber-700 dark:text-amber-400">{{ 'ai.imageSearch.index.failed' | transloco: { count: row.failed } }}</span>
                  }
                </span>
              </div>
              @if (row.pictures) {
                <div class="bg-muted h-2 overflow-hidden rounded-full" role="progressbar" [attr.aria-valuenow]="row.indexed" aria-valuemin="0" [attr.aria-valuemax]="row.pictures">
                  <div class="h-full transition-all" [class]="row.ready ? 'bg-primary' : 'bg-muted-foreground/40'" [style.width.%]="(row.indexed / row.pictures) * 100"></div>
                </div>
              }
              <p class="text-muted-foreground text-xs">
                {{ row.what | transloco }}
                @if (!row.ready) {
                  <span [class]="row.optional ? '' : 'text-amber-700 dark:text-amber-400'">{{ row.why | transloco }}</span>
                }
              </p>
            </div>
          }
          @if (indexing() && progress(); as p) {
            <p class="text-muted-foreground flex items-center gap-2 text-xs"><hlm-spinner class="size-3.5" />{{ 'ai.imageSearch.index.progress' | transloco: p }}</p>
          } @else if (indexing()) {
            <p class="text-muted-foreground flex items-center gap-2 text-xs"><hlm-spinner class="size-3.5" />{{ 'ai.imageSearch.index.working' | transloco }}</p>
          }
          @if (!s.pictures) {
            <p class="text-muted-foreground text-xs">{{ 'ai.imageSearch.index.noPictures' | transloco }}</p>
          } @else if (canEdit() && toIndex() && !indexing()) {
            <p class="text-muted-foreground text-xs">{{ 'ai.imageSearch.index.hint' | transloco }}</p>
          }
        </section>
      }

      <!-- Search -->
      <section hlmCard class="gap-4 p-5">
        <div class="flex flex-wrap items-center gap-2">
          <div class="bg-muted inline-flex rounded-md p-1" role="tablist">
            @for (m of modes; track m) {
              <button type="button" role="tab" class="rounded px-3 py-1.5 text-sm" [attr.aria-selected]="mode() === m"
                [class]="mode() === m ? 'bg-background shadow-sm font-medium' : 'text-muted-foreground'" (click)="mode.set(m)">
                {{ 'ai.imageSearch.mode.' + m | transloco }}
              </button>
            }
          </div>
          <hlm-native-select selectId="image-search-customer" selectClass="h-11 w-full lg:h-9 sm:w-48" class="sm:ml-auto"
            [attr.aria-label]="'styles.allCustomers' | transloco" [value]="customer()" (valueChange)="customer.set($event ?? '')">
            <option hlmNativeSelectOption value="" [selected]="customer() === ''">{{ 'styles.allCustomers' | transloco }}</option>
            @for (c of customers(); track c.code) {
              <option hlmNativeSelectOption [value]="c.code" [selected]="c.code === customer()">{{ c.name }}</option>
            }
          </hlm-native-select>
        </div>

        @if (mode() === 'photo') {
          <label
            class="hover:bg-accent/40 flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed p-6 text-center transition-colors"
            [class.border-primary]="dragging()"
            (dragover)="$event.preventDefault(); dragging.set(true)"
            (dragleave)="dragging.set(false)"
            (drop)="drop($event)"
          >
            @if (preview(); as url) {
              <img [src]="url" alt="" class="max-h-56 rounded-md object-contain" />
              <span class="text-muted-foreground text-xs">{{ 'ai.imageSearch.photo.change' | transloco }}</span>
            } @else {
              <ng-icon name="lucideImageUp" class="text-muted-foreground size-8" />
              <span class="text-sm">{{ 'ai.imageSearch.photo.drop' | transloco }}</span>
              <span class="text-muted-foreground text-xs">{{ 'ai.imageSearch.photo.notStored' | transloco }}</span>
            }
            <input type="file" accept="image/jpeg,image/png,image/webp,image/gif" class="sr-only" (change)="pick($event)" />
          </label>
          <div class="flex justify-end">
            <button hlmBtn class="h-11 lg:h-10" [disabled]="busy() || !photo()" (click)="searchPhoto()">
              @if (busy()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideSearch" /> }
              {{ 'ai.imageSearch.photo.run' | transloco }}
            </button>
          </div>
        } @else {
          <form class="flex flex-col gap-2 sm:flex-row" (submit)="searchWords($event)">
            <input hlmInput class="h-11 w-full lg:h-10" maxlength="300" [attr.aria-label]="'ai.imageSearch.words.label' | transloco"
              [placeholder]="'ai.imageSearch.words.placeholder' | transloco" [value]="words()" (input)="words.set($any($event.target).value)" />
            <button hlmBtn type="submit" class="h-11 shrink-0 lg:h-10" [disabled]="busy() || words().trim().length < 3">
              @if (busy()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideSearch" /> }
              {{ 'ai.imageSearch.words.run' | transloco }}
            </button>
          </form>
        }
        @if (searchRoute(); as rt) {
          <label class="flex items-start gap-2 text-sm" [class.opacity-60]="!rt.choice">
            <input type="checkbox" class="mt-0.5 size-4" [checked]="rt.aiServer" [disabled]="!rt.choice" (change)="useAiServer.set($any($event.target).checked)" />
            <span>
              {{ 'ai.imageSearch.useAiServer.label' | transloco }}
              <span class="text-muted-foreground block text-xs">{{ 'ai.imageSearch.useAiServer.' + rt.note | transloco }}</span>
            </span>
          </label>
        }
        <p class="text-muted-foreground text-xs">{{ 'ai.imageSearch.guardrail' | transloco }}</p>
      </section>

      <!-- Results -->
      @if (result(); as r) {
        <p class="text-muted-foreground text-xs">{{ 'ai.imageSearch.method.' + r.method | transloco }}</p>
        @if (r.reading; as read) {
          <section hlmCard class="gap-2 p-5">
            <p class="text-sm"><span class="text-muted-foreground">{{ 'ai.imageSearch.readAs' | transloco }}</span> <strong>{{ read.garment || '—' }}</strong></p>
            <div class="flex flex-wrap gap-1.5">
              @for (c of read.colours; track c) { <span hlmBadge variant="secondary">{{ c }}</span> }
              @for (d of read.details; track d) { <span hlmBadge variant="outline">{{ d }}</span> }
            </div>
            @if (read.description) { <p class="text-muted-foreground text-xs">{{ read.description }}</p> }
          </section>
        }
        @if (r.mode === 'similar' && similarTo()) {
          <p class="text-sm">{{ 'ai.imageSearch.similarTo' | transloco: { styleNo: similarTo() } }}</p>
        }
        @if (r.weak) {
          <p class="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-300">
            {{ 'ai.imageSearch.weak' | transloco }}
          </p>
        }
        @if (r.matches.length) {
          @if (canEdit()) {
            <!-- Style Packages: every style found at once (another brand's style is offered as a copy in the dialog) -->
            <button hlmBtn variant="outline" type="button" class="h-11 self-start lg:h-9" (click)="addToPackage(r.matches)">
              <ng-icon name="lucidePackagePlus" />{{ 'packages.pick.addAllFound' | transloco: { count: r.matches.length } }}
            </button>
          }
          <ul class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4" role="list">
            @for (m of r.matches; track m.styleId) {
              <li hlmCard class="gap-0 overflow-hidden py-0">
                <a [routerLink]="['/styles', m.styleId]" class="bg-muted relative block aspect-[4/3]">
                  <img [appAuthSrc]="m.imageUrl" alt="" class="size-full object-contain" loading="lazy" />
                  @if (!(r.mode === 'words' && r.method === 'lookAlike')) {
                    <!-- Word-to-picture scores are low even for good matches: their order counts, not the number. -->
                    <span hlmBadge class="absolute top-2 right-2">{{ m.similarity | percent: '1.0-0' }}</span>
                  }
                  <span hlmBadge variant="secondary" class="absolute top-2 left-2">
                    {{ 'ai.imageSearch.source.' + m.source | transloco }}{{ m.colorwayCode ? ' ' + m.colorwayCode : '' }}
                  </span>
                </a>
                <div class="flex flex-1 flex-col gap-1 p-3">
                  <a [routerLink]="['/styles', m.styleId]" class="truncate font-mono text-sm font-medium hover:underline">{{ m.styleNo }}</a>
                  <span class="text-muted-foreground truncate text-xs">{{ m.customerCode }} · {{ m.seasonCode }} · {{ m.modelName || m.productTypeName || '—' }}</span>
                  @if (m.garment) { <span class="line-clamp-2 text-xs">{{ m.garment }}{{ m.colours ? ' — ' + m.colours : '' }}</span> }
                  <div class="mt-auto flex flex-wrap gap-1">
                    <button hlmBtn variant="ghost" size="sm" class="h-9 px-2" (click)="similar(m.styleId, m.styleNo)">
                      {{ 'ai.imageSearch.moreLikeThis' | transloco }}<ng-icon name="lucideArrowRight" />
                    </button>
                    @if (canEdit()) {
                      <button hlmBtn variant="ghost" size="sm" class="h-9 px-2" (click)="addToPackage([m])">
                        <ng-icon name="lucidePackagePlus" />{{ 'packages.pick.addToPackage' | transloco }}
                      </button>
                    }
                  </div>
                </div>
              </li>
            }
          </ul>
        } @else {
          <p class="text-muted-foreground rounded-md border border-dashed p-6 text-center text-sm">
            {{ (r.mode === 'similar' ? 'ai.imageSearch.noSimilar' : 'ai.imageSearch.noMatches') | transloco }}
          </p>
        }
      }
    </section>

    @if (packageStyles(); as p) {
      <app-add-to-package-dialog [open]="true" [styleIds]="p.styleIds" addedFrom="ImageSearch" [customerCode]="p.customerCode" [seasonCode]="p.seasonCode"
                                 (closed)="packageStyles.set(null)" />
    }
  `,
})
export class ImageSearch implements OnInit {
  private readonly svc = inject(AiStudioService);
  private readonly styles = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly canEdit = this.styles.canEdit;
  readonly modes = ['photo', 'words'] as const;
  /** Styles being added to a style package, with the customer and season they share (to start a new package with). */
  readonly packageStyles = signal<{ styleIds: number[]; customerCode: string | null; seasonCode: string | null } | null>(null);

  addToPackage(matches: { styleId: number; customerCode: string; seasonCode: string }[]): void {
    const one = (values: string[]) => (new Set(values).size === 1 ? values[0] : null);
    this.packageStyles.set({
      styleIds: [...new Set(matches.map((m) => m.styleId))],
      customerCode: one(matches.map((m) => m.customerCode)),
      seasonCode: one(matches.map((m) => m.seasonCode)),
    });
  }
  private readonly lookups = toSignal(this.styles.lookups().pipe(catchError(() => of(null))), { initialValue: null });
  readonly customers = computed(() => this.lookups()?.customers ?? []);

  readonly status = signal<ImageSearchStatus | null>(null);
  /** Descriptions can be made: the Vision and Embedding jobs are set up. */
  readonly jobsReady = computed(() => !!this.status()?.vision.isConfigured && !!this.status()?.embedding.isConfigured);

  /** Pictures the next Index click would work on: look-alike vectors when the built-in model runs, descriptions when the jobs do. */
  readonly toIndex = computed(() => {
    const s = this.status();
    if (!s) return 0;
    return (s.lookAlike.isAvailable ? s.lookAlike.toIndex : 0) + (this.jobsReady() ? s.toIndex : 0);
  });

  /** The two kinds of index: look-alike (built-in model; photo, more like this) and descriptions (in-house AI server; words). */
  readonly rows = computed(() => {
    const s = this.status();
    if (!s) return [];
    const l = s.lookAlike;
    const k = 'ai.imageSearch.index.';
    return [
      { key: 'lookAlike', title: k + 'lookAlike.title', what: k + (l.supportsWords ? 'lookAlike.whatWords' : 'lookAlike.what'),
        pictures: l.pictures, indexed: l.indexed, failed: l.failed, ready: l.isAvailable,
        why: l.problem === 'aiOff' ? k + 'aiOff' : k + 'lookAlike.notInstalled', optional: false },
      // Optional: search by photo and by words work with the built-in model; descriptions are for a (GPU) AI server later.
      { key: 'descriptions', title: k + 'descriptions.titleOptional', what: k + 'descriptions.what',
        pictures: s.pictures, indexed: s.indexed, failed: s.failed, ready: this.jobsReady(),
        why: k + 'descriptions.optional', optional: true },
    ];
  });
  readonly mode = signal<'photo' | 'words'>('photo');
  readonly customer = signal('');
  readonly words = signal('');
  /** The user ticked "Use the AI server" (only applies when both ways can run). */
  readonly useAiServer = signal(false);

  /**
   * How the current search mode will run. aiServer: the request asks for the descriptions. choice: both ways can run,
   * so the tickbox is enabled. note: the hint under the tickbox.
   */
  readonly searchRoute = computed(() => {
    const s = this.status();
    if (!s) return null;
    const builtIn = this.mode() === 'photo' ? s.lookAlike.isAvailable : s.lookAlike.supportsWords;
    const aiServer = this.mode() === 'photo' ? this.jobsReady() : s.embedding.isConfigured;
    if (builtIn && aiServer) return { aiServer: this.useAiServer(), choice: true, note: this.useAiServer() ? 'slower' : 'faster' };
    if (aiServer) return { aiServer: true, choice: false, note: 'onlyAiServer' };
    return { aiServer: false, choice: false, note: builtIn ? 'onlyBuiltIn' : 'neither' };
  });
  readonly photo = signal<File | null>(null);
  readonly preview = signal<string | null>(null);
  readonly dragging = signal(false);
  readonly busy = signal(false);
  readonly result = signal<ImageSearchResult | null>(null);
  readonly similarTo = signal<string | null>(null);

  readonly indexing = signal(false);
  readonly progress = signal<{ done: number; remaining: number } | null>(null);
  private stopRequested = false;

  ngOnInit(): void {
    this.loadStatus();
    this.destroyRef.onDestroy(() => {
      this.stopRequested = true;
      this.revokePreview();
    });
    // /ai/image-search?styleId=.. opens "more like this" for that style (e.g. from the style page).
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      const id = Number(p.get('styleId'));
      if (id > 0) this.runSimilar(id, p.get('styleNo'));
      // /ai/image-search?q=.. (e.g. from Smart search) searches those words.
      const q = p.get('q')?.trim();
      if (q && q.length >= 3 && id <= 0) {
        this.mode.set('words');
        this.words.set(q.slice(0, 300));
        this.searchWords();
      }
    });
  }

  private loadStatus(): void {
    this.svc.imageSearchStatus().subscribe({ next: (s) => this.status.set(s), error: (e) => toast.error(problemMessage(e)) });
  }

  // ----- Index -----

  async index(): Promise<void> {
    if (this.indexing()) return;
    this.indexing.set(true);
    this.stopRequested = false;
    this.progress.set(null);
    let done = 0;
    let missing = 0;
    try {
      for (;;) {
        const r = await firstValueFrom(this.svc.indexImages());
        done += r.indexed + r.failed;
        missing = r.missing;
        this.progress.set({ done, remaining: r.remaining });
        this.loadStatus();
        // No progress = finished, or only pictures whose file is missing are left (they stay to do).
        if (r.indexed + r.failed === 0 || r.remaining === 0 || this.stopRequested) break;
        await new Promise((resolve) => setTimeout(resolve, PAUSE_MS));
        if (this.stopRequested) break;
      }
      toast.success(this.transloco.translate('ai.imageSearch.index.done', { count: done }));
      if (missing) toast.warning(this.transloco.translate('ai.imageSearch.index.missing', { count: missing }));
    } catch (e) {
      toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
    } finally {
      this.indexing.set(false);
      this.loadStatus();
    }
  }

  stop(): void {
    this.stopRequested = true;
  }

  // ----- Search -----

  pick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.setPhoto(input.files?.[0] ?? null);
    input.value = '';
  }

  drop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.setPhoto(event.dataTransfer?.files?.[0] ?? null);
  }

  private setPhoto(file: File | null): void {
    if (!file) return;
    if (!file.type.startsWith('image/') || file.size > MAX_PHOTO_BYTES) {
      toast.error(this.transloco.translate('ai.imageSearch.photo.invalid'));
      return;
    }
    this.revokePreview();
    this.photo.set(file);
    this.preview.set(URL.createObjectURL(file));
  }

  private revokePreview(): void {
    const url = this.preview();
    if (url) URL.revokeObjectURL(url);
  }

  searchPhoto(): void {
    const file = this.photo();
    if (file) this.run(this.svc.searchImagesByPhoto(file, this.customer() || null, !!this.searchRoute()?.aiServer));
  }

  searchWords(event?: Event): void {
    event?.preventDefault();
    const q = this.words().trim();
    if (q.length >= 3) this.run(this.svc.searchImagesByWords(q, this.customer() || null, !!this.searchRoute()?.aiServer));
  }

  /** "More like this": keeps the address shareable (?styleId=..). */
  similar(styleId: number, styleNo: string): void {
    void this.router.navigate([], { queryParams: { styleId, styleNo }, replaceUrl: false });
  }

  private runSimilar(styleId: number, styleNo: string | null): void {
    this.similarTo.set(styleNo);
    this.run(this.svc.similarImages(styleId, this.customer() || null));
  }

  private run(call: Observable<ImageSearchResult>): void {
    if (this.busy()) return;
    this.busy.set(true);
    call.subscribe({
      next: (r) => {
        if (r.mode !== 'similar') this.similarTo.set(null);
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
