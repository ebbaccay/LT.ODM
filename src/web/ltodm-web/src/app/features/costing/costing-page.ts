import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalculator, lucideDownload, lucideSearch, lucideUpload } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { CostingImportResult, CostingPage as Page, CostingService, StylePriceSummary } from './costing.service';
import { PlantRatesPanel } from './plant-rates';
import { fobRange } from './price-summary';

const PAGE_SIZE = 50;

/**
 * Style Library > Costing (roadmap 3.16): how much each garment has cost, per style over its IOs (latest FOB, range, average weighted by
 * quantity), most recently costed first; a style's IOs are on its style page. Admin loads the ERP costing export here.
 */
@Component({
  selector: 'app-costing-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, PercentPipe, RouterLink, NgIcon, HlmButtonImports, HlmCardImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, HlmTooltipImports, PlantRatesPanel, TranslocoPipe],
  providers: [DecimalPipe, provideIcons({ lucideCalculator, lucideDownload, lucideSearch, lucideUpload })],
  template: `
    <section class="flex flex-col gap-5" [attr.aria-busy]="loading()">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideCalculator" />{{ 'titles.costing' | transloco }}</h1>
          <p class="text-muted-foreground mt-1 max-w-3xl text-sm">{{ 'ioPrices.subtitle' | transloco }}</p>
          @if (page()?.lastImport; as li) {
            <p class="text-muted-foreground mt-1 text-xs">{{ 'ioPrices.lastImport' | transloco: { file: li.fileName, user: li.importedBy, date: (li.importedUtc | date: 'medium') } }}</p>
          }
        </div>
        @if (svc.canImport()) {
          <!-- Kept together on the right: the template and the upload it is for -->
          <div class="flex flex-wrap items-center gap-2">
            <a hlmBtn variant="outline" class="h-11 lg:h-9" href="assets/templates/costing-import-template.xlsx" download="LT_ODM_costing_template.xlsx">
              <ng-icon name="lucideDownload" />{{ 'ioPrices.template' | transloco }}
            </a>
            <input #file type="file" class="sr-only" accept=".xlsx" tabindex="-1" aria-hidden="true" (change)="upload(file)" />
            <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="importing()" (click)="file.click()"><ng-icon name="lucideUpload" />{{ (importing() ? 'ioPrices.importing' : 'ioPrices.import') | transloco }}</button>
          </div>
        }
      </header>

      @if (result(); as r) {
        <section hlmCard class="gap-2 p-4 text-sm" role="status">
          <p class="font-medium">{{ 'ioPrices.importDone' | transloco: { rows: r.import.rowsRead, added: r.import.added, updated: r.import.updated, unmatched: r.import.unmatched } }}</p>
          @if (r.unmatchedStyles.length) {
            <p class="text-muted-foreground text-xs">{{ 'ioPrices.unmatched' | transloco }}</p>
            <ul class="flex flex-wrap gap-x-3 gap-y-1 font-mono text-xs" role="list">
              @for (u of r.unmatchedStyles; track u.customer + u.season + u.styleNo) { <li>{{ u.customer }} {{ u.season }} {{ u.styleNo }}</li> }
            </ul>
          }
          @if (r.issues.length) {
            <ul class="text-muted-foreground flex list-disc flex-col gap-0.5 pl-4 text-xs">
              @for (x of r.issues; track x) { <li>{{ x }}</li> }
            </ul>
          }
        </section>
      }

      @if (svc.canKeepRates()) {
        <div class="bg-muted flex w-full rounded-lg p-1 sm:w-fit" role="tablist">
          @for (t of tabs; track t) {
            <button type="button" role="tab" class="flex min-h-11 flex-1 items-center justify-center rounded-md px-4 text-sm font-medium whitespace-nowrap lg:min-h-8"
                    [class]="view() === t ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'"
                    [attr.aria-selected]="view() === t" (click)="view.set(t)">{{ 'ioPrices.tab.' + t | transloco }}</button>
          }
        </div>
      }

      @if (view() === 'rates') {
        <app-plant-rates />
      } @else {
      <div class="grid gap-2 sm:flex sm:items-center">
        <div class="relative w-full sm:max-w-sm">
          <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 -translate-y-1/2" />
          <input hlmInput type="search" class="h-11 w-full pl-9 lg:h-9" [attr.aria-label]="'ioPrices.search' | transloco" [placeholder]="'ioPrices.search' | transloco"
                 [value]="search()" (change)="search.set($any($event.target).value); go(1)" />
        </div>
        <hlm-native-select selectId="cost-customer" selectClass="h-11 w-full sm:w-52 lg:h-9" [attr.aria-label]="'packages.allCustomers' | transloco" [value]="customer()" (valueChange)="customer.set($event ?? ''); go(1)">
          <option hlmNativeSelectOption value="">{{ 'packages.allCustomers' | transloco }}</option>
          @for (c of lookups()?.customers ?? []; track c.code) { <option hlmNativeSelectOption [value]="c.code">{{ c.code }}</option> }
        </hlm-native-select>
        <hlm-native-select selectId="cost-season" selectClass="h-11 w-full sm:w-40 lg:h-9" [attr.aria-label]="'ioPrices.allSeasons' | transloco" [value]="season()" (valueChange)="season.set($event ?? ''); go(1)">
          <option hlmNativeSelectOption value="">{{ 'ioPrices.allSeasons' | transloco }}</option>
          @for (s of lookups()?.seasons ?? []; track s.code) { <option hlmNativeSelectOption [value]="s.code">{{ s.code }}</option> }
        </hlm-native-select>
      </div>

      @if (loading() && !page()) {
        <div hlmSkeleton class="h-64 w-full"></div>
      } @else if (page(); as p) {
        @if (!p.items.length) {
          <section hlmCard class="items-center gap-2 px-5 py-14 text-center">
            <ng-icon name="lucideCalculator" class="text-muted-foreground text-4xl" />
            <p class="font-medium">{{ 'ioPrices.empty' | transloco }}</p>
            <p class="text-muted-foreground max-w-md text-sm">{{ 'ioPrices.emptyHint' | transloco }}</p>
          </section>
        } @else {
          <section hlmCard class="gap-0 py-0 transition-opacity" [class.opacity-60]="loading()">
            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead class="text-muted-foreground text-left text-xs tracking-wide uppercase">
                  <tr class="border-b">
                    <th class="px-5 py-2 font-medium">{{ 'ioPrices.col.style' | transloco }}</th>
                    <th class="px-3 py-2 font-medium">{{ 'ioPrices.col.garment' | transloco }}</th>
                    <th class="px-3 py-2 font-medium">{{ 'ioPrices.col.customer' | transloco }}</th>
                    <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.ios' | transloco">{{ 'ioPrices.ios' | transloco }}</span></th>
                    <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.qty' | transloco">{{ 'ioPrices.col.qty' | transloco }}</span></th>
                    <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.latestFob' | transloco">{{ 'ioPrices.latestFob' | transloco }}</span></th>
                    <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.range' | transloco">{{ 'ioPrices.range' | transloco }}</span></th>
                    <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.average' | transloco">{{ 'ioPrices.average' | transloco }}</span></th>
                    @if (p.showCost) {
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.net' | transloco">{{ 'ioPrices.group.net' | transloco }}</span></th>
                      <th class="px-3 py-2 text-right font-medium"><span class="cursor-help underline decoration-dotted underline-offset-4" tabindex="0" [hlmTooltip]="'ioPrices.tip.percent' | transloco">{{ 'ioPrices.col.percent' | transloco }}</span></th>
                    }
                  </tr>
                </thead>
                <tbody class="divide-y">
                  @for (s of p.items; track s.styleId + s.currencyCode) {
                    <tr>
                      <td class="px-5 py-2">
                        <a class="text-primary font-medium underline-offset-2 hover:underline" [routerLink]="['/styles', s.styleId]" [queryParams]="{ tab: 'prices' }">{{ s.styleNo }}</a>
                        @if (s.modelName) { <span class="text-muted-foreground block text-xs">{{ s.modelName }}</span> }
                      </td>
                      <td class="px-3 py-2 text-xs">{{ s.productTypeName }}</td>
                      <td class="px-3 py-2 text-xs whitespace-nowrap">{{ s.customerCode }} · {{ s.seasonCode }}</td>
                      <td class="px-3 py-2 text-right tabular-nums">{{ s.ios | number }}</td>
                      <td class="px-3 py-2 text-right tabular-nums">{{ s.totalQty | number }}</td>
                      <td class="px-3 py-2 text-right font-semibold whitespace-nowrap tabular-nums">{{ s.latestFob | number: '1.2-2' }} <span class="text-muted-foreground text-xs font-normal">{{ s.currencyCode }}</span></td>
                      <td class="px-3 py-2 text-right whitespace-nowrap tabular-nums">{{ rangeText(s) }}</td>
                      <td class="px-3 py-2 text-right tabular-nums">{{ s.avgFob | number: '1.2-2' }}</td>
                      @if (p.showCost) {
                        <td class="px-3 py-2 text-right tabular-nums">{{ s.avgNet | number: '1.2-2' }}</td>
                        <td class="px-3 py-2 text-right tabular-nums">{{ (s.avgNet ?? 0) / s.avgFob | percent: '1.1-1' }}</td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
          @if (p.total > pageSize) {
            <div class="flex items-center justify-between gap-2 text-sm">
              <span class="text-muted-foreground">{{ 'ioPrices.pageOf' | transloco: { page: pageNo(), pages: pages(p) } }}</span>
              <span class="flex gap-2">
                <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" [disabled]="pageNo() <= 1 || loading()" (click)="go(pageNo() - 1)">{{ 'ioPrices.previous' | transloco }}</button>
                <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" [disabled]="pageNo() >= pages(p) || loading()" (click)="go(pageNo() + 1)">{{ 'ioPrices.next' | transloco }}</button>
              </span>
            </div>
          }
        }
      }
      }
    </section>
  `,
})
export class CostingPage implements OnInit {
  readonly tabs = ['styles', 'rates'] as const;
  readonly view = signal<'styles' | 'rates'>('styles');
  readonly svc = inject(CostingService);
  private readonly styles = inject(StyleLibraryService);
  private readonly number = inject(DecimalPipe);

