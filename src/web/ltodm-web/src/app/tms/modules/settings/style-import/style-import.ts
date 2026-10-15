import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideCircleAlert,
  lucideCircleCheck,
  lucideDownload,
  lucideFileSpreadsheet,
  lucideSearch,
  lucideSparkles,
  lucideTableProperties,
  lucideTrash2,
  lucideTriangleAlert,
  lucideUndo2,
  lucideUpload,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import {
  ImportCodeList,
  ImportColumnMapping,
  ImportCodeSuggestion,
  ImportNewCode,
  ImportNewCodes,
  ImportStyleAction,
  StyleImportBatch,
  StyleImportIssue,
  StyleImportService,
  StyleImportStyle,
  RollbackBlocker,
  WorkbookInspection,
  importErrorMessage,
  rollbackBlockers,
} from '@services/style-import.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { ImportDecisions } from './import-decisions';
import { ImportMapping } from './import-mapping';

const PAGE = 100;

const ACTION_TONE: Record<ImportStyleAction, Tone> = { New: 'green', Changed: 'amber', Unchanged: 'neutral', Blocked: 'red' };

/**
 * Settings > Import (Admin): loads the Style Library workbook (Style Header, Article, BOM Detail sheets).
 * Choose a file -> Map columns (which sheet and Excel column feeds each database column; saved mappings are reused)
 * -> Upload -> the API stages and checks every row -> preview (what happens to each style, issues by sheet and row)
 * -> Commit writes New styles and the Changed styles ticked here; Unchanged and Blocked styles are left alone.
 * Nothing reaches the library before Commit. An unfinished import can be reopened from Recent imports.
 */
