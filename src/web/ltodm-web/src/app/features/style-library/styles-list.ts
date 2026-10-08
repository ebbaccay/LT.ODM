import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGitBranch, lucideImage, lucidePlus, lucideSearch, lucideShirt, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Subject, debounceTime } from 'rxjs';
import { StyleFormDialog } from './style-form-dialog';
import { GENDERS, StyleLibraryService, StyleListFilter, StyleListItem, StyleLookups, styleError } from './style-library.service';

const PAGE = 50;
// status: '' = active styles (the default), 'inactive', or 'all'.
// weaveType and material have no select here; AI Studio's smart search sets them (and several seasons / product types).
const FILTER_KEYS = ['search', 'customer', 'season', 'businessUnit', 'productType', 'gender', 'status', 'weaveType', 'material'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

/**
 * Style Library > Styles: every style with search and filters (kept in the address so a filtered list can be
 * bookmarked or shared). Admins and merchandisers can add a style; the rest of the editing is on the style's page.
 */
@Component({
  selector: 'app-styles-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, NgIcon, AuthImgDirective, StyleFormDialog, HlmBadgeImports, HlmButtonImports, HlmCardImports,
    HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideGitBranch, lucideImage, lucidePlus, lucideSearch, lucideShirt, lucideX })],
  templateUrl: './styles-list.html',
})
export class StylesList implements OnInit {
  private readonly svc = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchInput = new Subject<string>();

  readonly canEdit = this.svc.canEdit;
  readonly genders = GENDERS;
  readonly lookups = signal<StyleLookups | null>(null);
  readonly filter = signal<Record<FilterKey, string>>({
    search: '', customer: '', season: '', businessUnit: '', productType: '', gender: '', status: '', weaveType: '', material: '',
  });
  readonly styles = signal<StyleListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly createOpen = signal(false);
  /** Image URLs that failed to load (the row falls back to the sketch, then the placeholder). */
  readonly brokenImages = signal<ReadonlySet<string>>(new Set());

  readonly activeFilters = computed(() => FILTER_KEYS.filter((k) => k !== 'search' && this.filter()[k]).length);

  /** Filters the selects cannot show (several codes, or no select at all), as removable chips. */
  readonly extraChips = computed(() => {
    const f = this.filter();
    const chips: { key: FilterKey; label: string; value: string }[] = [];
    if (f.season.includes(',')) chips.push({ key: 'season', label: 'styles.chipSeasons', value: f.season.replaceAll(',', ', ') });
    if (f.productType.includes(',')) chips.push({ key: 'productType', label: 'styles.chipProductTypes', value: f.productType.replaceAll(',', ', ') });
    if (f.weaveType) chips.push({ key: 'weaveType', label: 'styles.chipWeave', value: f.weaveType });
    if (f.material) chips.push({ key: 'material', label: 'styles.chipMaterial', value: f.material });
    return chips;
  });

  ngOnInit(): void {
    this.svc.lookups().subscribe({ next: (l) => this.lookups.set(l), error: (e) => toast.error(styleError(e).message) });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const next = { ...this.filter() };
      for (const k of FILTER_KEYS) next[k] = params.get(k) ?? '';
      this.filter.set(next);
      this.load();
    });
    this.searchInput.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe((value) => this.setFilter('search', value));
  }

  onSearch(event: Event): void {
    this.searchInput.next((event.target as HTMLInputElement).value);
  }

  /** Filters live in the address; changing one navigates and the subscription reloads. */
  setFilter(key: FilterKey, value: string): void {
    const queryParams: Record<string, string | null> = {};
    queryParams[key] = value.trim() || null;
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }

  clearFilters(): void {
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  load(more = false): void {
    const f = this.filter();
    const status = f.status === 'all' ? undefined : (f.status || 'active');
    const query: StyleListFilter = { ...f, status, skip: more ? this.styles().length : 0, take: PAGE };
    this.loading.set(true);
    this.svc.list(query).subscribe({
      next: (page) => {
        this.styles.set(more ? [...this.styles(), ...page.items] : page.items);
        this.total.set(page.total);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  onCreated(styleId: number): void {
    this.createOpen.set(false);
    void this.router.navigate(['/styles', styleId]);
  }

  markBroken(url: string): void {
    this.brokenImages.update((s) => new Set(s).add(url));
  }

  /** Row thumbnail: the style photo first, else the sketch; skips an image that failed to load. */
  thumbFor(s: StyleListItem): string | null {
    const broken = this.brokenImages();
    return [s.imageUrl, s.sketchUrl].find((u): u is string => !!u && !broken.has(u)) ?? null;
  }

  filterValue = (key: string) => this.filter()[key as FilterKey] ?? '';

  /** Translation key for a known gender code, else the code itself (shown as is). */
  genderLabel = (code: string | null) => (GENDERS.some((g) => g.code === code) ? `styles.gender.${code}` : (code ?? ''));
}