  readonly pageSize = PAGE_SIZE;
  readonly lookups = signal<StyleLookups | null>(null);
  readonly page = signal<Page | null>(null);
  readonly loading = signal(true);
  readonly importing = signal(false);
  readonly result = signal<CostingImportResult | null>(null);
  readonly search = signal('');
  readonly customer = signal('');
  readonly season = signal('');
  readonly pageNo = signal(1);

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l) });
    this.load();
  }

  go(page: number): void {
    this.pageNo.set(page);
    this.load();
  }

  /** One amount when every IO is at the same price. */
  rangeText(s: StylePriceSummary): string {
    return fobRange(this.number, s);
  }

  pages(p: Page): number {
    return Math.max(1, Math.ceil(p.total / PAGE_SIZE));
  }

  private load(): void {
    this.loading.set(true);
    this.svc.list(this.search(), this.customer(), this.season(), this.pageNo(), PAGE_SIZE).subscribe({
      next: (p) => {
        this.page.set(p);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  upload(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.importing.set(true);
    this.result.set(null);
    this.svc.importFile(file).subscribe({
      next: (r) => {
        this.importing.set(false);
        this.result.set(r);
        this.go(1);
      },
      error: (e) => {
        this.importing.set(false);
        toast.error(styleError(e).message);
      },
    });
  }
}
