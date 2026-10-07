import { AllCommunityModule, ModuleRegistry, provideGlobalGridOptions, themeQuartz } from 'ag-grid-community';
import {
  AggregationModule,
  CellSelectionModule,
  ClipboardModule,
  ColumnMenuModule,
  ColumnsToolPanelModule,
  ContextMenuModule,
  ExcelExportModule,
  FiltersToolPanelModule,
  LicenseManager,
  MasterDetailModule,
  MultiFilterModule,
  RichSelectModule,
  RowGroupingModule,
  RowGroupingPanelModule,
  ServerSideRowModelModule,
  SetFilterModule,
  SideBarModule,
  StatusBarModule,
  TreeDataModule,
} from 'ag-grid-enterprise';
import type { ClientConfigService } from '../config/client-config.service';

/**
 * AG Grid theme built from the Spartan CSS variables, so grids follow light/dark mode,
 * the accent colour and the radius chosen in the theme panel.
 */
export const ltodmGridTheme = themeQuartz.withParams({
  fontFamily: 'inherit',
  backgroundColor: 'var(--card)',
  foregroundColor: 'var(--card-foreground)',
  accentColor: 'var(--primary)',
  borderColor: 'var(--border)',
  headerBackgroundColor: 'var(--muted)',
  headerTextColor: 'var(--foreground)',
  chromeBackgroundColor: 'var(--muted)',
  borderRadius: 'var(--radius)',
  wrapperBorderRadius: 'var(--radius)',
  inputBorder: { color: 'var(--input)' },
  inputFocusBorder: { color: 'var(--ring)' },
});

let ready: Promise<void> | null = null;

/**
 * Registers the AG Grid modules, the shared theme and the licence key (once).
 * Integrated Charts and Sparklines are deliberately NOT registered: the LT licence does not cover them.
 * Loaded lazily by agGridGuard so AG Grid stays out of the initial bundle.
 */
export function setUpAgGrid(clientConfig: ClientConfigService): Promise<void> {
  ready ??= (async () => {
    ModuleRegistry.registerModules([
      AllCommunityModule,
      RowGroupingModule,
      RowGroupingPanelModule,
      AggregationModule,
      TreeDataModule,
      MasterDetailModule,
      ServerSideRowModelModule,
      SetFilterModule,
      MultiFilterModule,
      ColumnMenuModule,
      ContextMenuModule,
      SideBarModule,
      ColumnsToolPanelModule,
      FiltersToolPanelModule,
      StatusBarModule,
      CellSelectionModule,
      ClipboardModule,
      ExcelExportModule,
      RichSelectModule,
    ]);
    provideGlobalGridOptions({ theme: ltodmGridTheme });

    const config = await clientConfig.load();
    if (config?.agGridLicenseKey) {
      LicenseManager.setLicenseKey(config.agGridLicenseKey);
    }
    // No key (or API unreachable): grids still work but show the AG Grid watermark.
  })();
  return ready;
}
