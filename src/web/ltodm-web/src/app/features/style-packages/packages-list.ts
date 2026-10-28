import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChartColumn, lucideImage, lucideLightbulb, lucidePackage, lucidePlus, lucideSearch, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { toneBadge } from '@tms/shared/ui/tones';
import { Subject, debounceTime } from 'rxjs';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { PackageFormDialog } from './package-form-dialog';
import { PACKAGE_STATUSES, PackageListItem, PackagesService, STATUS_TONE } from './packages.service';

const PAGE = 48;
const FILTER_KEYS = ['search', 'customer', 'season', 'status', 'mine'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

/**
 * Style Packages: the offers of library styles to clients, latest change first, with search and filters kept in the address.
 * Admins and merchandisers start a package here or from the Styles list / a style page ("Add to package").
 */
@Component({
  selector: 'app-packages-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, NgIcon, AuthImgDirective, PackageFormDialog, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports,
    HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideChartColumn, lucideImage, lucideLightbulb, lucidePackage, lucidePlus, lucideSearch, lucideX })],
  template: `
    <section class="flex flex-col gap-5" [attr.aria-busy]="loading()">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight">
            {{ 'packages.title' | transloco }}
            @if (total()) { <span hlmBadge variant="secondary">{{ total() | number }}</span> }
          </h1>
          <p class="text-muted-foreground mt-1 text-sm">{{ 'packages.subtitle' | transloco }}</p>
        </div>
        <div class="flex flex-wrap gap-2">
          <a hlmBtn variant="outline" class="h-11 lg:h-9" routerLink="/packages/hit-rate" [queryParams]="{ customer: filter().customer || null }">
            <ng-icon name="lucideChartColumn" />{{ 'packages.hitRate.title' | transloco }}
          </a>
          <a hlmBtn variant="outline" class="h-11 lg:h-9" routerLink="/packages/offer" [queryParams]="{ customer: filter().customer || null }">
            <ng-icon name="lucideLightbulb" />{{ 'packages.offer.title' | transloco }}
          </a>
          @if (canEdit()) {
            <button hlmBtn type="button" class="h-11 lg:h-9" (click)="createOpen.set(true)"><ng-icon name="lucidePlus" />{{ 'packages.newPackage' | transloco }}</button>
          }
        </div>
      </header>

      @let f = filter();
      <div class="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center sm:gap-3">
        <div class="relative w-full sm:w-auto sm:min-w-72 sm:flex-1 lg:max-w-md">
          <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 -translate-y-1/2" />
          <input hlmInput type="search" class="h-11 w-full pr-9 pl-9 lg:h-9" [attr.aria-label]="'packages.searchLabel' | transloco"
                 [placeholder]="'packages.searchPlaceholder' | transloco" [value]="f.search" (input)="searchInput.next($any($event.target).value)" />
          @if (loading()) {
            <hlm-spinner class="text-muted-foreground pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2" />
          }
        </div>
        <div class="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap sm:items-center">
          <hlm-native-select selectId="pk-filter-customer" selectClass="h-11 w-full lg:h-9 lg:w-40" [attr.aria-label]="'packages.allCustomers' | transloco"
                             [value]="f.customer" (valueChange)="setFilter('customer', $event ?? '')">
            <option hlmNativeSelectOption value="">{{ 'packages.allCustomers' | transloco }}</option>
            @for (c of lookups()?.customers ?? []; track c.code) { <option hlmNativeSelectOption [value]="c.code">{{ c.code === c.name ? c.code : c.name }}</option> }
          </hlm-native-select>
          <hlm-native-select selectId="pk-filter-season" selectClass="h-11 w-full lg:h-9 lg:w-36" [attr.aria-label]="'packages.allSeasons' | transloco"
                             [value]="f.season" (valueChange)="setFilter('season', $event ?? '')">
            <option hlmNativeSelectOption value="">{{ 'packages.allSeasons' | transloco }}</option>
            @for (s of lookups()?.seasons ?? []; track s.code) { <option hlmNativeSelectOption [value]="s.code">{{ s.code === s.name ? s.code : s.name }}</option> }
          </hlm-native-select>
          <hlm-native-select selectId="pk-filter-status" selectClass="h-11 w-full lg:h-9 lg:w-36" [attr.aria-label]="'packages.allStatuses' | transloco"
                             [value]="f.status" (valueChange)="setFilter('status', $event ?? '')">
            <option hlmNativeSelectOption value="">{{ 'packages.allStatuses' | transloco }}</option>
            @for (s of statuses; track s) { <option hlmNativeSelectOption [value]="s">{{ 'packages.status.' + s | transloco }}</option> }
          </hlm-native-select>
          <label class="flex h-11 items-center gap-2 text-sm whitespace-nowrap lg:h-9" for="pk-filter-mine">
            <hlm-checkbox inputId="pk-filter-mine" [checked]="f.mine === 'true'" (checkedChange)="setFilter('mine', f.mine === 'true' ? '' : 'true')" />
            {{ 'packages.mine' | transloco }}
          </label>
          @if (activeFilters()) {
            <button hlmBtn variant="ghost" type="button" class="h-11 lg:h-9" (click)="clearFilters()"><ng-icon name="lucideX" />{{ 'styles.clear' | transloco }}</button>
          }
        </div>
      </div>

      @if (loading() && !items().length) {
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          @for (i of [1, 2, 3]; track i) { <div hlmSkeleton class="h-44 w-full"></div> }
        </div>
      } @else if (!items().length) {
        <section hlmCard class="items-center gap-2 px-5 py-14 text-center">
          <ng-icon name="lucidePackage" class="text-muted-foreground text-4xl" />
          @if (activeFilters()) {
            <p class="font-medium">{{ 'packages.noMatch' | transloco }}</p>
          } @else {
            <p class="font-medium">{{ 'packages.empty' | transloco }}</p>
            <p class="text-muted-foreground max-w-md text-sm">{{ (canEdit() ? 'packages.emptyHintEdit' : 'packages.emptyHint') | transloco }}</p>
          }
        </section>
      } @else {
        <ul class="grid gap-4 transition-opacity sm:grid-cols-2 xl:grid-cols-3" [class.opacity-50]="loading()" role="list">
          @for (p of items(); track p.packageId) {
            <li>
              <a [routerLink]="['/packages', p.packageId]" hlmCard class="hover:bg-accent/30 h-full flex-row gap-4 p-4 transition-colors">
                <span class="bg-muted flex size-20 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                  @if (p.coverUrl && !broken().has(p.coverUrl)) {
                    <img [appAuthSrc]="p.coverUrl" alt="" class="size-full object-cover" loading="lazy" (loadError)="markBroken(p.coverUrl)" />
                  } @else {
                    <ng-icon name="lucidePackage" class="text-muted-foreground text-2xl" />
                  }
                </span>
                <span class="flex min-w-0 flex-1 flex-col gap-1">
                  <span class="flex items-start justify-between gap-2">
                    <span class="truncate font-medium">{{ p.name }}</span>
                    <span class="shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge(tone(p))">{{ 'packages.status.' + p.status | transloco }}</span>
                  </span>
                  <span class="text-muted-foreground truncate text-sm">{{ p.customerName }}{{ p.seasonCode ? ' · ' + p.seasonCode : '' }}</span>
                  <span class="text-muted-foreground text-xs">{{ 'packages.styleCount' | transloco: { count: p.items } }}</span>
                  <span class="text-muted-foreground mt-auto truncate text-xs">
                    {{ 'packages.ownerChanged' | transloco: { owner: p.ownerName, date: (p.lastChangedUtc | date: 'mediumDate') } }}
                  </span>
                </span>
              </a>
            </li>
          }
        </ul>
        @if (items().length < total()) {
          <div class="text-center">
            <button hlmBtn variant="ghost" type="button" class="h-11 lg:h-9" [disabled]="loading()" (click)="load(true)">
              {{ 'styles.showMore' | transloco: { shown: (items().length | number), total: (total() | number) } }}
            </button>
          </div>
        }
      }
    </section>

    <app-package-form-dialog [open]="createOpen()" [lookups]="lookups()" (closed)="createOpen.set(false)" (saved)="onCreated($event)" />
  `,
})
export class PackagesList implements OnInit {
  private readonly svc = inject(PackagesService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly canEdit = this.styles.canEdit;
  readonly statuses = PACKAGE_STATUSES;
  readonly toneBadge = toneBadge;
  readonly lookups = signal<StyleLookups | null>(null);
  readonly filter = signal<Record<FilterKey, string>>({ search: '', customer: '', season: '', status: '', mine: '' });
  readonly items = signal<PackageListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly createOpen = signal(false);
  readonly broken = signal<ReadonlySet<string>>(new Set());
  readonly searchInput = new Subject<string>();
  readonly activeFilters = computed(() => FILTER_KEYS.filter((k) => this.filter()[k]).length);

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: (e) => toast.error(styleError(e).message) });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const next = { ...this.filter() };
      for (const k of FILTER_KEYS) next[k] = params.get(k) ?? '';
      this.filter.set(next);
      this.load();
    });
    this.searchInput.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.setFilter('search', v));
  }

  tone(p: PackageListItem) {
    return STATUS_TONE[p.status] ?? 'neutral';
  }

  setFilter(key: FilterKey, value: string): void {
    void this.router.navigate([], { queryParams: { [key]: value.trim() || null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  clearFilters(): void {
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  load(more = false): void {
    const f = this.filter();
    this.loading.set(true);
    this.svc
      .list({
        search: f.search || undefined, customer: f.customer || undefined, season: f.season || undefined, status: f.status || undefined,
        mine: f.mine === 'true', skip: more ? this.items().length : 0, take: PAGE,
      })
      .subscribe({
        next: (page) => {
          this.items.set(more ? [...this.items(), ...page.items] : page.items);
          this.total.set(page.total);
          this.loading.set(false);
        },
        error: (e) => {
          this.loading.set(false);
          toast.error(styleError(e).message);
        },
      });
  }

  onCreated(packageId: number): void {
    this.createOpen.set(false);
    void this.router.navigate(['/packages', packageId]);
  }

  markBroken(url: string): void {
    this.broken.update((s) => new Set(s).add(url));
  }
}
