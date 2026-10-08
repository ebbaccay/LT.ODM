import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideDownload,
  lucideFileSpreadsheet,
  lucideHistory,
  lucideLanguages,
  lucideRotateCcw,
  lucideSearch,
  lucideTriangleAlert,
  lucideUpload,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { concat, last } from 'rxjs';
import {
  TranslationImportResult,
  TranslationLanguage,
  TranslationRow,
  TranslationVersion,
  TranslationsAdminService,
  translationsErrorMessage,
} from '@services/translations-admin.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { Modal } from '@tms/shared/ui/modal';
import { saveBlob } from '../../../../features/style-library/style-library.service';

type Filter = 'all' | 'corrected' | 'untranslated' | 'releaseChanged';

const PAGE = 100;
const PLACEHOLDER = /\{\{\s*([\w.]+)\s*\}\}/g;

/** The {{name}} placeholders in a text, sorted and distinct (the API checks the same thing). */
const placeholders = (text: string | null | undefined): string[] =>
  [...new Set([...(text ?? '').matchAll(PLACEHOLDER)].map((m) => m[1]))].sort();

/**
 * Settings > Translations: correct UI texts without a release. Edit one key here, or download the Excel workbook,
 * change it and upload it (checked first, saved on Apply). The deployed files stay the base: only texts that differ
 * from them are kept, so a release still brings its new and reworded texts.
 */