@Component({
  selector: 'app-style-import',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, TranslocoPipe, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports,
    HlmInputImports, HlmSkeletonImports, HlmSpinnerImports, ImportMapping, ImportDecisions,
  ],
  providers: [
    provideIcons({
      lucideArrowRight, lucideCircleAlert, lucideCircleCheck, lucideDownload, lucideFileSpreadsheet, lucideSearch, lucideSparkles, lucideTableProperties, lucideTrash2, lucideTriangleAlert, lucideUndo2, lucideUpload, lucideX,
    }),
  ],
  templateUrl: './style-import.html',
})
export class StyleImport implements OnInit {
  private readonly svc = inject(StyleImportService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly templateUrl = this.svc.templateUrl;
  readonly toneBadge = toneBadge;
  readonly actionTone = (a: ImportStyleAction) => ACTION_TONE[a];

  // ── Upload ─────────────────────────────────────────────────
  readonly file = signal<File | null>(null);
  readonly dragging = signal(false);
  readonly uploading = signal(false);
  readonly uploadError = signal<{ title: string; problems: string[] } | null>(null);
  /** The chosen file's sheets and headings, while its columns are being mapped. */
  readonly inspection = signal<WorkbookInspection | null>(null);
  readonly inspecting = signal(false);
  /** Mapping edits waiting to be saved (saved shortly after the last change, and when leaving the step). */
  private pendingMapping: ImportColumnMapping | null = null;
  private saveTimer: ReturnType<typeof setTimeout> | null = null;

  // ── Batches ────────────────────────────────────────────────
  readonly recent = signal<StyleImportBatch[]>([]);
  readonly recentLoading = signal(false);
  readonly batch = signal<StyleImportBatch | null>(null);
  readonly committing = signal(false);
  readonly rollingBack = signal(false);
  /** Why the last rollback attempt stopped (nothing was changed). */
  readonly blockers = signal<RollbackBlocker[] | null>(null);

  // ── Preview ────────────────────────────────────────────────
  readonly tab = signal<'styles' | 'issues' | 'codes'>('styles');
  readonly actionFilter = signal<ImportStyleAction | null>(null);
  readonly search = signal('');
  readonly styles = signal<StyleImportStyle[]>([]);
  readonly stylesTotal = signal(0);
  readonly stylesLoading = signal(false);
  readonly severityFilter = signal<'Error' | 'Warning' | null>(null);
  readonly issues = signal<StyleImportIssue[]>([]);
  readonly issuesTotal = signal(0);
  readonly issuesLoading = signal(false);
  /** Changed styles ticked for overwrite (styleKey). */
  readonly overwrite = signal<ReadonlySet<string>>(new Set());
  readonly selectingAll = signal(false);

  // ── New codes ──────────────────────────────────────────────
  readonly codes = signal<ImportNewCodes | null>(null);
  readonly codesFailed = signal(false);
  readonly suggestions = signal<ReadonlyMap<string, ImportCodeSuggestion>>(new Map());
  readonly suggesting = signal(false);
  readonly mapping = signal<string | null>(null);
  /** Keep answers (Use, Use all, grouping) for later imports. */
  readonly remember = signal(true);
  /** Code picked in "use existing", per list|value. */
  readonly picked = signal<Record<string, string>>({});
  readonly unmatched = computed(() => (this.codes()?.codes ?? []).filter((c) => !c.suggestedCode && !this.suggestions().get(this.codeKey(c))).length);
  codeKey = (c: { list: string; value: string }) => `${c.list}|${c.value}`;
  optionsFor = (list: ImportCodeList) => (this.codes()?.options ?? []).filter((o) => o.list === list);

  readonly summary = computed(() => this.batch()?.summary ?? null);
  readonly isStaged = computed(() => this.batch()?.status === 'Staged');
  readonly toWrite = computed(() => (this.summary()?.styles.new ?? 0) + this.overwrite().size);
  readonly actionFilters: { id: ImportStyleAction | null; label: string }[] = [
    { id: null, label: 'settings.import.filterAll' },
    { id: 'New', label: 'settings.import.actionNew' },
    { id: 'Changed', label: 'settings.import.actionChanged' },
    { id: 'Unchanged', label: 'settings.import.actionUnchanged' },
    { id: 'Blocked', label: 'settings.import.actionBlocked' },
  ];

  ngOnInit(): void {
    this.loadRecent();
  }

  loadRecent(): void {
    this.recentLoading.set(true);
    this.svc.list().subscribe({
      next: (list) => {
        this.recent.set(list);
        this.recentLoading.set(false);
      },
      error: (e) => {
        this.recentLoading.set(false);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  // ── Upload ─────────────────────────────────────────────────

  onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.pick(input.files?.[0] ?? null);
    input.value = '';
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.pick(event.dataTransfer?.files?.[0] ?? null);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  private pick(file: File | null): void {
    this.uploadError.set(null);
    this.inspection.set(null);
    if (!file) return;
    if (!file.name.toLowerCase().endsWith('.xlsx')) {
      this.uploadError.set({ title: this.transloco.translate('settings.import.onlyXlsx'), problems: [] });
      return;
    }
    this.file.set(file);
  }

  /** Reads the file's sheets and headings for the Map columns step. */
  inspect(): void {
    const file = this.file();
    if (!file || this.inspecting()) return;
    this.inspecting.set(true);
    this.uploadError.set(null);
    this.svc.inspect(file).subscribe({
      next: (inspection) => {
        this.inspecting.set(false);
        this.file.set(null);
        this.inspection.set(inspection);
        this.loadRecent();
      },
      error: (e) => {
        this.inspecting.set(false);
        this.uploadError.set(importErrorMessage(e));
      },
    });
  }

  /** Stages the kept workbook with the mapping and opens the result. */
  upload(mapping: ImportColumnMapping): void {
    const insp = this.inspection();
    if (!insp || this.uploading()) return;
    this.cancelSave();
    this.uploading.set(true);
    this.uploadError.set(null);
    this.svc.stage(insp.batchId, mapping).subscribe({
      next: (batch) => {
        this.uploading.set(false);
        this.file.set(null);
        this.inspection.set(null);
        this.open(batch);
        this.loadRecent();
      },
      error: (e) => {
        this.uploading.set(false);
        this.uploadError.set(importErrorMessage(e));
      },
    });
  }

  /** Saves the mapping shortly after the last change (Recent imports can resume it). */
  mappingEdited(mapping: ImportColumnMapping): void {
    this.pendingMapping = mapping;
    if (this.saveTimer) clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.flushMapping(), 800);
  }

  private flushMapping(): void {
    const insp = this.inspection();
    const mapping = this.pendingMapping;
    this.cancelSave();
    if (insp && mapping) this.svc.saveBatchMapping(insp.batchId, mapping).subscribe({ error: (e) => toast.error(importErrorMessage(e).title) });
  }

  private cancelSave(): void {
    if (this.saveTimer) clearTimeout(this.saveTimer);
    this.saveTimer = null;
    this.pendingMapping = null;
  }

  /** Leaves Map columns; the workbook and the mapping so far stay with the import (Recent imports). */
  leaveMapping(): void {
    this.flushMapping();
    this.inspection.set(null);
    this.uploadError.set(null);
    this.loadRecent();
  }

  /** Opens Map columns for an import that was not committed (its kept workbook, its mapping). */
  editMapping(batchId: number): void {
    this.uploadError.set(null);
    this.svc.reinspect(batchId).subscribe({
      next: (inspection) => {
        this.batch.set(null);
        this.file.set(null);
        this.inspection.set(inspection);
      },
      error: (e) => toast.error(importErrorMessage(e).title),
    });
  }

  /** Deletes an import that was not committed, with its kept workbook. */
  async deleteImport(r: StyleImportBatch): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.import.delete.title'),
      message: this.transloco.translate('settings.import.delete.message', { file: r.fileName }),
      confirmLabel: this.transloco.translate('settings.import.delete.confirm'),
      cancelLabel: this.transloco.translate('settings.import.keep'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteImport(r.batchId).subscribe({
      next: () => {
        toast.success(this.transloco.translate('settings.import.delete.done', { file: r.fileName }));
        if (this.batch()?.batchId === r.batchId) this.batch.set(null);
        this.loadRecent();
      },
      error: (e) => toast.error(importErrorMessage(e).title),
    });
  }

  /** Not committed: can be deleted from the list. */
  canDelete = (r: StyleImportBatch) => r.status === 'Mapping' || r.status === 'Staged' || r.status === 'Cancelled';

  /** The workbook is kept and the import not committed: its columns can be mapped (again). */
  canRemap = (r: StyleImportBatch) => r.hasFile && this.canDelete(r);

  // ── Open a batch ───────────────────────────────────────────

  open(batch: StyleImportBatch): void {
    this.batch.set(batch);
    this.blockers.set(null);
    this.overwrite.set(new Set());
    this.actionFilter.set(null);
    this.severityFilter.set(null);
    this.search.set('');
    // Start where attention is needed: blocked styles first if any, else everything.
    this.tab.set('styles');
    this.loadStyles();
    this.loadIssues();
    this.suggestions.set(new Map());
    this.picked.set({});
    this.loadCodes();
  }

  loadCodes(): void {
    const b = this.batch();
    this.codes.set(null);
    this.codesFailed.set(false);
    if (!b || b.status !== 'Staged') return;
    this.svc.newCodes(b.batchId).subscribe({
      next: (c) => this.codes.set(c),
      error: (e) => {
        this.codesFailed.set(true);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  suggestCodes(): void {
    const b = this.batch();
    if (!b || this.suggesting()) return;
    this.suggesting.set(true);
    this.svc.suggestCodes(b.batchId).subscribe({
      next: (list) => {
        this.suggesting.set(false);
        this.suggestions.update((m) => {
          const next = new Map(m);
          for (const s of list) next.set(this.codeKey(s), s);
          return next;
        });
        if (!list.length) toast.info(this.transloco.translate('settings.import.codes.noSuggestions'));
      },
      error: (e) => {
        this.suggesting.set(false);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  /** Uses an existing code for the value: the staged rows change and the batch is checked again. */
  mapCode(c: ImportNewCode, code: string): void {
    const b = this.batch();
    if (!b || !code || this.mapping()) return;
    this.mapping.set(this.codeKey(c));
    this.svc.mapCode(b.batchId, c.list, c.value, code, this.remember()).subscribe({
      next: (batch) => {
        this.mapping.set(null);
        toast.success(this.transloco.translate('settings.import.codes.mapped', { value: c.value, code }));
        this.batch.set(batch);
        this.loadStyles();
        this.loadIssues();
        this.svc.newCodes(batch.batchId).subscribe({ next: (x) => this.codes.set(x), error: () => undefined });
      },
      error: (e) => {
        this.mapping.set(null);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  /** An answer in the questions panel changed the staged rows: show the new check. */
  onAnswered(batch: StyleImportBatch): void {
    this.batch.set(batch);
    this.loadStyles();
    this.loadIssues();
    this.svc.newCodes(batch.batchId).subscribe({ next: (x) => this.codes.set(x), error: () => undefined });
  }

  pickCode(c: ImportNewCode, code: string): void {
    this.picked.update((p) => ({ ...p, [this.codeKey(c)]: code }));
  }

  reopen(batchId: number): void {
    this.svc.get(batchId).subscribe({ next: (b) => this.open(b), error: (e) => toast.error(importErrorMessage(e).title) });
  }

  close(): void {
    this.batch.set(null);
    this.loadRecent();
  }

  setActionFilter(action: ImportStyleAction | null): void {
    this.actionFilter.set(action);
    this.loadStyles();
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.loadStyles();
  }

  setSeverity(severity: 'Error' | 'Warning' | null): void {
    this.severityFilter.set(severity);
    this.loadIssues();
  }

  loadStyles(more = false): void {
    const b = this.batch();
    if (!b) return;
    this.stylesLoading.set(true);
    const skip = more ? this.styles().length : 0;
    this.svc.styles(b.batchId, { action: this.actionFilter(), search: this.search().trim(), skip, take: PAGE }).subscribe({
      next: (page) => {
        this.styles.set(more ? [...this.styles(), ...page.items] : page.items);
        this.stylesTotal.set(page.total);
        this.stylesLoading.set(false);
      },
      error: (e) => {
        this.stylesLoading.set(false);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  loadIssues(more = false): void {
    const b = this.batch();
    if (!b) return;
    this.issuesLoading.set(true);
    const skip = more ? this.issues().length : 0;
    this.svc.issues(b.batchId, { severity: this.severityFilter(), skip, take: PAGE }).subscribe({
      next: (page) => {
        this.issues.set(more ? [...this.issues(), ...page.items] : page.items);
        this.issuesTotal.set(page.total);
        this.issuesLoading.set(false);
      },
      error: (e) => {
        this.issuesLoading.set(false);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  /** Issues for one style (from its row in the styles list). */
  showIssuesFor(style: StyleImportStyle): void {
    const b = this.batch();
    if (!b) return;
    this.tab.set('issues');
    this.severityFilter.set(null);
    this.issuesLoading.set(true);
    this.svc.issues(b.batchId, { styleKey: style.styleKey, take: PAGE }).subscribe({
      next: (page) => {
        this.issues.set(page.items);
        this.issuesTotal.set(page.total);
        this.issuesLoading.set(false);
      },
      error: (e) => {
        this.issuesLoading.set(false);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  // ── Overwrite selection ────────────────────────────────────

  isTicked = (s: StyleImportStyle) => this.overwrite().has(s.styleKey);

  toggle(s: StyleImportStyle): void {
    const next = new Set(this.overwrite());
    if (next.has(s.styleKey)) next.delete(s.styleKey);
    else next.add(s.styleKey);
    this.overwrite.set(next);
  }

  /** Ticks every Changed style in the batch (fetched page by page), or clears the selection. */
  toggleAllChanged(): void {
    const b = this.batch();
    const changed = this.summary()?.styles.changed ?? 0;
    if (!b || changed === 0) return;
    if (this.overwrite().size === changed) {
      this.overwrite.set(new Set());
      return;
    }
    this.selectingAll.set(true);
    const keys = new Set<string>();
    const next = (skip: number) =>
      this.svc.styles(b.batchId, { action: 'Changed', skip, take: 500 }).subscribe({
        next: (page) => {
          page.items.forEach((s) => keys.add(s.styleKey));
          if (keys.size < page.total && page.items.length) next(skip + page.items.length);
          else {
            this.overwrite.set(keys);
            this.selectingAll.set(false);
          }
        },
        error: (e) => {
          this.selectingAll.set(false);
          toast.error(importErrorMessage(e).title);
        },
      });
    next(0);
  }

  // ── Commit / cancel ────────────────────────────────────────

  async commit(): Promise<void> {
    const b = this.batch();
    const s = this.summary();
    if (!b || !s || this.committing() || this.toWrite() === 0) return;
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.import.commitTitle'),
      message: this.transloco.translate('settings.import.commitMessage', {
        newCount: s.styles.new,
        replaceCount: this.overwrite().size,
        skipCount: s.styles.total - this.toWrite(),
      }) + this.newPartnersNote(),
      confirmLabel: this.transloco.translate('settings.import.commit'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: this.overwrite().size ? 'warning' : 'info',
    });
    if (!ok) return;
    this.committing.set(true);
    this.svc.commit(b.batchId, [...this.overwrite()]).subscribe({
      next: (done) => {
        this.committing.set(false);
        this.batch.set(done);
        this.overwrite.set(new Set());
        this.loadStyles();
        this.loadRecent();
        toast.success(this.transloco.translate('settings.import.committedToast', { count: done.summary?.committed?.styles ?? 0 }));
      },
      error: (e) => {
        this.committing.set(false);
        toast.error(importErrorMessage(e).title);
      },
    });
  }

  /** Flags customers and SAP suppliers the commit would add, so adding them is a choice. */
  private newPartnersNote(): string {
    const codes = this.codes()?.codes ?? [];
    const customers = codes.filter((c) => c.list === 'customer').length;
    const suppliers = codes.filter((c) => c.list === 'supplier').length;
    const skipped = this.summary()?.skippedBom?.rows ?? 0;
    return (customers + suppliers ? ' ' + this.transloco.translate('settings.import.ask.commitAdds', { customers, suppliers }) : '')
      + (skipped ? ' ' + this.transloco.translate('settings.import.ask.commitSkips', { rows: skipped }) : '');
  }

  /** Undoes the commit; when styles changed since, lists them instead (nothing is changed). */
  async rollback(): Promise<void> {
    const b = this.batch();
    const c = b?.summary?.committed;
    if (!b || !c || this.rollingBack()) return;
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.import.rollback.title'),
      message: this.transloco.translate('settings.import.rollback.message', { file: b.fileName, newCount: c.new, replaced: c.replaced }),
      confirmLabel: this.transloco.translate('settings.import.rollback.confirm'),
      cancelLabel: this.transloco.translate('settings.import.keep'),
      variant: 'danger',
    });
    if (!ok) return;
    this.rollingBack.set(true);
    this.blockers.set(null);
    this.svc.rollback(b.batchId).subscribe({
      next: (done) => {
        this.rollingBack.set(false);
        this.batch.set(done);
        this.loadStyles();
        this.loadRecent();
        toast.success(this.transloco.translate('settings.import.rollback.done', { removed: done.summary?.rolledBack?.removed ?? 0, restored: done.summary?.rolledBack?.restored ?? 0 }));
      },
      error: (e) => {
        this.rollingBack.set(false);
        const blockers = rollbackBlockers(e);
        if (blockers) this.blockers.set(blockers);
        else toast.error(importErrorMessage(e).title);
      },
    });
  }

  async cancelImport(): Promise<void> {
    const b = this.batch();
    if (!b) return;
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.import.cancelTitle'),
      message: this.transloco.translate('settings.import.cancelMessage', { file: b.fileName }),
      confirmLabel: this.transloco.translate('settings.import.cancelImport'),
      cancelLabel: this.transloco.translate('settings.import.keep'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.cancel(b.batchId).subscribe({
      next: () => this.close(),
      error: (e) => toast.error(importErrorMessage(e).title),
    });
  }

  sheetLabel = (sheet: StyleImportIssue['sheet']) =>
    sheet === 'Style' ? 'Style Header' : sheet === 'Article' ? 'Article' : 'BOM Detail';

  statusTone = (status: StyleImportBatch['status']): Tone =>
    status === 'Committed' ? 'green' : status === 'Staged' ? 'amber' : status === 'Mapping' ? 'primary' : status === 'RolledBack' ? 'red' : 'neutral';
}
