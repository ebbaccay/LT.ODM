import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCopy, lucideImage, lucideInfo, lucideLightbulb, lucidePackagePlus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { AddToPackageDialog } from './add-to-package-dialog';
import { Offer, OfferSection, OfferSuggestion, PackagesService } from './packages.service';

const SECTIONS: OfferSection[] = ['Repitch', 'Gap', 'Fabric'];

/**
 * Style Packages > What to offer (roadmap 5.14): for a customer's brand, from the library alone (rules, no AI) - its own
 * proven styles not carried over since, garments it has rarely bought with LT's best examples, and other brands' styles on
 * fabrics it already buys. Other brands' styles are offered as copies (the Add to package dialog makes them). The customer
 * and an optional package (?customer=..&package=..) are kept in the address; a package's own styles are left out.
 */
@Component({
  selector: 'app-offer-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, NgIcon, AuthImgDirective, AddToPackageDialog, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports,
    HlmNativeSelectImports, HlmSkeletonImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideArrowLeft, lucideCopy, lucideImage, lucideInfo, lucideLightbulb, lucidePackagePlus })],
  template: `
    <section class="flex flex-col gap-5" [attr.aria-busy]="loading()">
      @if (packageId()) {
        <a [routerLink]="['/packages', packageId()]" class="text-muted-foreground hover:text-foreground flex w-fit items-center gap-1 text-sm">
          <ng-icon name="lucideArrowLeft" />{{ 'packages.offer.backToPackage' | transloco }}
        </a>
      }
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideLightbulb" />{{ 'packages.offer.title' | transloco }}</h1>
          <p class="text-muted-foreground mt-1 max-w-3xl text-sm">{{ 'packages.offer.subtitle' | transloco }}</p>
        </div>
        <hlm-native-select selectId="offer-customer" selectClass="h-11 w-full sm:w-64 lg:h-9" [attr.aria-label]="'packages.form.customer' | transloco"
                           [value]="customer()" (valueChange)="setCustomer($event ?? '')">
          <option hlmNativeSelectOption value="">{{ 'packages.offer.chooseCustomer' | transloco }}</option>
          @for (c of lookups()?.customers ?? []; track c.code) { <option hlmNativeSelectOption [value]="c.code">{{ c.code === c.name ? c.code : c.code + ' · ' + c.name }}</option> }
        </hlm-native-select>
      </header>

      @if (!customer()) {
        <section hlmCard class="items-center gap-2 px-5 py-14 text-center">
          <ng-icon name="lucideLightbulb" class="text-muted-foreground text-4xl" />
          <p class="font-medium">{{ 'packages.offer.pickCustomer' | transloco }}</p>
        </section>
      } @else if (loading() && !offer()) {
        <div hlmSkeleton class="h-28 w-full"></div>
        <div hlmSkeleton class="h-72 w-full"></div>
      } @else if (offer(); as o) {
        @if (o.profile; as p) {
          <section hlmCard class="gap-3 p-5">
            <p class="text-sm">
              {{ 'packages.offer.profile' | transloco: { brand: p.brand, styles: (p.styles | number), accounts: p.accounts, first: p.firstSeason ?? '—', latest: p.latestSeason ?? '—' } }}
            </p>
            <div class="flex flex-wrap gap-1.5">
              @for (c of o.categories; track c.category) {
                <span hlmBadge variant="secondary" [title]="'packages.offer.latestIn' | transloco: { season: c.latestSeason ?? '—' }">
                  {{ c.category }} · {{ c.styles }}@if (c.offered) {<span class="ml-1 text-emerald-700 dark:text-emerald-400">· {{ 'packages.offer.won' | transloco: { won: c.won, offered: c.offered } }}</span>}
                </span>
              }
            </div>
          </section>
        } @else {
          <p class="text-muted-foreground rounded-md border border-dashed p-6 text-center text-sm">{{ 'packages.offer.noStyles' | transloco }}</p>
        }

        @for (section of sections; track section) {
          @let items = bySection()[section];
          <section class="flex flex-col gap-3">
            <div>
              <h2 class="text-lg font-semibold">{{ 'packages.offer.section.' + section + '.title' | transloco }}</h2>
              <p class="text-muted-foreground text-sm">{{ 'packages.offer.section.' + section + '.hint' | transloco }}</p>
            </div>
            @if (!items.length) {
              <p class="text-muted-foreground rounded-md border border-dashed p-4 text-center text-sm">{{ 'packages.offer.none' | transloco }}</p>
            } @else {
              <ul class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4" role="list">
                @for (s of items; track s.styleId) {
                  <li hlmCard class="flex-row gap-3 p-3" [class.ring-primary]="picked().has(s.styleId)" [class.ring-2]="picked().has(s.styleId)">
                    @if (canEdit()) {
                      <hlm-checkbox class="mt-1" [inputId]="'offer-' + s.styleId" [checked]="picked().has(s.styleId)" (checkedChange)="toggle(s)"
                                    [attr.aria-label]="'packages.pick.one' | transloco: { styleNo: s.styleNo }" />
                    }
                    <a [routerLink]="['/styles', s.styleId]" class="bg-muted flex size-20 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                      @let thumb = thumbFor(s);
                      @if (thumb) {
                        <img [appAuthSrc]="thumb" alt="" class="size-full object-cover" loading="lazy" (loadError)="markBroken(thumb)" />
                      } @else {
                        <ng-icon name="lucideImage" class="text-muted-foreground" />
                      }
                    </a>
                    <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                      <a [routerLink]="['/styles', s.styleId]" class="truncate font-mono text-sm font-medium underline-offset-2 hover:underline">{{ s.styleNo }}</a>
                      <span class="text-muted-foreground truncate text-xs">{{ s.customerCode }} · {{ s.seasonCode }}{{ s.category ? ' · ' + s.category : '' }}</span>
                      <span class="truncate text-xs">{{ s.modelName || s.productTypeName || s.description || '—' }}</span>
                      <span class="text-muted-foreground text-xs">{{ reasonKey(s) | transloco: { d1: s.detail1 ?? 0, d2: s.detail2 ?? 0, category: s.category ?? '' } }}</span>
                      @if (s.categoryOffered) {
                        <span class="text-xs" [class]="s.categoryWon ? 'text-emerald-700 dark:text-emerald-400' : 'text-muted-foreground'">
                          {{ 'packages.offer.record' | transloco: { won: s.categoryWon, offered: s.categoryOffered, category: s.category ?? '' } }}
                        </span>
                      }
                      @if (!s.sameBrand) {
                        <span class="mt-0.5 flex items-center gap-1 text-xs text-amber-700 dark:text-amber-400"><ng-icon name="lucideCopy" />{{ 'packages.offer.asCopy' | transloco }}</span>
                      }
                    </div>
                  </li>
                }
              </ul>
            }
          </section>
        }
        <p class="text-muted-foreground flex items-start gap-1.5 text-xs"><ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />{{ 'packages.offer.rules' | transloco }}</p>
      }
    </section>

    @if (canEdit() && picked().size) {
      <div class="floating-bar bg-card ring-foreground/10 sticky bottom-4 z-20 mx-auto mt-4 flex w-fit max-w-full flex-wrap items-center gap-3 rounded-xl px-4 py-2 shadow-lg ring-1">
        <span class="text-sm font-medium">{{ 'packages.pick.selected' | transloco: { count: picked().size } }}</span>
        <button hlmBtn variant="ghost" type="button" class="h-11 lg:h-9" (click)="picked.set(emptyPicks)">{{ 'styles.clear' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" (click)="packageOpen.set(true)"><ng-icon name="lucidePackagePlus" />{{ 'packages.pick.addToPackage' | transloco }}</button>
      </div>
    }
    @if (packageOpen()) {
      <app-add-to-package-dialog [open]="true" [styleIds]="pickedIds()" addedFrom="Offer" [customerCode]="customer()" [packageId]="packageId()"
                                 (closed)="closePackageDialog()" (stylesAdded)="added = true" />
    }
  `,
})
export class OfferPage implements OnInit {
  private readonly svc = inject(PackagesService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly canEdit = this.styles.canEdit;
  readonly sections = SECTIONS;
  readonly emptyPicks: ReadonlySet<number> = new Set();
  readonly lookups = signal<StyleLookups | null>(null);
  readonly customer = signal('');
  readonly packageId = signal<number | null>(null);
  readonly offer = signal<Offer | null>(null);
  readonly loading = signal(false);
  readonly picked = signal<ReadonlySet<number>>(new Set());
  readonly packageOpen = signal(false);
  readonly broken = signal<ReadonlySet<string>>(new Set());
  /** Styles were added from the open dialog: the picks are cleared and the suggestions reloaded when it closes. */
  added = false;

  readonly pickedIds = computed(() => [...this.picked()]);
  readonly bySection = computed(() => {
    const all = this.offer()?.suggestions ?? [];
    return Object.fromEntries(SECTIONS.map((s) => [s, all.filter((x) => x.section === s)])) as Record<OfferSection, OfferSuggestion[]>;
  });

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: (e) => toast.error(styleError(e).message) });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.customer.set(params.get('customer') ?? '');
      this.packageId.set(Number(params.get('package')) || null);
      this.picked.set(new Set());
      this.load();
    });
  }

  setCustomer(code: string): void {
    // Another customer: the package (if any) is for the old one, so it is dropped.
    void this.router.navigate([], { queryParams: { customer: code || null, package: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  load(): void {
    const customer = this.customer();
    this.offer.set(null);
    if (!customer) return;
    this.loading.set(true);
    this.svc.offer(customer, this.packageId()).subscribe({
      next: (o) => {
        this.offer.set(o);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  toggle(s: OfferSuggestion): void {
    this.picked.update((p) => {
      const next = new Set(p);
      if (!next.delete(s.styleId)) next.add(s.styleId);
      return next;
    });
  }

  closePackageDialog(): void {
    this.packageOpen.set(false);
    if (!this.added) return;
    this.added = false;
    this.picked.set(new Set());
    this.load();
  }

  /** The reason line: why this style is suggested (translation key; d1 / d2 / category fill it). */
  reasonKey(s: OfferSuggestion): string {
    return `packages.offer.reason.${s.section}`;
  }

  thumbFor(s: OfferSuggestion): string | null {
    const broken = this.broken();
    return [s.imageUrl, s.sketchUrl].find((u): u is string => !!u && !broken.has(u)) ?? null;
  }

  markBroken(url: string): void {
    this.broken.update((b) => new Set(b).add(url));
  }
}
