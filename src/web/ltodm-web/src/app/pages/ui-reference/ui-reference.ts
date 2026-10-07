import { Component, computed, inject } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import type { ColDef, GridApi, GridReadyEvent } from 'ag-grid-community';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileSpreadsheet, lucideInfo } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { toast } from '@spartan-ng/brain/sonner';
import { LayoutService } from '../../layout/service/layout.service';

interface SampleRow {
  category: string;
  item: string;
  supplier: string;
  qty: number;
  unitCost: number;
}

/**
 * Developer reference: the Spartan components and an AG Grid Enterprise grid with the app theme.
 * Sample data only - not a business feature.
 */
@Component({
  selector: 'app-ui-reference',
  imports: [AgGridAngular, NgIcon, HlmAlertImports, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideFileSpreadsheet, lucideInfo })],
  template: `
    <h1 class="mb-2 text-2xl font-semibold tracking-tight">UI reference</h1>
    <p class="text-muted-foreground mb-6">Spartan/ui components and AG Grid with the LT ODM theme. Sample data only.</p>

    <div class="grid grid-cols-1 gap-4 lg:grid-cols-3">
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>Buttons</h2>
          <p hlmCardDescription>hlmBtn variants</p>
        </div>
        <div hlmCardContent class="flex flex-wrap gap-2">
          <button hlmBtn>Primary</button>
          <button hlmBtn variant="secondary">Secondary</button>
          <button hlmBtn variant="outline">Outline</button>
          <button hlmBtn variant="ghost">Ghost</button>
          <button hlmBtn variant="destructive">Delete</button>
          <button hlmBtn variant="outline" (click)="showToast()">Show toast</button>
        </div>
      </section>

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>Badges</h2>
          <p hlmCardDescription>Status labels</p>
        </div>
        <div hlmCardContent class="flex flex-wrap gap-2">
          <span hlmBadge>Approved</span>
          <span hlmBadge variant="secondary">Draft</span>
          <span hlmBadge variant="outline">Sampling</span>
          <span hlmBadge variant="destructive">Rejected</span>
        </div>
      </section>

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>Loading</h2>
          <p hlmCardDescription>Skeletons while data loads</p>
        </div>
        <div hlmCardContent class="flex flex-col gap-2">
          <hlm-skeleton class="h-4 w-3/4" />
          <hlm-skeleton class="h-4 w-1/2" />
          <hlm-skeleton class="h-4 w-2/3" />
        </div>
      </section>
    </div>

    <div hlmAlert class="my-6">
      <ng-icon name="lucideInfo" />
      <h3 hlmAlertTitle>AG Grid Enterprise</h3>
      <p hlmAlertDescription>Row grouping, set filters, Excel export and the side bar below need the licence key from the API (see README).</p>
    </div>

    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>Grid</h2>
        <p hlmCardDescription>Grouped by category with totals. Try the column menu, side bar and Excel export.</p>
        <div hlmCardAction>
          <button hlmBtn variant="outline" size="sm" (click)="exportExcel()"><ng-icon name="lucideFileSpreadsheet" />Excel</button>
        </div>
      </div>
      <div hlmCardContent>
        <ag-grid-angular
          class="block h-[480px] w-full"
          [rowData]="rows"
          [columnDefs]="columns"
          [defaultColDef]="defaultColDef"
          [autoGroupColumnDef]="{ minWidth: 200 }"
          [groupDefaultExpanded]="1"
          [grandTotalRow]="'bottom'"
          [sideBar]="sideBar()"
          [cellSelection]="true"
          (gridReady)="onGridReady($event)"
        />
      </div>
    </section>
  `,
})
export class UiReference {
  private readonly layout = inject(LayoutService);
  private gridApi?: GridApi<SampleRow>;

  /** Side bar takes too much room on phones. */
  protected readonly sideBar = computed(() => (this.layout.isDesktop() ? ['columns', 'filters'] : false));

  protected readonly defaultColDef: ColDef = { flex: 1, minWidth: 120, filter: true, enableRowGroup: true };

  protected readonly columns: ColDef<SampleRow>[] = [
    { field: 'category', rowGroup: true, hide: true, filter: 'agSetColumnFilter' },
    { field: 'item', minWidth: 180 },
    { field: 'supplier', filter: 'agSetColumnFilter' },
    { field: 'qty', type: 'numericColumn', aggFunc: 'sum', filter: 'agNumberColumnFilter' },
    {
      field: 'unitCost',
      headerName: 'Unit cost',
      type: 'numericColumn',
      aggFunc: 'avg',
      valueFormatter: (p) => (p.value == null ? '' : Number(p.value).toFixed(2)),
    },
  ];

  protected readonly rows: SampleRow[] = [
    { category: 'Fabric', item: 'Sample fabric A', supplier: 'Supplier 1', qty: 1.45, unitCost: 3.2 },
    { category: 'Fabric', item: 'Sample fabric B', supplier: 'Supplier 2', qty: 0.3, unitCost: 2.75 },
    { category: 'Trim', item: 'Sample button', supplier: 'Supplier 3', qty: 6, unitCost: 0.04 },
    { category: 'Trim', item: 'Sample zip', supplier: 'Supplier 3', qty: 1, unitCost: 0.35 },
    { category: 'Trim', item: 'Sample label', supplier: 'Supplier 4', qty: 2, unitCost: 0.02 },
    { category: 'Packing', item: 'Sample polybag', supplier: 'Supplier 5', qty: 1, unitCost: 0.03 },
  ];

  protected onGridReady(event: GridReadyEvent<SampleRow>): void {
    this.gridApi = event.api;
  }

  protected exportExcel(): void {
    this.gridApi?.exportDataAsExcel({ fileName: 'ui-reference-sample.xlsx' });
  }

  protected showToast(): void {
    toast.success('Saved', { description: 'This is a sample toast.' });
  }
}
