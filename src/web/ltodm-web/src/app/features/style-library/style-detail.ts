import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideCopy,
  lucideDownload,
  lucideFileText,
  lucideGitBranch,
  lucideGitCompareArrows,
  lucideImage,
  lucideLink,
  lucidePencil,
  lucidePlus,
  lucideSearch,
  lucideShieldAlert,
  lucideTrash2,
  lucideUnlink,
  lucideWandSparkles,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { toneBadge } from '@tms/shared/ui/tones';
import { BomLineDialog } from './bom-line-dialog';
import { ColorwayDialog } from './colorway-dialog';
import { CopyStyleDialog } from './copy-style-dialog';
import { DesignPromptDialog } from './design-prompt-dialog';
import { HistoryDialog } from './history-dialog';
import { StyleFormDialog } from './style-form-dialog';
import { BomLine, Colorway, FamilyMember, GENDERS, StyleDetail as Detail, StyleLibraryService, StyleLookups, styleError } from './style-library.service';

type Tab = 'colorways' | 'bom' | 'history';

interface BomSection {
  code: string;
  /** Empty for lines without a content class (shown as "Unclassified"). */
  name: string;
  lines: BomLine[];
}

/**
 * Style Library > style page: header (sketch, photo, facts, who changed it), colorways, BOM by content class with
 * the material colours of a chosen colorway, and the style's family history. Admins and merchandisers can edit
 * everything here; others read only.
 */
@Component({
  selector: 'app-style-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, NgIcon, AuthImgDirective, Modal, StyleFormDialog, ColorwayDialog, BomLineDialog, CopyStyleDialog,
    HistoryDialog, DesignPromptDialog, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, TranslocoPipe,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft, lucideCopy, lucideDownload, lucideFileText, lucideGitBranch, lucideGitCompareArrows, lucideImage, lucideLink, lucidePencil, lucidePlus, lucideSearch, lucideShieldAlert,
      lucideTrash2, lucideUnlink, lucideWandSparkles,
    }),
  ],
  templateUrl: './style-detail.html',
})
export class StyleDetail implements OnInit {
  private readonly svc = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly canEdit = this.svc.canEdit;
  readonly toneBadge = toneBadge;

  readonly styleId = signal(0);
  readonly detail = signal<Detail | null>(null);
  readonly lookups = signal<StyleLookups | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly tab = signal<Tab>('bom');
  /** BOM view: one colorway's material colours, or all lines. */
  readonly bomColorway = signal<number | null>(null);
  /** BOM search text: matches part, material, type, supplier and material colour. */
  readonly bomSearch = signal('');
  /** Image shown large; fileName (without extension) is used when it is downloaded. */
  readonly preview = signal<{ url: string; title: string; fileName?: string } | null>(null);
  readonly downloading = signal(false);

  // Dialogs
  readonly editOpen = signal(false);
  readonly copyOpen = signal(false);
  readonly historyOpen = signal(false);
  readonly promptOpen = signal(false);
  readonly colorwayOpen = signal(false);
  readonly editingColorway = signal<Colorway | null>(null);
  readonly lineOpen = signal(false);
  readonly editingLine = signal<BomLine | null>(null);
  readonly newLineClass = signal('');

  readonly style = computed(() => this.detail()?.style ?? null);
  readonly colorways = computed(() => this.detail()?.colorways ?? []);
  readonly colorwayById = computed(() => new Map(this.colorways().map((c) => [c.colorwayId, c])));

  /** BOM lines by content class (sections in the class order, unclassified last), filtered to the chosen colorway and search text. */
  readonly bomSections = computed<BomSection[]>(() => {
    const cw = this.bomColorway();
    const terms = this.bomSearch().trim().toLowerCase().split(/\s+/).filter(Boolean);
    const lines = (this.detail()?.bomLines ?? []).filter(
      (l) => (cw === null || l.colorways.some((c) => c.colorwayId === cw)) && (!terms.length || terms.every((t) => this.bomLineText(l).includes(t))),
    );
    const order = (this.lookups()?.contentClasses ?? []).map((c) => c.code);
    const sections = new Map<string, BomSection>();
    for (const l of lines) {
      const code = l.contentClassCode ?? '';
      if (!sections.has(code)) sections.set(code, { code, name: l.contentClassName ?? '', lines: [] });
      sections.get(code)!.lines.push(l);
    }
    const rank = (code: string) => (code ? (order.indexOf(code) + 1 || 90) : 99);
    return [...sections.values()].sort((a, b) => rank(a.code) - rank(b.code));
  });
  readonly bomLineCount = computed(() => this.detail()?.bomLines.length ?? 0);

