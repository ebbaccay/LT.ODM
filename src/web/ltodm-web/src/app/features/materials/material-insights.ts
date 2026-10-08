import { DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { Observable } from 'rxjs';
import { StyleLookups, styleError } from '../style-library/style-library.service';
import {
  ColourUsage,
  Duplicates,
  InsightFilter,
  MaterialView,
  MaterialsService,
  RecycledShare,
  StandardTrims,
  SupplierConcentration,
} from './materials.service';

type Band = 'standard' | 'common' | 'occasional';

/** A job done by this many different materials, several of them used only once, is worth consolidating. */
const CONSOLIDATE_MATERIALS = 5;
const CONSOLIDATE_ONE_OFFS = 3;

/**
 * Materials page views beside the list: standard trims per product type, supplier concentration, duplicate
 * candidates, recycled share of fabrics and material colour usage. Filters (customer, season, section, product type)
 * live in the address and are shared with the list.
 */
@Component({
  selector: 'app-material-insights',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, PercentPipe, RouterLink, HlmBadgeImports, HlmCardImports, HlmNativeSelectImports, HlmSkeletonImports, TranslocoPipe],
  templateUrl: './material-insights.html',
  // Spacing between the filters, the view's hint, its totals and its results.
  host: { class: 'flex flex-col gap-4' },
})
export class MaterialInsights implements OnInit {
  private readonly svc = inject(MaterialsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Read from the address in the same callback that loads, so a tab change never loads the previous view. */
  readonly view = signal<Exclude<MaterialView, 'all'>>('trims');
  readonly lookups = input<StyleLookups | null>(null);

  readonly filter = signal<InsightFilter>({});
  readonly loading = signal(false);
  readonly trims = signal<StandardTrims | null>(null);
  readonly suppliers = signal<SupplierConcentration | null>(null);
  readonly duplicates = signal<Duplicates | null>(null);
  readonly recycled = signal<RecycledShare | null>(null);
  readonly colours = signal<ColourUsage[] | null>(null);

  readonly bands: Band[] = ['standard', 'common', 'occasional'];
  readonly trimBands = computed(() => {
    const list = this.trims()?.materials ?? [];
    const band = (share: number): Band => (share >= 0.5 ? 'standard' : share >= 0.2 ? 'common' : 'occasional');
    return this.bands.map((b) => ({ band: b, items: list.filter((m) => band(Number(m.share)) === b) }));
  });
  readonly supplierMax = computed(() => Math.max(0.0001, ...(this.suppliers()?.suppliers ?? []).map((s) => Number(s.share))));
  readonly colourMax = computed(() => Math.max(1, ...(this.colours() ?? []).map((c) => c.colorways)));

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      const view = p.get('view');
      if (view === 'trims' || view === 'suppliers' || view === 'duplicates' || view === 'recycled' || view === 'colours') this.view.set(view);
      this.filter.set({
        customer: p.get('customer'),
        season: p.get('season'),
        contentClass: p.get('contentClass'),
        productType: p.get('productType'),
      });
      this.load();
    });
  }

  setFilter(key: keyof InsightFilter, value: string): void {
    void this.router.navigate([], { queryParams: { [key]: value || null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  private load(): void {
    const f = this.filter();
    const run = <T>(call: Observable<T>, target: (v: T) => void) => {
      this.loading.set(true);
      call.subscribe({
        next: (v) => {
          target(v);
          this.loading.set(false);
        },
        error: (e) => {
          this.loading.set(false);
          toast.error(styleError(e).message);
        },
      });
    };
    switch (this.view()) {
      case 'trims':
        // A product type is required; start with the one that has the most styles.
        if (!f.productType) {
          const first = this.defaultProductType();
          if (first) this.setFilter('productType', first);
          return;
        }
        run(this.svc.standardTrims(f), (v) => this.trims.set(v));
        break;
      case 'suppliers':
        run(this.svc.suppliers(f), (v) => this.suppliers.set(v));
        break;
      case 'duplicates':
        run(this.svc.duplicates({ contentClass: f.contentClass }), (v) => this.duplicates.set(v));
        break;
      case 'recycled':
        run(this.svc.recycled({ customer: f.customer }), (v) => this.recycled.set(v));
        break;
      case 'colours':
        run(this.svc.colours(f), (v) => this.colours.set(v));
        break;
    }
  }

  private defaultProductType(): string | null {
    const codes = (this.lookups()?.productTypes ?? []).map((p) => p.code);
    return codes.includes('JACKET') ? 'JACKET' : (codes[0] ?? null);
  }

  consolidate = (t: { materials: number; oneOffs: number }) => t.materials >= CONSOLIDATE_MATERIALS && t.oneOffs >= CONSOLIDATE_ONE_OFFS;
  ratio = (part: number, whole: number) => (whole ? part / whole : 0);

  num(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    const n = Number(value);
    return Number.isInteger(n) ? String(n) : n.toFixed(4).replace(/0+$/, '').replace(/\.$/, '');
  }
}
