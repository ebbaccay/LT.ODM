import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideListTree, lucideSearch, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { Subject, debounceTime } from 'rxjs';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { MaterialInsights } from './material-insights';
import { MaterialReader } from './material-reader';
import { MaterialListItem, MaterialView, MaterialsService } from './materials.service';

const PAGE = 50;
const VIEWS: MaterialView[] = ['all', 'trims', 'suppliers', 'duplicates', 'recycled', 'colours', 'reader'];
const FILTER_KEYS = ['search', 'contentClass', 'materialType', 'supplier', 'customer', 'season', 'sort'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

/**
 * Style Library > Materials: every material on a BOM with how widely it is used (styles, lines, seasons, suppliers),
 * most used first. Filters live in the address. Customer / season filters narrow the counts to those styles.
 */
@Component({
  selector: 'app-materials-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, NgIcon, MaterialInsights, MaterialReader, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports,
    TranslocoPipe,
  ],
  providers: [provideIcons({ lucideListTree, lucideSearch, lucideX })],
  templateUrl: './materials-list.html',
})
export class MaterialsList implements OnInit {
  private readonly svc = inject(MaterialsService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchInput = new Subject<string>();

  readonly views = VIEWS;
  /** 'all' = the list; the others are the views in MaterialInsights (?view=...). */
  readonly view = signal<MaterialView>('all');
  readonly lookups = signal<StyleLookups | null>(null);
  readonly filter = signal<Record<FilterKey, string>>({ search: '', contentClass: '', materialType: '', supplier: '', customer: '', season: '', sort: '' });
  readonly items = signal<MaterialListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);

  readonly activeFilters = computed(() => FILTER_KEYS.filter((k) => k !== 'search' && k !== 'sort' && this.filter()[k]).length);
  readonly scoped = computed(() => !!(this.filter().customer || this.filter().season));

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: (e) => toast.error(styleError(e).message) });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const next = { ...this.filter() };
      for (const k of FILTER_KEYS) next[k] = params.get(k) ?? '';
      this.filter.set(next);
      const view = params.get('view') as MaterialView | null;
      this.view.set(view && VIEWS.includes(view) ? view : 'all');
      if (this.view() === 'all') this.load();
    });
    this.searchInput.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe((value) => this.setFilter('search', value));
  }

  onSearch(event: Event): void {
    this.searchInput.next((event.target as HTMLInputElement).value);
  }

  setFilter(key: FilterKey, value: string): void {
    const queryParams: Record<string, string | null> = {};
    queryParams[key] = value.trim() || null;
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }

  clearFilters(): void {
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  /** Switching view keeps the shared filters (customer, season, section). */
  setView(view: MaterialView): void {
    void this.router.navigate([], { queryParams: { view: view === 'all' ? null : view }, queryParamsHandling: 'merge' });
  }

  load(more = false): void {
    const f = this.filter();
    this.loading.set(true);
    this.svc
      .list({ ...f, sort: f.sort === 'code' ? 'code' : 'used', skip: more ? this.items().length : 0, take: PAGE })
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

  filterValue = (key: string) => this.filter()[key as FilterKey] ?? '';
}