  /** Family by season with "reused from" and the chain's first style (origin) resolved to style numbers. */
  readonly family = computed(() => {
    const members = this.detail()?.family ?? [];
    const byId = new Map(members.map((m) => [m.styleId, m]));
    const originOf = (m: FamilyMember) => {
      const seen = new Set<number>();
      let at = m;
      while (at.sourceStyleId && byId.has(at.sourceStyleId) && !seen.has(at.styleId)) {
        seen.add(at.styleId);
        at = byId.get(at.sourceStyleId)!;
      }
      return at;
    };
    return members.map((m) => {
      const source = m.sourceStyleId ? (byId.get(m.sourceStyleId) ?? null) : null;
      const origin = originOf(m);
      // Only worth its own button when it is further back than the style it was reused from.
      return { ...m, source, origin: source && origin.styleId !== source.styleId && origin.styleId !== m.styleId ? origin : null };
    });
  });
  /** History rows ticked for "Compare selected" (at most two; ticking a third drops the earliest tick). */
  readonly compareIds = signal<number[]>([]);
  readonly currentLink = computed(() => this.detail()?.family.find((m) => m.isCurrent) ?? null);

  ngOnInit(): void {
    const tab = this.route.snapshot.queryParamMap.get('tab');
    if (tab === 'colorways' || tab === 'bom' || tab === 'history') this.tab.set(tab);
    this.svc.lookups().subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      this.styleId.set(Number(p.get('id')));
      this.bomColorway.set(null);
      this.bomSearch.set('');
      this.compareIds.set([]);
      this.load(true);
    });
  }

  load(showSkeleton = false): void {
    if (showSkeleton) this.loading.set(true);
    this.svc.get(this.styleId()).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.notFound.set(false);
        this.loading.set(false);
        if (this.bomColorway() !== null && !d.colorways.some((c) => c.colorwayId === this.bomColorway())) this.bomColorway.set(null);
      },
      error: (e) => {
        this.loading.set(false);
        if (e?.status === 404) this.notFound.set(true);
        else toast.error(styleError(e).message);
      },
    });
  }

  /** Translation key for a known gender code, else the code itself (shown as is). */
  genderLabel = (code: string | null) => (GENDERS.some((g) => g.code === code) ? `styles.gender.${code}` : (code ?? '—'));
  /** Lower-cased text a BOM line is searched by. */
  private bomLineText(l: BomLine): string {
    const colours = l.colorways.flatMap((c) => [c.materialColorCode, c.materialColorDescription, this.colorwayById().get(c.colorwayId)?.colorwayCode]);
    return [
      l.partNo, l.materialCode, l.materialDescription, l.materialTypeCode, l.materialTypeName, l.contentClassName, l.nominatedSupplierCode,
      l.nominatedSupplierName, l.supplierCode, l.supplierName, ...colours,
    ].filter((v) => v !== null && v !== undefined).join(' ').toLowerCase();
  }

  colourFor = (line: BomLine, colorwayId: number) => line.colorways.find((c) => c.colorwayId === colorwayId) ?? null;
  lineColorwayCodes = (line: BomLine) => line.colorways.map((c) => this.colorwayById().get(c.colorwayId)?.colorwayCode ?? '?').join(', ');

  /** "1.25 m" style consumption text; '—' when not given. */
  consumption(value: number | null, uom: string | null): string {
    if (value === null || value === undefined) return '—';
    const n = Number(value);
    return `${Number.isInteger(n) ? n : n.toFixed(4).replace(/0+$/, '').replace(/\.$/, '')}${uom ? ' ' + uom : ''}`;
  }

  // ── Style ──────────────────────────────────────────────────

  async downloadPreview(): Promise<void> {
    const p = this.preview();
    if (!p) return;
    this.downloading.set(true);
    try {
      await this.svc.downloadImage(p.url, p.fileName ?? p.title);
    } catch {
      toast.error(this.t('styles.image.downloadFailed'));
    } finally {
      this.downloading.set(false);
    }
  }

  onStyleSaved(): void {
    this.editOpen.set(false);
    this.load();
  }

  onCopied(newId: number): void {
    this.copyOpen.set(false);
    void this.router.navigate(['/styles', newId]);
  }

  async deleteStyle(): Promise<void> {
    const s = this.style();
    if (!s) return;
    const ok = await this.confirmDlg.confirm({
      title: this.t('styles.detail.deleteStyle'),
      message: this.t('styles.detail.deleteStyleMessage', {
        styleNo: s.styleNo,
        season: s.seasonCode,
        colorways: this.colorways().length,
        lines: this.bomLineCount(),
      }),
      confirmLabel: this.t('styles.detail.deleteStyle'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteStyle(s.styleId, s.rowVer).subscribe({
      next: () => {
        toast.success(this.t('styles.detail.styleDeleted', { styleNo: s.styleNo }));
        void this.router.navigate(['/styles']);
      },
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  // ── Colorways ──────────────────────────────────────────────

  openColorway(colorway: Colorway | null): void {
    this.editingColorway.set(colorway);
    this.colorwayOpen.set(true);
  }

  onColorwaySaved(): void {
    this.colorwayOpen.set(false);
    this.load();
  }

  async deleteColorway(c: Colorway): Promise<void> {
    const used = (this.detail()?.bomLines ?? []).filter((l) => l.colorways.some((x) => x.colorwayId === c.colorwayId)).length;
    const ok = await this.confirmDlg.confirm({
      title: this.t('styles.detail.deleteColorway'),
      message:
        this.t('styles.detail.deleteColorwayMessage', { code: c.colorwayCode + (c.colorwayName ? ` (${c.colorwayName})` : '') }) +
        (used ? ' ' + this.t('styles.detail.deleteColorwayUsed', { count: used }) : ''),
      confirmLabel: this.t('actions.delete'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteColorway(this.styleId(), c).subscribe({
      next: () => {
        toast.success(this.t('styles.detail.colorwayDeleted', { code: c.colorwayCode }));
        this.load();
      },
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  // ── BOM ────────────────────────────────────────────────────

  openLine(line: BomLine | null, contentClass = ''): void {
    this.editingLine.set(line);
    this.newLineClass.set(contentClass);
    this.lineOpen.set(true);
  }

  onLineSaved(): void {
    this.lineOpen.set(false);
    this.load();
  }

  async deleteLine(line: BomLine): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.t('styles.detail.deleteLine'),
      message:
        line.partNo !== null
          ? this.t('styles.detail.deleteLineMessagePart', { material: line.materialCode, part: line.partNo })
          : this.t('styles.detail.deleteLineMessage', { material: line.materialCode }),
      confirmLabel: this.t('actions.delete'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteBomLine(this.styleId(), line).subscribe({
      next: () => {
        toast.success(this.t('styles.detail.lineDeleted'));
        this.load();
      },
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  // ── History ────────────────────────────────────────────────

  onHistorySaved(): void {
    this.historyOpen.set(false);
    this.load();
  }

  async removeLink(member: FamilyMember): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.t('styles.detail.removeLink'),
      message:
        this.t('styles.detail.removeLinkMessage', { styleNo: member.styleNo }) +
        (member.linkSource === 'Import' ? ' ' + this.t('styles.detail.removeLinkImport') : ''),
      confirmLabel: this.t('styles.detail.remove'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'warning',
    });
    if (!ok) return;
    this.svc.removeHistory(member.styleId).subscribe({
      next: () => {
        toast.success(this.t('styles.detail.linkRemoved'));
        this.load();
      },
      error: (e) => toast.error(styleError(e).message),
    });
  }

  /** Translation keys (linkManual takes { user }). */
  toggleCompare(styleId: number, on: boolean): void {
    const ids = this.compareIds().filter((id) => id !== styleId);
    this.compareIds.set(on ? [...ids, styleId].slice(-2) : ids);
  }

  /** Opens the compare page for the two ticked styles: the later one (in family order, oldest first) against the earlier. */
  compareSelected(): void {
    const ids = this.compareIds();
    if (ids.length !== 2) return;
    const order = this.family().map((m) => m.styleId);
    const [from, to] = [...ids].sort((a, b) => order.indexOf(a) - order.indexOf(b));
    void this.router.navigate(['/styles', to, 'compare'], { queryParams: { from } });
  }

  linkSourceLabel = (m: FamilyMember) =>
    m.linkSource === 'Manual'
      ? m.linkedBy
        ? 'styles.detail.linkManual'
        : 'styles.detail.linkManualUnknown'
      : m.linkSource === 'Auto'
        ? 'styles.detail.linkAuto'
        : 'styles.detail.linkImport';

  relationLabel = (m: FamilyMember) =>
    m.relation === 'Variant' ? 'styles.detail.variantOf' : m.relation === 'CarryOver' ? 'styles.detail.carryOverFrom' : '';

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(key, params);
  }
}
