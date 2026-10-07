import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, NgZone, OnDestroy, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { environment } from '@env/environment';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronLeft,
  lucideChevronRight,
  lucideEye,
  lucideImage,
  lucideImagePlus,
  lucidePackageSearch,
  lucidePencil,
  lucidePlus,
  lucideSearch,
  lucideTrash2,
  lucideX,
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
import { StoredProcSignalRService } from '@services/spHub.service';
import { UserInfoService } from '@services/user-info.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { compressImage } from '@tms/shared/image-compress';
import { CostBreakdownItem, SBU_PRODUCT_SP as SP, SbuProduct, mapGqProducts, mapSbuProducts } from '@tms/shared/sbu-products';
import { FlatResult, procName, spError, spTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { NumberInputDirective } from '@tms/shared/ui/number-input';
import { problemMessage } from '../../../../core/auth/auth.service';
import { resolveImageUrl } from '../../marketing/concept-studio/concept-studio.model';
import { costItems } from '../../marketing/manage-offering/sbu-submission.model';

/** Countries TMS offered for SBU products, with their ISO codes. */
export const SBU_COUNTRIES: Record<string, string> = { Cambodia: 'KH', China: 'CN', Vietnam: 'VN', Bangladesh: 'BD', Indonesia: 'ID' };

const PAGE_SIZE = 20;
/** Product photos are resized to at most this many pixels per side and sent as JPEG (as in TMS). */
const MAX_IMAGE_DIMENSION = 800;

interface Draft {
  name: string;
  styleCode: string;
  category: string;
  sbu: string;
  country: string;
  leadTimeDays: number | null;
  /** Stored image value ('' = none). */
  image: string;
}

const emptyDraft = (): Draft => ({ name: '', styleCode: '', category: '', sbu: '', country: '', leadTimeDays: null, image: '' });

/**
 * SBU overview (TMS business-unit "Products Offered"): the products each SBU or factory offers, plus approved
 * garment quotations (read-only). Factory users only receive their own factory's products (enforced by the API).
 * Photos go to /api/v1/sbu-products/images (signed-in users only; TMS allowed anonymous uploads and deletes).
 */
@Component({
  selector: 'app-sbu-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, NgTemplateOutlet, TranslocoPipe, NgIcon, Modal, NumberInputDirective, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports,
    HlmLabelImports, HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports,
  ],
  providers: [
    provideIcons({
      lucideChevronLeft, lucideChevronRight, lucideEye, lucideImage, lucideImagePlus, lucidePackageSearch, lucidePencil, lucidePlus,
      lucideSearch, lucideTrash2, lucideX,
    }),
  ],
  templateUrl: './sbu-overview.html',
})
export class SbuOverview implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly zone = inject(NgZone);
  private readonly http = inject(HttpClient);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly user = inject(UserInfoService).user;

  protected readonly countries = Object.keys(SBU_COUNTRIES);
  protected readonly costItems = costItems;
  protected readonly resolveImageUrl = resolveImageUrl;
  readonly isFactoryUser = ['FTY', 'FACTORY'].includes((this.user?.userGroup ?? '').toUpperCase());

  // ── Data ───────────────────────────────────────────────────
  readonly products = signal<SbuProduct[]>([]);
  readonly pending = signal(0);
  readonly categories = signal<string[]>([]);

  // ── Filters ────────────────────────────────────────────────
  readonly search = signal('');
  readonly category = signal('');
  readonly country = signal('');
  readonly source = signal<'' | 'manual' | 'gq'>('');
  readonly page = signal(1);

  readonly countryOptions = computed(() => [...new Set([...this.countries, ...this.products().map((p) => p.country).filter((c) => !!c)])].sort());
  readonly categoryOptions = computed(() =>
    [...new Set([...this.categories(), ...this.products().map((p) => p.category).filter((c) => !!c)])].sort(),
  );

  readonly filtered = computed(() => {
    const q = this.search().toLowerCase().trim();
    const cat = this.category();
    const ctr = this.country();
    const src = this.source();
    return this.products().filter(
      (p) =>
        (!q || [p.name, p.styleCode, p.sbu].some((v) => v.toLowerCase().includes(q))) &&
        (!cat || p.category === cat) &&
        (!ctr || p.country === ctr) &&
        (!src || p.source === src),
    );
  });
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / PAGE_SIZE)));
  readonly paged = computed(() => {
    const start = (Math.min(this.page(), this.pageCount()) - 1) * PAGE_SIZE;
    return this.filtered().slice(start, start + PAGE_SIZE);
  });
  readonly hasFilters = computed(() => !!(this.search() || this.category() || this.country() || this.source()));

  // ── Selection (catalog products only) ──────────────────────
  readonly selected = signal<ReadonlySet<string>>(new Set());
  readonly selectable = computed(() => this.paged().filter((p) => p.source === 'manual'));
  readonly allOnPageSelected = computed(() => this.selectable().length > 0 && this.selectable().every((p) => this.selected().has(p.id)));

  // ── Dialog ─────────────────────────────────────────────────
  readonly dialogOpen = signal(false);
  /** The product being edited or viewed; null = new product. */
  readonly editing = signal<SbuProduct | null>(null);
  readonly viewOnly = computed(() => this.editing()?.source === 'gq');
  readonly draft = signal<Draft>(emptyDraft());
  readonly lines = signal<CostBreakdownItem[]>([]);
  readonly total = computed(() => +this.lines().reduce((sum, l) => sum + (Number(l.amount) || 0), 0).toFixed(2));
  readonly uploading = signal(false);
  readonly saving = signal(false);
  readonly deleting = signal(false);
  /** A cost line with an amount but no name. */
  readonly unnamedLine = computed(() => this.lines().some((l) => l.item.trim() === '' && !!l.amount));
  readonly canSave = computed(() => {
    const d = this.draft();
    const lead = d.leadTimeDays;
    return (
      !!d.name.trim() &&
      !!d.styleCode.trim() &&
      (lead === null || (Number.isInteger(lead) && lead >= 0 && lead <= 999)) &&
      !this.unnamedLine() &&
      this.lines().every((l) => l.amount >= 0) &&
      !this.uploading() &&
      !this.saving()
    );
  });

  constructor() {
    // Back to page 1 when the filters change.
    effect(() => {
      this.search();
      this.category();
      this.country();
      this.source();
      untracked(() => this.page.set(1));
    });
  }

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));
    this.call(SP.CATEGORIES);
    this.load();
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  private call(proc: string, parameters?: Record<string, unknown>): void {
    void this.sp.invoke('ExecuteStoredProcFlat', parameters ? { SpName: proc, Parameters: parameters } : { SpName: proc });
  }

  /** Factory users get only their own factory's rows: the API sets the filter from the sign-in token. */
  load(): void {
    this.pending.set(2);
    this.call(SP.PRODUCTS);
    this.call(SP.GQ_APPROVED);
  }

  private onResult(res: FlatResult): void {
    const proc = procName(res.procedure);
    const error = spError(res);
    const data = spTables(res);
    const row = data['table0']?.[0];
    const status = row?.['result_status'];
    const ok = !error && status === 'Success';
    const reason = error || (status ? String(status) : undefined);

    switch (proc) {
      case SP.PRODUCTS:
      case SP.GQ_APPROVED: {
        const source = proc === SP.PRODUCTS ? 'manual' : 'gq';
        if (error) toast.error(this.transloco.translate('sbu.loadFailed'), { description: error });
        const rows = error ? [] : source === 'manual' ? mapSbuProducts(data) : mapGqProducts(data);
        this.products.update((current) => [...current.filter((p) => p.source !== source), ...rows]);
        if (source === 'manual') {
          const ids = new Set(rows.map((p) => p.id));
          this.selected.update((s) => new Set([...s].filter((id) => ids.has(id))));
        }
        this.pending.update((n) => Math.max(0, n - 1));
        break;
      }
      case SP.CATEGORIES:
        this.categories.set((data['table0'] ?? []).map((r) => String(r['Description'] ?? '').trim()).filter((c) => !!c));
        break;
      case SP.INSERT:
      case SP.UPDATE:
        this.saving.set(false);
        if (ok) {
          toast.success(this.transloco.translate(proc === SP.INSERT ? 'sbu.added' : 'sbu.updated'));
          this.dialogOpen.set(false);
          this.load();
        } else {
          toast.error(this.transloco.translate('sbu.saveFailed'), { description: reason });
        }
        break;
      case SP.DELETE_BULK:
        this.deleting.set(false);
        if (ok) {
          toast.success(this.transloco.translate('sbu.deleted', { count: Number(row?.['rows_affected']) || 0 }));
          this.selected.set(new Set());
          this.load();
        } else {
          toast.error(this.transloco.translate('sbu.deleteFailed'), { description: reason });
        }
        break;
    }
  }

  // ── Filters / paging ───────────────────────────────────────

  clearFilters(): void {
    this.search.set('');
    this.category.set('');
    this.country.set('');
    this.source.set('');
  }

  goTo(page: number): void {
    this.page.set(Math.max(1, Math.min(page, this.pageCount())));
  }

  // ── Selection ──────────────────────────────────────────────

  toggle(id: string, checked: boolean): void {
    this.selected.update((s) => {
      const next = new Set(s);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  togglePage(checked: boolean): void {
    const ids = this.selectable().map((p) => p.id);
    this.selected.update((s) => {
      const next = new Set(s);
      for (const id of ids) {
        if (checked) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  }

  async deleteSelected(): Promise<void> {
    const recids = this.products()
      .filter((p) => p.source === 'manual' && this.selected().has(p.id) && p.recid)
      .map((p) => p.recid!);
    if (!recids.length) return;
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('sbu.confirmDeleteTitle'),
      message: this.transloco.translate('sbu.confirmDeleteMessage', { count: recids.length }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!confirmed) return;
    this.deleting.set(true);
    this.call(SP.DELETE_BULK, { recids: recids.join(',') });
  }

  // ── Add / edit / view ──────────────────────────────────────

  openAdd(): void {
    // Factory users: prefill their SBU and country from their existing products (as TMS did).
    const own = this.isFactoryUser ? this.products().find((p) => p.source === 'manual') : undefined;
    this.editing.set(null);
    this.draft.set({ ...emptyDraft(), sbu: own?.sbu ?? '', country: own?.country ?? '', category: this.categoryOptions()[0] ?? '' });
    this.lines.set([{ item: '', amount: 0 }]);
    this.dialogOpen.set(true);
  }

  open(p: SbuProduct): void {
    this.editing.set(p);
    this.draft.set({
      name: p.name,
      styleCode: p.styleCode,
      category: p.category,
      sbu: p.sbu,
      country: p.country,
      leadTimeDays: p.leadTimeDays,
      image: p.imageStored,
    });
    const lines = costItems(p.costBreakdown);
    this.lines.set(lines.length || p.source === 'gq' ? lines : [{ item: '', amount: 0 }]);
    this.dialogOpen.set(true);
  }

  setDraft(patch: Partial<Draft>): void {
    this.draft.update((d) => ({ ...d, ...patch }));
  }

  addLine(): void {
    this.lines.update((l) => [...l, { item: '', amount: 0 }]);
  }

  setLine(index: number, patch: Partial<CostBreakdownItem>): void {
    this.lines.update((l) => l.map((line, i) => (i === index ? { ...line, ...patch } : line)));
  }

  removeLine(index: number): void {
    this.lines.update((l) => l.filter((_, i) => i !== index));
  }

  async onImagePicked(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.uploading.set(true);
    try {
      const blob = await compressImage(file, MAX_IMAGE_DIMENSION);
      const form = new FormData();
      form.append('file', blob, 'product.jpg');
      const res = await new Promise<{ imageUrl: string }>((resolve, reject) =>
        this.http.post<{ imageUrl: string }>(`${environment.apiBaseUrl}/api/v1/sbu-products/images`, form).subscribe({ next: resolve, error: reject }),
      );
      this.setDraft({ image: res.imageUrl });
    } catch (e) {
      toast.error(this.transloco.translate('sbu.uploadFailed'), { description: problemMessage(e, file.name) });
    } finally {
      this.uploading.set(false);
    }
  }

  /** Removes the photo from this product only; the stored file is kept (as for concept images). */
  removeImage(): void {
    this.setDraft({ image: '' });
  }

  save(): void {
    if (!this.canSave()) return;
    const d = this.draft();
    const lines = this.lines()
      .filter((l) => l.item.trim() !== '' || l.amount)
      .map((l) => ({ item: l.item.trim(), amount: +(Number(l.amount) || 0).toFixed(2) }));
    const fields = {
      product_name: d.name.trim(),
      style_code: d.styleCode.trim(),
      sbu: d.sbu.trim() || null,
      country: d.country || null,
      country_code: SBU_COUNTRIES[d.country] ?? null,
      fob_price: this.total(),
      cost_breakdown: JSON.stringify(lines),
      lead_time_days: d.leadTimeDays,
      category: d.category || null,
    };
    const editing = this.editing();
    this.saving.set(true);
    // created_by / modified_by, location and user group are filled by the API from the sign-in token.
    if (editing?.recid) {
      // image_url: NULL keeps the stored photo, '' removes it.
      this.call(SP.UPDATE, { recid: editing.recid, ...fields, image_url: d.image === editing.imageStored ? null : d.image });
    } else {
      this.call(SP.INSERT, { ...fields, image_url: d.image || null });
    }
  }
}
