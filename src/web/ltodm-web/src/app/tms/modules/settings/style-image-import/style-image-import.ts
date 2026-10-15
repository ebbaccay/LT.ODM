import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeftRight,
  lucideCircleAlert,
  lucideCircleCheck,
  lucideEye,
  lucideFolderOpen,
  lucideImage,
  lucideImageUp,
  lucideMaximize,
  lucideRotateCcw,
  lucideSave,
  lucideScanSearch,
  lucideTriangleAlert,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import {
  CropBox,
  ExistingPicture,
  ExistingPicturesCheck,
  PictureField,
  StyleImageAnalysis,
  StyleImageImportService,
  StyleImageImportStatus,
  StyleImageMatch,
  StyleImageSaveResult,
  imageImportErrorMessage,
} from '@services/style-image-import.service';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { firstValueFrom } from 'rxjs';
import { CropBoxEditor } from './crop-box-editor';

type RowStatus = 'waiting' | 'analysing' | 'ready' | 'error' | 'saving' | 'saved';
type Filter = 'all' | 'check' | 'noStyle' | 'ready' | 'saved';
/** Auto: sketches to the sketch, photos to the picture, as detected per file. */
type SaveAs = 'Auto' | PictureField;

interface Row {
  id: number;
  file: File;
  /** Object URL of the file (shown in the editor). */
  url: string;
  status: RowStatus;
  error: string | null;
  analysis: StyleImageAnalysis | null;
  /** Garment box being edited; null = whole picture. */
  box: CropBox | null;
  /** Where this file goes: from Save as (Auto = detected), or switched on the row. */
  field: PictureField;
  ticked: ReadonlySet<number>;
  /** Object URL of the server's rendered result (Preview). */
  previewUrl: string | null;
  previewing: boolean;
  result: StyleImageSaveResult | null;
}

const IMAGE_EXT = /\.(jpe?g|png|gif|webp)$/i;
/** Files analysed at the same time (each takes 1-6 s of server CPU). */
const PARALLEL = 2;
const PAGE = 40;

const MATCH_TONE: Record<string, Tone> = { Exact: 'green', Suffix: 'teal', Variant: 'amber', Root: 'amber' };

/**
 * Settings > Import pictures (Admin): style pictures from a folder of files named after the style
 * (RC2606OW001_Img01.jpg -> RC2606OW001, its _H / -H / _L versions too). Each file is analysed on the server: styles
 * found from the name, and on sheets that hold other things (checkpoint tables, screenshots) the garment box. The admin
 * checks the box (drag to change), ticks the styles and saves: the server cuts the picture, makes small results sharper
 * and sets it on the styles ticked. Nothing changes before Save.
 */
@Component({
  selector: 'app-style-image-import',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, NgIcon, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmSpinnerImports, AuthImgDirective, CropBoxEditor],
  providers: [
    provideIcons({
      lucideArrowLeftRight, lucideScanSearch, lucideCircleAlert, lucideCircleCheck, lucideEye, lucideFolderOpen, lucideImage, lucideImageUp, lucideMaximize, lucideRotateCcw, lucideSave,
      lucideTriangleAlert, lucideX,
    }),
  ],
  templateUrl: './style-image-import.html',
})
export class StyleImageImport implements OnInit {
  private readonly svc = inject(StyleImageImportService);
  private readonly transloco = inject(TranslocoService);

  readonly toneBadge = toneBadge;
  readonly matchTone = (m: string) => MATCH_TONE[m] ?? 'neutral';

  readonly status = signal<StyleImageImportStatus | null>(null);
  readonly rows = signal<Row[]>([]);
  readonly dragging = signal(false);
  readonly filter = signal<Filter>('all');
  /** Hides files whose style number is not in the library (still listed under "No style found"). */
  readonly libraryOnly = signal(true);
  readonly shown = signal(PAGE);

  // Options for every picture saved.
  readonly saveAs = signal<SaveAs>('Auto');
  readonly saveAsOptions: { id: SaveAs; label: string }[] = [
    { id: 'Auto', label: 'settings.imageImport.asAuto' },
    { id: 'Image', label: 'settings.imageImport.asPicture' },
    { id: 'Sketch', label: 'settings.imageImport.asSketch' },
  ];
  readonly overwrite = signal(false);
  readonly enhance = signal(true);

