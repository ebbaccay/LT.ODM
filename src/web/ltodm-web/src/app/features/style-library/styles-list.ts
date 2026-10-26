import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDown, lucideArrowUp, lucideGitBranch, lucideImage, lucidePlus, lucideSearch, lucideShirt, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Subject, debounceTime } from 'rxjs';
import { StyleFormDialog } from './style-form-dialog';
import { GENDERS, StyleLibraryService, StyleListFilter, StyleListItem, StyleLookups, styleError } from './style-library.service';

const PAGE = 50;
// status: '' = active styles (the default), 'inactive', or 'all'.
// pictures: '' = styles with a picture or sketch (the default), or 'all'.
// sort / dir: the column sorted by and 'desc' (both kept in the address; empty = newest season first).
// versions: '' = parent styles only, versions folded under them (the default), or 'all'.
// weaveType and material have no select here; AI Studio's smart search sets them (and several seasons / product types).
const FILTER_KEYS = ['search', 'customer', 'season', 'businessUnit', 'productType', 'gender', 'status', 'weaveType', 'material', 'looks', 'pictures', 'versions', 'sort', 'dir'] as const;
const SORT_KEYS = ['style', 'customer', 'season', 'businessUnit', 'productType', 'colorways', 'bomLines', 'changed'] as const;
/** Counts, dates and seasons start largest / newest first; text starts A-Z. */
const DESC_FIRST: readonly string[] = ['colorways', 'bomLines', 'changed', 'season'];
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
    HlmCheckboxImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideArrowDown, lucideArrowUp, lucideGitBranch, lucideImage, lucidePlus, lucideSearch, lucideShirt, lucideX })],
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
    search: '', customer: '', season: '', businessUnit: '', productType: '', gender: '', status: '', weaveType: '', material: '', looks: '', pictures: '', versions: '', sort: '', dir: '',
  });
  readonly styles = signal<StyleListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly createOpen = signal(false);
  /** Image URLs that failed to load (the row falls back to the sketch, then the placeholder). */
  readonly brokenImages = signal<ReadonlySet<string>>(new Set());

  // "With pictures only" and "Parent styles only" are view settings (on by default), not filters to count or clear.
  readonly activeFilters = computed(() => FILTER_KEYS.filter((k) => k !== 'search' && k !== 'pictures' && k !== 'versions' && k !== 'sort' && k !== 'dir' && this.filter()[k]).length);
  readonly picturesOnly = computed(() => this.filter().pictures !== 'all');
  readonly parentsOnly = computed(() => this.filter().versions !== 'all');
  readonly sortKeys = SORT_KEYS;
  readonly sortKey = computed(() => this.filter().sort);
  readonly sortDesc = computed(() => this.filter().dir === 'desc');

  /** The search's words as one pattern, to highlight them in the colorways and BOM lines it was found on. */
  private readonly searchPattern = computed(() => {
    const words = this.filter().search.trim().split(/\s+/).filter(Boolean);
    return words.length ? new RegExp(`(${words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`, 'i') : null;
  });

  /** Filters the selects cannot show (several codes, or no select at all), as removable chips. */
  readonly extraChips = computed(() => {
    const f = this.filter();
    const chips: { key: FilterKey; label: string; value: string }[] = [];
    if (f.season.includes(',')) chips.push({ key: 'season', label: 'styles.chipSeasons', value: f.season.replaceAll(',', ', ') });
    if (f.businessUnit.includes(',')) chips.push({ key: 'businessUnit', label: 'styles.chipBusinessUnits', value: f.businessUnit.replaceAll(',', ', ') });
    if (f.productType.includes(',')) chips.push({ key: 'productType', label: 'styles.chipProductTypes', value: f.productType.replaceAll(',', ', ') });
    if (f.weaveType) chips.push({ key: 'weaveType', label: 'styles.chipWeave', value: f.weaveType });
    if (f.material) chips.push({ key: 'material', label: 'styles.chipMaterial', value: f.material });
    if (f.looks) chips.push({ key: 'looks', label: 'styles.chipLooks', value: f.looks.replaceAll(',', ', ') });
    return chips;
  });

  ngOnInit(): void {
    this.svc.lookups().subscribe({ next: (l) => this.lookups.set(l), error: (e) => toast.error(styleError(e).message) });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const next = { ...this.filter() };
      for (const k of FILTER_KEYS) next[k] = params.get(k) ?? '';
      this.filter.set(next);
      // for "back to Styles" on a style's page
      this.svc.rememberListParams(Object.fromEntries(FILTER_KEYS.filter((k) => next[k]).map((k) => [k, next[k]])));
      this.load();
    });
    this.searchInput.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe((value) => this.setFilter('search', value));
  }

  /** Text split into plain and highlighted (search word) parts. */
  highlight(text: string | null): { text: string; hit: boolean }[] {
    const re = this.searchPattern();
    if (!text) return [];
    if (!re) return [{ text, hit: false }];
    // With one capture group, split() alternates plain text (even indexes) and matched words (odd indexes).
    return text
      .split(re)
      .map((part, i) => ({ text: part, hit: i % 2 === 1 }))
      .filter((p) => p.text);
  }

  /** How many more colorways / BOM lines matched than are listed under the style. */
  moreFound(s: StyleListItem, kind: 'Colorway' | 'BomLine'): number {
    const f = s.found;
    if (!f) return 0;
    return (kind === 'Colorway' ? f.colorways : f.bomLines) - f.matches.filter((m) => m.kind === kind).length;
  }

  /** Opens the style where the search was found: the BOM tab filtered by the search, or the colorways. */
  rowParams(s: StyleListItem): Record<string, string> | null {
    const f = s.found;
    if (f?.bomLines) return { tab: 'bom', bomSearch: this.filter().search.trim() };
    if (f?.colorways) return { tab: 'colorways' };
    return null;
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

  /** Keeps "With pictures only" and "Parent styles only" as they are; clears everything else. */
  clearFilters(): void {
    const f = this.filter();
    void this.router.navigate([], {
      queryParams: { pictures: f.pictures || null, versions: f.versions || null, sort: f.sort || null, dir: f.dir || null },
      replaceUrl: true,
    });
  }

  /** A heading: sort by it; the same one again reverses the order. */
  sortBy(key: string): void {
    const f = this.filter();
    const desc = f.sort === key ? f.dir !== 'desc' : DESC_FIRST.includes(key);
    void this.router.navigate([], { queryParams: { sort: key, dir: desc ? 'desc' : null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  /** The phone sort list: 'key' or 'key:desc'; empty = the default order. */
  sortFromSelect(value: string): void {
    const [key, dir] = value.split(':');
    void this.router.navigate([], { queryParams: { sort: key || null, dir: dir === 'desc' ? 'desc' : null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  toggleParentsOnly(): void {
    this.setFilter('versions', this.parentsOnly() ? 'all' : '');
  }

  togglePicturesOnly(): void {
    this.setFilter('pictures', this.picturesOnly() ? 'all' : '');
  }

  load(more = false): void {
    const f = this.filter();
    const status = f.status === 'all' ? undefined : (f.status || 'active');
    const pictures = f.pictures === 'all' ? undefined : 'with';
    const { versions, sort, dir, ...filters } = f;
    const parentsOnly = versions === 'all' ? undefined : true;
    const query: StyleListFilter = {
      ...filters, status, pictures, parentsOnly, sort: sort || undefined, desc: sort && dir === 'desc' ? true : undefined,
      skip: more ? this.styles().length : 0, take: PAGE,
    };
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
