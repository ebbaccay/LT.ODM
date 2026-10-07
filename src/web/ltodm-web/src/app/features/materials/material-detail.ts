import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCopy, lucideTriangleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { styleError } from '../style-library/style-library.service';
import { MaterialBenchmark, MaterialDetail as Detail, MaterialSpec, MaterialUse, MaterialsService } from './materials.service';

type Tab = 'uses' | 'consumption' | 'suppliers' | 'colours' | 'similar';

/** A use is "unusual" when its consumption is under 0.6x or over 1.6x the median for its product type (at least 3 styles). */
const LOW = 0.6;
const HIGH = 1.6;

/**
 * Style Library > material page: where the material is used (every style and line, newest season first), consumption
 * per product type (spread across styles), its suppliers and colours, and materials that may be the same thing.
 */
@Component({
  selector: 'app-material-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmNativeSelectImports, HlmSkeletonImports,
    TranslocoPipe,
  ],
  providers: [provideIcons({ lucideArrowLeft, lucideCopy, lucideTriangleAlert })],
  templateUrl: './material-detail.html',
})
export class MaterialDetailPage implements OnInit {
  private readonly svc = inject(MaterialsService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly detail = signal<Detail | null>(null);
  /** The AI reading of the description (Materials > Description reader), if any. */
  readonly spec = signal<MaterialSpec | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly tab = signal<Tab>('uses');
  readonly productType = signal('');
  readonly unusualOnly = signal(false);

  /** Benchmarks by product type + unit, for marking unusual uses. */
  private readonly benchByKey = computed(() => new Map((this.detail()?.benchmarks ?? []).map((b) => [this.key(b.productTypeCode, b.uomCode), b])));

  readonly productTypes = computed(() => {
    const seen = new Map<string, string>();
    for (const u of this.detail()?.uses ?? []) seen.set(u.productTypeCode ?? '', u.productTypeName ?? u.productTypeCode ?? '');
    return [...seen.entries()].map(([code, name]) => ({ code, name })).sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly uses = computed(() => {
    const pt = this.productType();
    const unusual = this.unusualOnly();
    return (this.detail()?.uses ?? []).filter((u) => (!pt || (u.productTypeCode ?? '') === pt) && (!unusual || this.unusual(u) !== null));
  });

  readonly unusualCount = computed(() => (this.detail()?.uses ?? []).filter((u) => this.unusual(u) !== null).length);

  /** Top of each benchmark bar: the largest value across product types of the same unit. */
  readonly scaleByUom = computed(() => {
    const max = new Map<string, number>();
    for (const b of this.detail()?.benchmarks ?? []) max.set(b.uomCode, Math.max(max.get(b.uomCode) ?? 0, Number(b.maxValue)));
    return max;
  });

  readonly supplierMax = computed(() => Math.max(1, ...(this.detail()?.suppliers ?? []).map((s) => s.styles)));

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      this.tab.set('uses');
      this.productType.set('');
      this.unusualOnly.set(false);
      this.load(Number(p.get('id')));
    });
  }

  private load(id: number): void {
    this.loading.set(true);
    this.spec.set(null);
    this.svc.spec(id).subscribe({ next: (s) => this.spec.set(s), error: () => undefined });
    this.svc.get(id).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.notFound.set(false);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        if (e instanceof HttpErrorResponse && e.status === 404) this.notFound.set(true);
        else toast.error(styleError(e).message);
      },
    });
  }

  private key = (productType: string | null, uom: string | null) => `${productType ?? ''}|${uom ?? ''}`;

  /** Consumption compared: brand's, else LCO (as in the BOM check). */
  eff = (u: MaterialUse) => (u.brandConsumption && u.brandConsumption > 0 ? u.brandConsumption : u.lcoConsumption && u.lcoConsumption > 0 ? u.lcoConsumption : null);

  /** 'low' / 'high' against the product type's median, or null when usual or not comparable. */
  unusual(u: MaterialUse): 'low' | 'high' | null {
    const b = this.benchByKey().get(this.key(u.productTypeCode, u.uomCode));
    const v = this.eff(u);
    if (!b || b.styles < 3 || !v || !b.median) return null;
    const r = v / Number(b.median);
    return r < LOW ? 'low' : r > HIGH ? 'high' : null;
  }

  benchFor = (u: MaterialUse) => this.benchByKey().get(this.key(u.productTypeCode, u.uomCode)) ?? null;

  /** Left position (%) of a value on its unit's scale. */
  pos(b: MaterialBenchmark, value: number): number {
    const max = this.scaleByUom().get(b.uomCode) || 1;
    return Math.min(100, (Number(value) / max) * 100);
  }

  /** Width (%) of the middle-half box; at least a sliver so a tight spread still shows. */
  boxWidth = (b: MaterialBenchmark) => Math.max(0.8, this.pos(b, b.p75) - this.pos(b, b.p25));

  num(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    const n = Number(value);
    return Number.isInteger(n) ? String(n) : n.toFixed(4).replace(/0+$/, '').replace(/\.$/, '');
  }

  copyCode(code: string): void {
    void navigator.clipboard?.writeText(code);
  }
}