  readonly saving = signal(false);
  /** Save progress: pictures to save in this run, and how many are done (saved or failed). */
  readonly saveTotal = signal(0);
  readonly saveDone = signal(0);
  readonly savePercent = computed(() => (this.saveTotal() ? Math.round((this.saveDone() / this.saveTotal()) * 100) : 0));
  private nextId = 1;
  private queue: number[] = [];
  private workers = 0;

  readonly counts = computed(() => {
    const rows = this.rows();
    return {
      all: this.libraryOnly() ? rows.filter((r) => !this.noStyle(r)).length : rows.length,
      analysed: rows.filter((r) => r.status !== 'waiting' && r.status !== 'analysing').length,
      check: rows.filter((r) => this.needsCheck(r)).length,
      noStyle: rows.filter((r) => this.noStyle(r)).length,
      ready: rows.filter((r) => this.isReady(r)).length,
      saved: rows.filter((r) => r.status === 'saved').length,
    };
  });
  readonly analysing = computed(() => this.rows().some((r) => r.status === 'waiting' || r.status === 'analysing'));
  readonly filtered = computed(() => {
    const f = this.filter();
    const hideNoStyle = this.libraryOnly() && f !== 'noStyle';
    return this.rows().filter((r) => hideNoStyle && this.noStyle(r) ? false :
      f === 'all' ? true : f === 'check' ? this.needsCheck(r) : f === 'noStyle' ? this.noStyle(r) : f === 'ready' ? this.isReady(r) : r.status === 'saved');
  });
  readonly visible = computed(() => this.filtered().slice(0, this.shown()));
  readonly filters: { id: Filter; label: string }[] = [
    { id: 'all', label: 'settings.imageImport.filterAll' },
    { id: 'check', label: 'settings.imageImport.filterCheck' },
    { id: 'noStyle', label: 'settings.imageImport.filterNoStyle' },
    { id: 'ready', label: 'settings.imageImport.filterReady' },
    { id: 'saved', label: 'settings.imageImport.filterSaved' },
  ];

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      for (const r of this.rows()) this.revoke(r);
    });
  }

  ngOnInit(): void {
    this.svc.status().subscribe({ next: (s) => this.status.set(s), error: () => this.status.set(null) });
  }

  // ── Check existing pictures ────────────────────────────────
  readonly checking = signal(false);
  readonly moving = signal(false);
  readonly existing = signal<ExistingPicturesCheck | null>(null);
  /** Suggestions ticked for moving (styleId); sure ones start ticked, conflicts cannot be ticked. */
  readonly existingTicked = signal<ReadonlySet<number>>(new Set());

  checkExisting(): void {
    this.checking.set(true);
    this.svc.checkExisting().subscribe({
      next: (c) => {
        this.existing.set(c);
        this.existingTicked.set(new Set(c.suggestions.filter((s) => s.sure).map((s) => s.styleId)));
        this.checking.set(false);
      },
      error: (err) => {
        this.checking.set(false);
        toast.error(imageImportErrorMessage(err));
      },
    });
  }

  toggleExisting(s: ExistingPicture): void {
    if (s.action === 'Conflict') return;
    const next = new Set(this.existingTicked());
    if (next.has(s.styleId)) next.delete(s.styleId);
    else next.add(s.styleId);
    this.existingTicked.set(next);
  }

  moveExisting(): void {
    const c = this.existing();
    if (!c) return;
    const items = c.suggestions.filter((s) => this.existingTicked().has(s.styleId));
    if (!items.length) return;
    this.moving.set(true);
    this.svc.moveExisting(items).subscribe({
      next: (r) => {
        this.moving.set(false);
        const done = new Set(r.moved);
        this.existing.set({ ...c, suggestions: c.suggestions.filter((s) => !done.has(s.styleId)) });
        this.existingTicked.update((t) => new Set([...t].filter((id) => !done.has(id))));
        const message = this.transloco.translate('settings.imageImport.existingMoved', { moved: r.moved.length });
        if (r.skipped.length) toast.warning(message + ' ' + this.transloco.translate('settings.imageImport.existingSkipped', { count: r.skipped.length }));
        else toast.success(message);
      },
      error: (err) => {
        this.moving.set(false);
        toast.error(imageImportErrorMessage(err));
      },
    });
  }

  /** The stored file the suggestion is about (the one in the wrong place). */
  existingFile(s: ExistingPicture): string | null {
    return s.action === 'MoveToPicture' ? s.sketchUrl : s.imageUrl;
  }

  // ── Adding files ───────────────────────────────────────────
  onFileInput(e: Event): void {
    const input = e.target as HTMLInputElement;
    this.add(Array.from(input.files ?? []));
    input.value = '';
  }

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    this.dragging.set(true);
  }

  async onDrop(e: DragEvent): Promise<void> {
    e.preventDefault();
    this.dragging.set(false);
    const items = Array.from(e.dataTransfer?.items ?? []);
    const entries = items.map((i) => i.webkitGetAsEntry?.()).filter((x): x is FileSystemEntry => !!x);
    const files = entries.length ? (await Promise.all(entries.map(readEntry))).flat() : Array.from(e.dataTransfer?.files ?? []);
    this.add(files);
  }

  private add(files: File[]): void {
    const known = new Set(this.rows().map((r) => r.file.name + '|' + r.file.size));
    const pictures = files.filter((f) => IMAGE_EXT.test(f.name) && !known.has(f.name + '|' + f.size));
    const skipped = files.length - pictures.length;
    if (skipped > 0) toast.info(this.transloco.translate('settings.imageImport.skippedFiles', { count: skipped }));
    if (!pictures.length) return;
    pictures.sort((a, b) => a.name.localeCompare(b.name));
    const rows: Row[] = pictures.map((file) => ({
      id: this.nextId++, file, url: URL.createObjectURL(file), status: 'waiting', error: null, analysis: null, box: null, field: 'Image',
      ticked: new Set(), previewUrl: null, previewing: false, result: null,
    }));
    this.rows.update((list) => [...list, ...rows]);
    this.queue.push(...rows.map((r) => r.id));
    while (this.workers < PARALLEL) void this.work();
  }

  private async work(): Promise<void> {
    this.workers++;
    try {
      for (let id = this.queue.shift(); id !== undefined; id = this.queue.shift()) {
        const row = this.find(id);
        if (!row) continue;
        this.patch(id, { status: 'analysing' });
        try {
          const a = await firstValueFrom(this.svc.analyse(row.file));
          this.patch(id, {
            status: 'ready', analysis: a, box: a.crop.box, field: this.fieldFor(a),
            ticked: new Set(a.matches.filter((m) => m.ticked).map((m) => m.styleId)),
          });
        } catch (err) {
          this.patch(id, { status: 'error', error: imageImportErrorMessage(err) });
        }
      }
    } finally {
      this.workers--;
    }
  }

  remove(r: Row): void {
    this.revoke(r);
    this.queue = this.queue.filter((id) => id !== r.id);
    this.rows.update((list) => list.filter((x) => x.id !== r.id));
  }

  clear(): void {
    for (const r of this.rows()) this.revoke(r);
    this.queue = [];
    this.rows.set([]);
    this.shown.set(PAGE);
  }

  /** Sets Save as for every picture not saved yet (Auto = back to what was detected). */
  setSaveAs(value: SaveAs): void {
    this.saveAs.set(value);
    this.rows.update((list) =>
      list.map((r) => (r.analysis && r.status !== 'saved' && r.status !== 'saving' ? { ...r, field: this.fieldFor(r.analysis) } : r)));
  }

  private fieldFor(a: StyleImageAnalysis): PictureField {
    const v = this.saveAs();
    return v !== 'Auto' ? v : a.kind.kind === 'Sketch' ? 'Sketch' : 'Image';
  }

  // ── Per picture ────────────────────────────────────────────
  setField(r: Row, field: PictureField): void {
    this.patch(r.id, { field });
  }

  setBox(r: Row, box: CropBox | null): void {
    this.patch(r.id, { box, ...this.dropPreview(r) });
  }

  wholePicture(r: Row): void {
    this.setBox(r, null);
  }

  resetBox(r: Row): void {
    this.setBox(r, r.analysis?.crop.box ?? null);
  }

  toggle(r: Row, m: StyleImageMatch): void {
    const next = new Set(r.ticked);
    if (next.has(m.styleId)) next.delete(m.styleId);
    else next.add(m.styleId);
    this.patch(r.id, { ticked: next });
  }

  async preview(r: Row): Promise<void> {
    this.patch(r.id, { previewing: true });
    try {
      const blob = await firstValueFrom(this.svc.preview(r.file, r.box, this.enhance()));
      const current = this.find(r.id);
      if (current?.previewUrl) URL.revokeObjectURL(current.previewUrl);
      this.patch(r.id, { previewUrl: URL.createObjectURL(blob), previewing: false });
    } catch (err) {
      this.patch(r.id, { previewing: false });
      toast.error(imageImportErrorMessage(err));
    }
  }

  closePreview(r: Row): void {
    this.patch(r.id, this.dropPreview(r));
  }

  // ── Save ───────────────────────────────────────────────────
  async saveAll(): Promise<void> {
    const ids = this.rows().filter((r) => this.isReady(r)).map((r) => r.id);
    if (!ids.length) return;
    this.saving.set(true);
    this.saveTotal.set(ids.length);
    this.saveDone.set(0);
    let pictures = 0;
    let styles = 0;
    let kept = 0;
    let failed = 0;
    for (const id of ids) {
      const r = this.find(id);
      if (!r || !this.isReady(r)) continue;
      this.patch(id, { status: 'saving', error: null });
      try {
        const result = await firstValueFrom(this.svc.save(r.file, {
          box: r.box, enhance: this.enhance(), field: r.field, overwrite: this.overwrite(), styleIds: [...r.ticked],
        }));
        this.patch(id, { status: 'saved', result, error: null });
        pictures++;
        styles += result.updated.length;
        kept += result.skipped.length;
      } catch (err) {
        this.patch(id, { status: 'ready', error: imageImportErrorMessage(err) });
        failed++;
      }
      this.saveDone.update((n) => n + 1);
    }
    this.saving.set(false);
    const message = this.transloco.translate('settings.imageImport.savedToast', { pictures, styles, kept });
    if (failed) toast.error(message + ' ' + this.transloco.translate('settings.imageImport.failedToast', { count: failed }));
    else toast.success(message);
  }

  // ── Helpers for the template ───────────────────────────────
  needsCheck(r: Row): boolean {
    const a = r.analysis;
    return r.status !== 'saved' && !!a && a.matches.length > 0 && (!a.crop.sure || (this.saveAs() === 'Auto' && !this.kindSure(a)));
  }

  noStyle(r: Row): boolean {
    return !!r.analysis && r.analysis.matches.length === 0;
  }

  isReady(r: Row): boolean {
    return r.status === 'ready' && r.ticked.size > 0;
  }

  /** Detected kind is clear (a sure Sketch or Photo). */
  kindSure(a: StyleImageAnalysis): boolean {
    return a.kind.kind !== null && a.kind.sure;
  }

  /** The style already has a picture of the kind being saved, so it is kept unless Replace is on. */
  hasExisting(r: Row, m: StyleImageMatch): boolean {
    return !!this.existingUrl(r, m);
  }

  existingUrl(r: Row, m: StyleImageMatch): string | null {
    return r.field === 'Image' ? m.imageUrl : m.sketchUrl;
  }

  boxLabel(r: Row): string {
    const b = r.box;
    return b
      ? this.transloco.translate('settings.imageImport.boxLabel', { width: Math.round(b.width), height: Math.round(b.height) })
      : this.transloco.translate('settings.imageImport.wholePicture');
  }

  showMore(): void {
    this.shown.update((n) => n + PAGE);
  }

  private find(id: number): Row | undefined {
    return this.rows().find((r) => r.id === id);
  }

  private patch(id: number, change: Partial<Row>): void {
    this.rows.update((list) => list.map((r) => (r.id === id ? { ...r, ...change } : r)));
  }

  private dropPreview(r: Row): Partial<Row> {
    const current = this.find(r.id);
    if (current?.previewUrl) URL.revokeObjectURL(current.previewUrl);
    return { previewUrl: null };
  }

  private revoke(r: Row): void {
    URL.revokeObjectURL(r.url);
    if (r.previewUrl) URL.revokeObjectURL(r.previewUrl);
  }
}

/** Files of a dropped file or folder (folders read recursively). */
async function readEntry(entry: FileSystemEntry): Promise<File[]> {
  if (entry.isFile) return [await new Promise<File>((resolve, reject) => (entry as FileSystemFileEntry).file(resolve, reject))];
  if (!entry.isDirectory) return [];
  const reader = (entry as FileSystemDirectoryEntry).createReader();
  const all: FileSystemEntry[] = [];
  // readEntries returns at most ~100 entries per call; read until empty.
  for (;;) {
    const batch = await new Promise<FileSystemEntry[]>((resolve, reject) => reader.readEntries(resolve, reject));
    if (!batch.length) break;
    all.push(...batch);
  }
  return (await Promise.all(all.map(readEntry))).flat();
}
