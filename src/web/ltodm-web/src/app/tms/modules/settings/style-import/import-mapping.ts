import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCircleAlert, lucideSave, lucideTrash2, lucideUpload } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import {
  ImportColumnMapping,
  ImportSheetKey,
  ImportTargetSheet,
  SavedImportMapping,
  StyleImportService,
  WorkbookInspection,
  WorkbookSheet,
  importErrorMessage,
} from '@services/style-import.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';

/** Lower case, letters and digits only ("Season_ID" = "Season ID"), as the API matches headings. */
const normalize = (text: string) => text.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');

const clone = (m: ImportColumnMapping): ImportColumnMapping => JSON.parse(JSON.stringify(m)) as ImportColumnMapping;

/**
 * Settings > Import, step 2: which workbook sheet feeds Style Header / Article / BOM Detail, and which database column
 * each Excel column goes to. Starts from the API's suggestion (a saved mapping that fits, otherwise matched by heading);
 * a mapping can be saved by name for the next file in the same layout (e.g. the GO style export).
 */
@Component({
  selector: 'app-import-mapping',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, NgIcon, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSpinnerImports],
  providers: [provideIcons({ lucideArrowLeft, lucideCircleAlert, lucideSave, lucideTrash2, lucideUpload })],
  templateUrl: './import-mapping.html',
})
export class ImportMapping {
  private readonly svc = inject(StyleImportService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly inspection = input.required<WorkbookInspection>();
  readonly fileName = input('');
  /** The upload is running (disables the form). */
  readonly busy = input(false);
  readonly back = output<void>();
  readonly confirmed = output<ImportColumnMapping>();
  /** Every change, so the page can save the mapping as it goes (resumable). */
  readonly edited = output<ImportColumnMapping>();

  readonly mapping = linkedSignal(() => clone(this.inspection().mapping));
  readonly saved = linkedSignal<SavedImportMapping[]>(() => this.inspection().saved);
  /** The saved mapping in use (null = the suggestion, or edited). */
  readonly selectedId = linkedSignal<number | null>(() => this.inspection().mappingId);
  readonly saveName = linkedSignal(() => this.saved().find((m) => m.mappingId === this.selectedId())?.name ?? '');
  readonly saving = signal(false);

  /** Required columns without an Excel column, as "Sheet › heading". */
  readonly missing = computed(() =>
    this.inspection().targets.flatMap((t) => {
      const m = this.mapping().sheets[t.key];
      if (!m?.source || !this.sheetOf(t.key)) return [`${t.label}`];
      return t.columns.filter((c) => c.required && !m.columns[c.name]).map((c) => `${t.label} › ${c.heading}`);
    }),
  );

  sheetOf(key: ImportSheetKey): WorkbookSheet | undefined {
    const source = this.mapping().sheets[key]?.source?.trim().toLowerCase();
    return source ? this.inspection().sheets.find((s) => s.name.trim().toLowerCase() === source) : undefined;
  }

  /** The column an Excel heading goes to on this import sheet ('' = not imported). */
  targetOf(key: ImportSheetKey, heading: string): string {
    const columns = this.mapping().sheets[key]?.columns ?? {};
    return Object.keys(columns).find((name) => columns[name]?.toLowerCase() === heading.toLowerCase()) ?? '';
  }

  /** Columns the import can fill that no Excel column feeds (optional ones; required ones are in missing()). */
  notInFile(t: ImportTargetSheet): string {
    const m = this.mapping().sheets[t.key];
    return t.columns.filter((c) => !c.required && !m?.columns[c.name]).map((c) => c.heading).join(', ');
  }

  /** Another sheet: columns are matched again by heading (template heading or column name). */
  setSource(t: ImportTargetSheet, source: string): void {
    const sheet = this.inspection().sheets.find((s) => s.name === source);
    const columns: Record<string, string | null> = {};
    const taken = new Set<string>();
    for (const c of t.columns) {
      const keys = [normalize(c.heading), normalize(c.name)];
      const hit = sheet?.columns.find((h) => !taken.has(h.heading) && keys.includes(normalize(h.heading)));
      columns[c.name] = hit?.heading ?? null;
      if (hit) taken.add(hit.heading);
    }
    this.edit(t.key, { source: source || null, columns });
  }

  /** Sends an Excel column to a database column (or nowhere); the column it fed before is freed. */
  setTarget(key: ImportSheetKey, heading: string, target: string): void {
    const columns = { ...(this.mapping().sheets[key]?.columns ?? {}) };
    for (const name of Object.keys(columns)) if (columns[name]?.toLowerCase() === heading.toLowerCase()) columns[name] = null;
    if (target) columns[target] = heading;
    this.edit(key, { source: this.mapping().sheets[key]?.source ?? null, columns });
  }

  private edit(key: ImportSheetKey, sheet: ImportColumnMapping['sheets'][ImportSheetKey]): void {
    this.mapping.update((m) => ({ sheets: { ...m.sheets, [key]: sheet } }));
    this.selectedId.set(null);
    this.edited.emit(this.mapping());
  }

  /** Applies a saved mapping, or ('') the suggestion the page opened with. */
  useSaved(id: string): void {
    const saved = this.saved().find((m) => String(m.mappingId) === id);
    this.mapping.set(clone(saved?.mapping ?? this.inspection().mapping));
    this.selectedId.set(saved?.mappingId ?? null);
    this.saveName.set(saved?.name ?? '');
    this.edited.emit(this.mapping());
  }

  save(): void {
    const name = this.saveName().trim();
    if (!name || this.saving()) return;
    this.saving.set(true);
    this.svc.saveMapping(name, this.mapping()).subscribe({
      next: (m) => {
        this.saving.set(false);
        this.saved.update((list) => [m, ...list.filter((x) => x.mappingId !== m.mappingId)]);
        this.selectedId.set(m.mappingId);
        toast.success(this.transloco.translate('settings.import.map.savedToast', { name: m.name }));
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  async remove(): Promise<void> {
    const m = this.saved().find((x) => x.mappingId === this.selectedId());
    if (!m) return;
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.import.map.deleteTitle'),
      message: this.transloco.translate('settings.import.map.deleteMessage', { name: m.name }),
      confirmLabel: this.transloco.translate('settings.import.map.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteMapping(m.mappingId).subscribe({
      next: () => {
        this.saved.update((list) => list.filter((x) => x.mappingId !== m.mappingId));
        this.selectedId.set(null);
      },
      error: (e) => toast.error(importErrorMessage(e).title),
    });
  }
}