@Component({
  selector: 'app-translations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, TranslocoPipe, NgIcon, Modal, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmLabelImports,
    HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports, HlmTextareaImports,
  ],
  providers: [
    provideIcons({
      lucideCircleAlert, lucideDownload, lucideFileSpreadsheet, lucideHistory, lucideLanguages, lucideRotateCcw, lucideSearch,
      lucideTriangleAlert, lucideUpload,
    }),
  ],
  templateUrl: './translations.html',
})
export class Translations implements OnInit {
  private readonly svc = inject(TranslationsAdminService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly loading = signal(true);
  readonly languages = signal<TranslationLanguage[]>([]);
  readonly rows = signal<TranslationRow[]>([]);
  readonly updatedBy = signal<string | null>(null);
  readonly updatedUtc = signal<string | null>(null);

  readonly search = signal('');
  readonly filter = signal<Filter>('all');
  readonly shown = signal(PAGE);

  readonly correctedCount = computed(() => this.rows().filter((r) => Object.keys(r.overrides).length > 0).length);
  readonly releaseChangedCount = computed(() => this.rows().filter((r) => r.baseChanged.length > 0).length);

  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const filter = this.filter();
    const others = this.languages().filter((l) => l.code !== 'en').map((l) => l.code);
    return this.rows().filter((r) => {
      if (filter === 'corrected' && !Object.keys(r.overrides).length) return false;
      if (filter === 'releaseChanged' && !r.baseChanged.length) return false;
      if (filter === 'untranslated' && !others.some((l) => this.isUntranslated(r, l))) return false;
      if (!q) return true;
      return r.key.toLowerCase().includes(q) || this.languages().some((l) => (this.text(r, l.code) ?? '').toLowerCase().includes(q));
    });
  });
  readonly visible = computed(() => this.filtered().slice(0, this.shown()));

  // ----- edit -----
  readonly editing = signal<TranslationRow | null>(null);
  readonly drafts = signal<Record<string, string>>({});
  readonly saving = signal(false);
  readonly expected = computed(() => {
    const r = this.editing();
    return r ? placeholders(r.base['en'] ?? Object.values(r.base).find((t) => t) ?? '') : [];
  });
  readonly draftProblems = computed(() => {
    const r = this.editing();
    const expected = this.expected().join(',');
    const problems: Record<string, boolean> = {};
    if (!r) return problems;
    for (const [lang, value] of Object.entries(this.drafts())) {
      const blankOrBase = !value.trim() || value.trim() === (r.base[lang] ?? '').trim();
      problems[lang] = !blankOrBase && placeholders(value).join(',') !== expected;
    }
    return problems;
  });
  readonly dirty = computed(() => {
    const r = this.editing();
    return !!r && Object.entries(this.drafts()).some(([lang, value]) => value !== (this.text(r, lang) ?? ''));
  });

  // ----- upload -----
  readonly uploadOpen = signal(false);
  readonly file = signal<File | null>(null);
  readonly dragging = signal(false);
  readonly checking = signal(false);
  readonly applying = signal(false);
  readonly uploadError = signal<string | null>(null);
  readonly preview = signal<TranslationImportResult | null>(null);
  readonly previewErrors = computed(() => this.preview()?.problems.filter((p) => p.severity === 'Error').length ?? 0);
  readonly previewWarnings = computed(() => this.preview()?.problems.filter((p) => p.severity === 'Warning').length ?? 0);

  // ----- history -----
  readonly historyOpen = signal(false);
  readonly historyLoading = signal(false);
  readonly versions = signal<TranslationVersion[]>([]);
  readonly restoring = signal<string | null>(null);

  readonly exporting = signal(false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.table().subscribe({
      next: (t) => {
        this.languages.set(t.languages);
        this.rows.set(t.rows);
        this.updatedBy.set(t.updatedBy);
        this.updatedUtc.set(t.updatedUtc);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(translationsErrorMessage(e));
      },
    });
  }

  setSearch(value: string): void {
    this.search.set(value);
    this.shown.set(PAGE);
  }

  setFilter(value: string | null | undefined): void {
    this.filter.set((value as Filter) ?? 'all');
    this.shown.set(PAGE);
  }

  showMore(): void {
    this.shown.update((n) => n + PAGE);
  }

  /** The text in use: the correction, else the deployed text. */
  text(r: TranslationRow, lang: string): string | null {
    return r.overrides[lang] ?? r.base[lang] ?? null;
  }

  isCorrected = (r: TranslationRow, lang: string): boolean => lang in r.overrides;

  /** {{name}} as text (braces cannot be written inside a template interpolation). */
  braces = (name: string): string => `{{${name}}}`;

  /** Missing, or the same as the English text (not translated yet). */
  isUntranslated(r: TranslationRow, lang: string): boolean {
    if (lang === 'en') return false;
    const t = this.text(r, lang);
    return !t || (t === this.text(r, 'en') && /[A-Za-z]{3}/.test(t));
  }

  // ----- edit -----

  openEdit(r: TranslationRow): void {
    this.editing.set(r);
    this.drafts.set(Object.fromEntries(this.languages().map((l) => [l.code, this.text(r, l.code) ?? ''])));
  }

  closeEdit(): void {
    if (!this.saving()) this.editing.set(null);
  }

  setDraft(lang: string, value: string): void {
    this.drafts.update((d) => ({ ...d, [lang]: value }));
  }

  /** Put the deployed text back into the box (saved with the others). */
  useReleased(lang: string): void {
    this.setDraft(lang, this.editing()?.base[lang] ?? '');
  }

  save(): void {
    const r = this.editing();
    if (!r || this.saving() || Object.values(this.draftProblems()).some(Boolean)) return;
    const calls = Object.entries(this.drafts())
      .filter(([lang, value]) => value !== (this.text(r, lang) ?? ''))
      .map(([lang, value]) => this.svc.set(lang, r.key, value.trim() ? value : null));
    if (!calls.length) {
      this.editing.set(null);
      return;
    }
    this.saving.set(true);
    concat(...calls)
      .pipe(last())
      .subscribe({
        next: (row) => {
          this.saving.set(false);
          this.replaceRow(row);
          this.editing.set(null);
          this.updatedUtc.set(new Date().toISOString());
          toast.success(this.transloco.translate('settings.translations.saved'));
          this.svc.refreshLive();
        },
        error: (e) => {
          this.saving.set(false);
          toast.error(translationsErrorMessage(e));
          this.load();
        },
      });
  }

  private replaceRow(row: TranslationRow): void {
    this.rows.update((rows) => rows.map((x) => (x.key === row.key ? row : x)));
  }

  // ----- export -----

  download(): void {
    if (this.exporting()) return;
    this.exporting.set(true);
    this.svc.export().subscribe({
      next: (blob) => {
        this.exporting.set(false);
        saveBlob(blob, `LT_ODM_translations_${new Date().toISOString().slice(0, 10)}.xlsx`);
      },
      error: (e) => {
        this.exporting.set(false);
        toast.error(translationsErrorMessage(e));
      },
    });
  }

  // ----- upload -----

  openUpload(): void {
    this.file.set(null);
    this.preview.set(null);
    this.uploadError.set(null);
    this.uploadOpen.set(true);
  }

  closeUpload(): void {
    if (!this.applying()) this.uploadOpen.set(false);
  }

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
    this.preview.set(null);
    if (!file) return;
    if (!file.name.toLowerCase().endsWith('.xlsx')) {
      this.uploadError.set(this.transloco.translate('settings.translations.onlyXlsx'));
      return;
    }
    this.file.set(file);
    this.check();
  }

  check(): void {
    const f = this.file();
    if (!f || this.checking()) return;
    this.checking.set(true);
    this.svc.import(f, false).subscribe({
      next: (result) => {
        this.checking.set(false);
        this.preview.set(result);
      },
      error: (e) => {
        this.checking.set(false);
        this.uploadError.set(translationsErrorMessage(e));
      },
    });
  }

  apply(): void {
    const f = this.file();
    if (!f || !this.preview()?.changes.length || this.applying()) return;
    this.applying.set(true);
    this.svc.import(f, true).subscribe({
      next: (result) => {
        this.applying.set(false);
        this.uploadOpen.set(false);
        toast.success(this.transloco.translate('settings.translations.applied', { count: result.changes.length }));
        this.load();
        this.svc.refreshLive();
      },
      error: (e) => {
        this.applying.set(false);
        this.uploadError.set(translationsErrorMessage(e));
      },
    });
  }

  langLabel(code: string | null): string {
    return this.languages().find((l) => l.code === code)?.label ?? code ?? '';
  }

  // ----- history -----

  openHistory(): void {
    this.historyOpen.set(true);
    this.historyLoading.set(true);
    this.svc.history().subscribe({
      next: (v) => {
        this.versions.set(v);
        this.historyLoading.set(false);
      },
      error: (e) => {
        this.historyLoading.set(false);
        toast.error(translationsErrorMessage(e));
      },
    });
  }

  async restore(v: TranslationVersion): Promise<void> {
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.translations.restoreTitle'),
      message: this.transloco.translate('settings.translations.restoreMessage', { count: v.overrides }),
      confirmLabel: this.transloco.translate('settings.translations.restore'),
      cancelLabel: this.transloco.translate('actions.cancel'),
    });
    if (!confirmed) return;
    this.restoring.set(v.id);
    this.svc.restore(v.id).subscribe({
      next: () => {
        this.restoring.set(null);
        this.historyOpen.set(false);
        toast.success(this.transloco.translate('settings.translations.restored'));
        this.load();
        this.svc.refreshLive();
      },
      error: (e) => {
        this.restoring.set(null);
        toast.error(translationsErrorMessage(e));
      },
    });
  }
}
