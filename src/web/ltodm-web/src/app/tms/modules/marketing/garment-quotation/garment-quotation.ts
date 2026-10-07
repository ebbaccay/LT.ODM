import {
  Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit, OnDestroy,
  signal, computed, inject, effect, NgZone, ElementRef, viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft, lucideBan, lucideCheck, lucideChevronDown, lucideChevronLeft, lucideChevronRight, lucideCircleAlert,
  lucideDollarSign, lucideFileSpreadsheet, lucideFileText, lucideInfo, lucideLayoutGrid, lucideGrid2x2, lucideList,
  lucideLock, lucidePencil, lucidePin, lucidePlus, lucideRefreshCw, lucideRotateCcw, lucideSave, lucideSearch,
  lucideSend, lucideTable2, lucideTrash2, lucideUndo2, lucideX,
} from '@ng-icons/lucide';
import { toast as sonnerToast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { Tone, toneBadge, toneBorder, toneDot, toneText } from '@tms/shared/ui/tones';
import { ActivatedRoute } from '@angular/router';

import { environment } from '@env/environment';
import { StoredProcSignalRService } from '@services/spHub.service';
import { UserInfoService } from '@services/user-info.service';
import { NotificationHubService } from '@services/notification-hub.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';

import {
  GQ_SP, STATUS_CFG, CURRENCIES, CMT_TEMPLATE, STATUS_FILTER_KEYS,
  MAT_CLASS_ORDER, MAT_CLASS_LABELS,
  GqPackage, GqStyle, GqBomItem, GqCmtItem, GqQuotation, GqFactory, GqRemark,
  GqToast, GqTotals, GqUom, QuotationStatus, UserRole, TabId, PriceType,
  GqCostingHistory, CompareRow,
  makeQuotation, makeCmt, calcTotals, deriveStatus, toRmb, effRate, fmtNum, nowTs,
} from './garment-quotation.model';

interface BatchRowState {
  comments: string;
  tmsPrice: string;
  factoryPrice: string;
  revisedPrice: string;
}

interface BatchDisplayRow {
  key: string;
  style: GqStyle;
  q: GqQuotation | null;
  factory: GqFactory | null;
  isDraft: boolean;
}

@Component({
  selector: 'app-garment-quotation',
  imports: [CommonModule, FormsModule, TranslocoPipe, NgIcon, HlmButtonImports, HlmCheckboxImports, HlmInputImports, HlmSpinnerImports, HlmTableImports],
  providers: [
    provideIcons({
      lucideArrowLeft, lucideBan, lucideCheck, lucideChevronDown, lucideChevronLeft, lucideChevronRight, lucideCircleAlert,
      lucideDollarSign, lucideFileSpreadsheet, lucideFileText, lucideInfo, lucideLayoutGrid, lucideGrid2x2, lucideList,
      lucideLock, lucidePencil, lucidePin, lucidePlus, lucideRefreshCw, lucideRotateCcw, lucideSave, lucideSearch,
      lucideSend, lucideTable2, lucideTrash2, lucideUndo2, lucideX,
    }),
  ],
  templateUrl: './garment-quotation.html',
  // Layout is Tailwind; only print rules (@page, repeated table headers) remain in SCSS.
  styleUrls: ['./garment-quotation.print.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GarmentQuotation implements OnInit, OnDestroy {

  private readonly _sp = inject(StoredProcSignalRService);
  private readonly _userInfo = inject(UserInfoService);
  private readonly _notifHub = inject(NotificationHubService);
  private readonly _confirmDlg = inject(ConfirmDialogService);
  private readonly _transloco = inject(TranslocoService);
  private readonly _zone = inject(NgZone);
  private readonly _cdr  = inject(ChangeDetectorRef);
  private readonly _route = inject(ActivatedRoute);

  private _pendingNav: { styleId: string; factoryId: string; tab: TabId } | null = null;
  private _routeSub?: import('rxjs').Subscription;
  private _rateRefreshPendingStyleId: string | null = null;
  private _pendingSaveQuotationQueue: Array<{
    modern: Record<string, unknown>;
    legacy: Record<string, unknown>;
    retriedLegacy: boolean;
  }> = [];
  private _pendingSaveCostingQueue: Array<{
    modern: Record<string, unknown>;
    legacy: Record<string, unknown>;
    retriedLegacy: boolean;
  }> = [];
  private readonly _spDebugEnabled = !!(environment as Record<string, unknown>)['spDebug'];
  private _spDebugSeq = 0;

  // ── SP full names ──────────────────────────────────────────────────────────
  private readonly _db = `${environment.appDb}.dbo`;
  private readonly _spPkg = `${this._db}.${GQ_SP.PACKAGES}`;
  private readonly _spStyle = `${this._db}.${GQ_SP.STYLES}`;
  private readonly _spBom = `${this._db}.${GQ_SP.BOM_QUOTE}`;
  private readonly _spCurrencyConversion = `${this._db}.${GQ_SP.CURRENCY_CONVERSION}`;
  private readonly _spSaveRemark = `${this._db}.${GQ_SP.SAVE_REMARK}`;
  private readonly _spDeleteRemark = `${this._db}.${GQ_SP.DELETE_REMARK}`;
  private readonly _spLoadRemarks = `${this._db}.${GQ_SP.LOAD_REMARKS}`;
  private readonly _spSaveCosting = `${this._db}.${GQ_SP.SAVE_COSTING}`;
  private readonly _spCostingHistory = `${this._db}.${GQ_SP.COSTING_HISTORY}`;
  private readonly _spSaveQuotation = `${this._db}.${GQ_SP.SAVE_QUOTATION}`;
  private readonly _spLoadQuotations = `${this._db}.${GQ_SP.LOAD_QUOTATIONS}`;
  private readonly _spFactoryList = `${this._db}.${GQ_SP.FACTORY_LIST}`;
  private readonly _spApproveCosting = `${this._db}.${GQ_SP.APPROVE_COSTING}`;
  private readonly _spDeleteQuotation = `${this._db}.${GQ_SP.DELETE_QUOTATION}`;
  private readonly _spSaveStylePricing  = `${this._db}.${GQ_SP.SAVE_STYLE_PRICING}`;
  private readonly _spLoadStylePricing  = `${this._db}.${GQ_SP.LOAD_STYLE_PRICING}`;
  private readonly _spUpdStyleDisplay   = `${this._db}.${GQ_SP.UPD_STYLE_DISPLAY}`;
  private readonly _spSaveBomDraft      = `${this._db}.${GQ_SP.SAVE_BOM_DRAFT}`;
  private readonly _spLoadBomDraft      = `${this._db}.${GQ_SP.LOAD_BOM_DRAFT}`;
  private readonly _spUomList           = `${this._db}.${GQ_SP.UOM_LIST}`;

  // ── BOM draft cache (persisted per style across drawer open/close) ─────────
  private _bomDraftCache = new Map<string, GqBomItem[]>();
  readonly isSavingBom = signal(false);
  readonly isSavingCosting = signal(false);

  // ── BOM auto-load queue (pre-loads BOM for all styles on package change) ───
  // Sequential (one at a time) to avoid ambiguous response matching.
  // Uses the same FIFO-ordering guarantee as the FTY scan queue.
  private _bomAutoQueue: Array<Pick<GqStyle, 'style_id' | 'customer_id' | 'season_id'>> = [];
  private _bomAutoPending = '';

  // ── FTY package scan state ────────────────────────────────────────────────
  private _ftyScanQueue: string[] = [];
  private _ftyScanPending = '';
  readonly ftyPackageSet = signal<Set<string>>(new Set());
  readonly isFtyScanComplete = signal(false);

  // ── Constants exposed to template ─────────────────────────────────────────
  readonly factories = signal<GqFactory[]>([]);
  readonly uomList   = signal<GqUom[]>([]);
  readonly uomOpenIdx = signal(-1);
  readonly uomSearch  = signal('');
  readonly uomDropdownPos = signal<{ top: number; left: number; openUp: boolean }>({ top: 0, left: 0, openUp: false });

  readonly filteredUoms = computed(() => {
    const q = this.uomSearch().toLowerCase();
    if (!q) return this.uomList();
    return this.uomList().filter(u =>
      u.uom_id.toLowerCase().includes(q) ||
      u.uom_desc.toLowerCase().includes(q)
    );
  });

  readonly uomCurrentValue = computed(() => {
    const idx = this.uomOpenIdx();
    if (idx < 0) return null;
    return this.bomDisplayBom()[idx]?.consump_uom ?? null;
  });
  readonly CURRENCIES = CURRENCIES;
  readonly STATUS_CFG = STATUS_CFG;
  readonly CMT_TEMPLATE = CMT_TEMPLATE;
  readonly STATUS_FILTER_KEYS = STATUS_FILTER_KEYS;
  readonly MAT_CLASS_ORDER = MAT_CLASS_ORDER;
  readonly PRICE_TYPES: PriceType[] = ['FOB', 'CIF', 'EXW'];
  readonly COL_WIDTHS = [220, 280, 360];

  // ── UI state ───────────────────────────────────────────────────────────────
  readonly role = signal<UserRole>('merchandiser');
  readonly factoryUserId = signal<string>('');
  readonly activeTab = signal<TabId>('bom');
  readonly activeQIdx = signal<number>(0);
  readonly drawerOpen = signal(false);
  readonly factModal = signal(false);
  readonly rejectModal = signal(false);
  readonly removeFactoryModal = signal(false);
  readonly addBomModal     = signal(false);
  readonly addBomMatClass  = signal<string>('FABRICS');
  readonly addBomMaterial  = signal('');
  readonly addBomDesc      = signal('');
  readonly addBomConsump   = signal('');
  readonly addBomUom       = signal('');
  readonly addBomPrice     = signal('');
  readonly addBomWastage   = signal('0');
  readonly addBomCurrency  = signal('RMB');

  readonly addBomActualCost = computed((): number => {
    const c = parseFloat(this.addBomConsump()) || 0;
    const p = parseFloat(this.addBomPrice()) || 0;
    const w = parseFloat(this.addBomWastage()) || 0;
    const base = c * p;
    return base + (base * w / 100);
  });
  readonly pendingQid = signal<string | null>(null);
  readonly pendingRemoveQid = signal<string | null>(null);
  readonly colIdx = signal<number>(2);
  readonly globalCurrency = signal<string>('RMB');
  readonly globalWastagePct = signal<string>('0');
  readonly isRefreshingRate = signal(false);

  // ── Filter state ───────────────────────────────────────────────────────────
  readonly selectedPkg = signal<string>('');
  readonly statusFilter = signal<string>('all');
  readonly searchQuery = signal<string>('');

  // ── Package dropdown ───────────────────────────────────────────────────────
  readonly pkgSearch = signal('');
  readonly pkgDropdownOpen = signal(false);

  // ── CMT global currency ────────────────────────────────────────────────────
  readonly cmtGlobalCurrency = signal<string>('RMB');

  // ── Factory modal state ────────────────────────────────────────────────────
  readonly chosenFactories = signal<string[]>([]);
  readonly factoryScope = signal<'selected' | 'all'>('selected');
  readonly factorySearch = signal('');
  readonly factoryPage = signal(0);
  readonly batchRows = signal<Record<string, BatchRowState>>({});
  readonly marginEntryMode = signal<Record<string, 'pct' | 'value'>>({});
  readonly FACTORY_PAGE_SIZE = 10;

  readonly favoriteFactories = signal<Set<string>>(this._loadFavorites());

  readonly filteredFactories = computed(() => {
    const q = this.factorySearch().toLowerCase().trim();
    const favs = this.favoriteFactories();
    const all = !q
      ? this.factories()
      : this.factories().filter(f =>
        f.name.toLowerCase().includes(q) || f.id.toLowerCase().includes(q)
      );
    return [
      ...all.filter(f => favs.has(f.id)),
      ...all.filter(f => !favs.has(f.id)),
    ];
  });

  readonly factoryPageCount = computed(() =>
    Math.max(1, Math.ceil(this.filteredFactories().length / this.FACTORY_PAGE_SIZE))
  );

  readonly pagedFactories = computed(() => {
    const start = this.factoryPage() * this.FACTORY_PAGE_SIZE;
    return this.filteredFactories().slice(start, start + this.FACTORY_PAGE_SIZE);
  });

  // ── Remarks / Reject form state ────────────────────────────────────────────
  readonly remarkText = signal('');
  readonly rejectReason = signal('');

  // ── Data ───────────────────────────────────────────────────────────────────
  readonly packages = signal<GqPackage[]>([]);
  readonly styles = signal<GqStyle[]>([]);
  readonly selectedStyle = signal<GqStyle | null>(null);
  readonly isLoadingPkg = signal(false);
  readonly isLoadingStyles = signal(false);
  readonly isLoadingBom = signal(false);

  // ── History ────────────────────────────────────────────────────────────────
  readonly costingHistory  = signal<GqCostingHistory[]>([]);
  readonly isLoadingHistory = signal(false);
  readonly histBomOpen     = signal<Set<number>>(new Set());
  private _historyCache = new Map<string, GqCostingHistory[]>();

  // ── Computed ───────────────────────────────────────────────────────────────

  readonly filteredPackages = computed(() => {
    const q = this.pkgSearch().toLowerCase().trim();
    const ftyId = this.factoryUserId();
    const ftySet = this.ftyPackageSet();
    return this.packages().filter(p => {
      if (ftyId && !ftySet.has(p.pack_name)) return false;
      return !q || p.pack_name.toLowerCase().includes(q);
    });
  });

  // For factory (FTY) users, narrow each style to only their own quotation and
  // drop styles that have no quotation for them at all.
  readonly baseStyles = computed(() => {
    const ftyId = this.factoryUserId();
    if (!ftyId) return this.styles();
    return this.styles()
      .map(s => ({ ...s, quotations: s.quotations.filter(q => q.factory.id === ftyId) }))
      .filter(s => s.quotations.length > 0);
  });

  readonly filteredStyles = computed(() => {
    const q = this.searchQuery().toLowerCase();
    const f = this.statusFilter();
    return this.baseStyles().filter(s => {
      if (f !== 'all' && deriveStatus(s) !== f) return false;
      if (!q) return true;
      return s.style_id.toLowerCase().includes(q)
        || s.model_name.toLowerCase().includes(q)
        || s.season_id.toLowerCase().includes(q)
        || s.composition.toLowerCase().includes(q)
        || s.customer_id.toLowerCase().includes(q);
    });
  });

  readonly selectedIds = computed(() =>
    this.styles().filter(s => s.selected).map(s => s.style_id)
  );

  readonly statusCounts = computed(() =>
    this.baseStyles().reduce((acc, s) => {
      const st = deriveStatus(s);
      acc[st] = (acc[st] || 0) + 1;
      return acc;
    }, {} as Record<string, number>)
  );

  readonly selQ = computed(() => {
    const s = this.selectedStyle();
    if (!s?.quotations?.length) return null;
    return s.quotations[this.activeQIdx()] ?? s.quotations[0];
  });

  readonly drawerTotals = computed((): GqTotals | null => {
    const s = this.selectedStyle();
    if (!s) return null;
    // On the Original tab, show pure BOM baseline (no factory CMT).
    // On a factory tab, include that factory's costing so the strip reflects
    // the full quote (BOM + CMT) for the selected factory.
    // LT ODM fix: only the BOM tab has an Original view. TMS kept it on in the other tabs, so a factory's
    // costing showed CMT 0.00 and a total built from the original BOM instead of the factory's priced BOM.
    // Outside the BOM tab the strip now matches the saved quote totals: factory BOM (q.bom ?? style.bom) + CMT.
    const onBomTab = this.activeTab() === 'bom';
    const showingOriginal = this.role() !== 'factory' && this.bomShowOriginal() && onBomTab;
    const q = showingOriginal ? null : this.selQ();
    const bom = onBomTab ? this.bomDisplayBom() : this.activeBom();
    return calcTotals(bom, q?.factoryCosting ?? [], effRate(s, q ?? this.selQ(), this.role()));
  });

  // Per-quotation priced BOM, falling back to the shared style BOM when the
  // factory has not yet provided their own pricing.
  readonly activeBom = computed((): GqBomItem[] => {
    const s = this.selectedStyle();
    if (!s) return [];
    return this.selQ()?.bom ?? s.bom;
  });

  // BOM tab view: "Original" (style.bom) or a specific factory's q.bom.
  // Defaults to true so the tab opens on the original merchandiser BOM.
  readonly bomShowOriginal = signal(true);

  readonly bomDisplayBom = computed((): GqBomItem[] => {
    const s = this.selectedStyle();
    if (!s) return [];
    // Factory users always view their own BOM copy (so their edits are visible immediately)
    if (this.role() === 'factory') return this.activeBom();
    if (this.bomShowOriginal()) return s.bom;
    return this.activeBom();
  });

  // Active rate for the drawer view (TMS rate for merchandiser; FTY rate when set, else style rate, for factory).
  readonly currentRate = computed((): number => {
    const s = this.selectedStyle();
    if (!s) return 6.55;
    return effRate(s, this.selQ(), this.role());
  });

  readonly canEditBom = computed(() => {
    if (this.role() !== 'merchandiser') return false;
    const qs = this.selectedStyle()?.quotations ?? [];
    // BOM locked once any quotation is actively with a factory or approved
    return !qs.some(q =>
      q.status === 'sent_to_factory' ||
      q.status === 'factory_submitted' ||
      q.status === 'for_revision' ||
      q.status === 'approved'
    );
  });

  readonly canEditBomFty = computed(() => {
    const q = this.selQ();
    return this.role() === 'factory'
      && (q?.status === 'sent_to_factory' || q?.status === 'for_revision');
  });

  readonly canEditCosting = computed(() => {
    const q = this.selQ();
    return this.role() === 'factory'
      && (q?.status === 'sent_to_factory' || q?.status === 'rejected' || q?.status === 'for_revision');
  });

  readonly drawerFobFactor = computed((): number => {
    const pct = parseFloat(this.selQ()?.gross_margin_pct ?? '0') || 0;
    return 1 + pct / 100;
  });

  readonly drawerMarginPct = computed((): number => {
    return parseFloat(this.selQ()?.gross_margin_pct ?? '0') || 0;
  });

  readonly tmsFobMargin = computed((): { value: number; str: string } | null => {
    const s = this.selectedStyle();
    const q = this.selQ();
    if (!s || !q || !s.tms_fob || !this.hasFactoryData(q)) return null;
    const v = this.fobMarginPct(s, q);
    if (v === null) return null;
    return { value: v, str: (v >= 0 ? '+' : '') + fmtNum(v, 1) + '%' };
  });

  readonly rejectFactoryName = computed(() => {
    const qid = this.pendingQid();
    return qid ? this._findStyleAndQuotation(qid)?.q.factory.name ?? '' : '';
  });

  readonly removeFactoryName = computed(() => {
    const qid = this.pendingRemoveQid();
    return this.selectedStyle()?.quotations?.find(q => q.qid === qid)?.factory.name ?? '';
  });

  readonly gridCols = computed(() =>
    // min(…, 100%): a column never exceeds the screen width on phones.
    `repeat(auto-fill, minmax(min(${this.COL_WIDTHS[this.colIdx()]}px, 100%), 1fr))`
  );

  readonly compareRows = computed((): CompareRow[] => {
    const s = this.selectedStyle();
    if (!s || s.quotations.length < 2) return [];
    const qs = s.quotations;
    const rate = s.rate_in_usd;
    const hasData = (q: GqQuotation) =>
      q.status === 'factory_submitted' || q.status === 'for_revision' || q.status === 'approved' || q.status === 'rejected';
    const qBom = (q: GqQuotation) => q.bom ?? s.bom;

    // Precompute per-quotation to avoid redundant calcTotals calls per row
    const bomOnly = qs.map(q => hasData(q) ? calcTotals(qBom(q), [], rate) : null);
    const full    = qs.map(q => hasData(q) ? calcTotals(qBom(q), q.factoryCosting, rate) : null);

    const allCmtKeys = [...new Set(qs.flatMap(q => q.factoryCosting.map(i => i.key)))];

    const rows: CompareRow[] = [
      { key: 'sep_bom', label: 'Bill of Materials', labelKey: 'gq.compare.sep.bom', isSep: true, values: [] },
      {
        key: 'fabric', label: 'Fabric', labelKey: 'gq.fabric',
        values: qs.map((_, i) => bomOnly[i]?.fabricTotal ?? null)
      },
      {
        key: 'trim', label: 'Trim', labelKey: 'gq.trim',
        values: qs.map((_, i) => bomOnly[i]?.trimTotal ?? null)
      },
      {
        key: 'others', label: 'Others', labelKey: 'gq.others',
        values: qs.map((_, i) => bomOnly[i]?.othersTotal ?? null)
      },
      { key: 'sep_cmt', label: 'Factory Costing (CMT)', labelKey: 'gq.compare.sep.cmt', isSep: true, values: [] },
      ...allCmtKeys.map(key => {
        const cmtItem = qs.flatMap(q => q.factoryCosting).find(i => i.key === key);
        const label = cmtItem?.label ?? key;
        const isCustom = cmtItem?.isCustom ?? false;
        const labelKey = cmtItem && !cmtItem.isCustom ? `gq.costing.cmt.${key}` : undefined;
        return {
          key, label, labelKey, isCustom,
          values: qs.map((q, i) => {
            if (!full[i]) return null;
            const item = q.factoryCosting.find(ci => ci.key === key);
            return item ? toRmb(item.value, item.curr_id, rate) : 0;
          }),
        };
      }),
      { key: 'sep_totals', label: 'Totals', labelKey: 'gq.compare.sep.totals', isSep: true, values: [] },
      {
        key: 'cmt_total', label: 'Total CMT', labelKey: 'gq.costing.totalCmt',
        values: qs.map((_, i) => full[i]?.cmtTotal ?? null)
      },
      {
        key: 'grand_total', label: 'Grand Total', labelKey: 'gq.compare.grandTotal', isTotal: true,
        values: qs.map((_, i) => full[i]?.total ?? null)
      },
      {
        key: 'fob', label: 'FOB Price', labelKey: 'gq.costing.fobPrice',
        values: qs.map((q, i) => {
          if (!full[i]) return null;
          return (full[i]!.total * (1 + (parseFloat(q.gross_margin_pct) || 0) / 100)) + this.adjustmentRmb(s, q);
        })
      },
    ];

    if (this.role() === 'merchandiser') {
      const tmsFobRmb = toRmb(parseFloat(s.tms_fob ?? '0') || 0, 'USD', rate);
      rows.push({
        key: 'tms_fob', label: 'TMS FOB', labelKey: 'gq.tmsFob',
        values: qs.map((_, i) => full[i] ? tmsFobRmb : null),
      });
      rows.push({
        key: 'margin', label: 'Margin vs TMS FOB', labelKey: 'gq.compare.marginVsTms',
        isHigherBetter: true, isPercent: true,
        values: qs.map((q, i) => {
          if (!full[i] || !tmsFobRmb) return null;
          const factoryFob = (full[i]!.total * (1 + (parseFloat(q.gross_margin_pct) || 0) / 100)) + this.adjustmentRmb(s, q);
          return ((tmsFobRmb - factoryFob) / tmsFobRmb) * 100;
        }),
      });
    }

    return rows;
  });

  // ── DOM refs ───────────────────────────────────────────────────────────────
  private readonly _remarksThread = viewChild<ElementRef<HTMLDivElement>>('remarksThread');

  // ── Lifecycle ──────────────────────────────────────────────────────────────
  constructor() {
    effect(() => {
      const pkg = this.selectedPkg();
      if (pkg) this._fetchStyles(pkg);
    });

    // Scroll remarks thread to bottom whenever the tab opens or a message arrives
    effect(() => {
      const isRemarks = this.activeTab() === 'remarks';
      const count = this.selQ()?.remarks.length ?? 0;
      void count;
      if (!isRemarks) return;
      setTimeout(() => {
        const el = this._remarksThread()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      }, 0);
    });

    // Load costing history when History tab is opened or active factory changes
    effect(() => {
      const tab = this.activeTab();
      const q = this.selQ();
      if (tab === 'history' && q) this._loadHistory(q.qid);
    });
  }

  async ngOnInit(): Promise<void> {
    const u = this._userInfo.user;
    if (u?.userGroup === 'FTY') {
      this.role.set('factory');
      this.factoryUserId.set(u.location ?? '');
    } else {
      this.role.set('merchandiser');
    }

    // Deep-link: open style drawer + Remarks when navigated from a notification
    this._routeSub = this._route.queryParams.subscribe(params => {
      const styleId = params['styleId'] as string | undefined;
      const factoryId = params['factoryId'] as string | undefined;
      const pkg = params['pkg'] as string | undefined;
      if (!styleId || !pkg) return;

      this._pendingNav = { styleId, factoryId: factoryId ?? '', tab: (params['tab'] as TabId) ?? 'remarks' };

      if (pkg !== this.selectedPkg()) {
        // Changing package triggers the effect → fetchStyles → loadQuotations →
        // _applyPendingNav is called at the end of _spLoadQuotations.
        this.selectedPkg.set(pkg);
      } else if (this.styles().length > 0) {
        // Already on the right package with styles loaded — apply immediately.
        this._applyPendingNav();
      }
      // If same package but styles not loaded yet, _applyPendingNav fires from
      // _spLoadQuotations when the in-flight load completes.
    });

    await this._ensureSpHubReady();
    this._registerListeners();
    this._fetchFactories();
    this._fetchPackages();
    this._fetchUomList();
  }

  ngOnDestroy(): void {
    this._sp.hubConn?.off('StoredProcResultFlat');
    this._routeSub?.unsubscribe();
  }

  private async _ensureSpHubReady(): Promise<void> {
    await this._sp.ensureConnected();
  }

  private _applyPendingNav(): void {
    const nav = this._pendingNav;
    if (!nav) return;
    // Use baseStyles so FTY users only see their own factory's quotation in the
    // drawer — the same filtering that applies when clicking a card normally.
    const style = this.baseStyles().find(s => s.style_id === nav.styleId);
    if (!style) return;
    // Wait until the target factory's quotation is present — scan responses for
    // other packages arrive concurrently and must not trigger the nav prematurely.
    if (nav.factoryId && !style.quotations.some(q => q.factory.id === nav.factoryId)) return;
    this._pendingNav = null;
    this.openDrawer(style);
    if (nav.factoryId) {
      const idx = style.quotations.findIndex(q => q.factory.id === nav.factoryId);
      if (idx >= 0) this.activeQIdx.set(idx);
    }
    this.activeTab.set(nav.tab);
  }

  // ── Data fetching ──────────────────────────────────────────────────────────
  private _advanceFtyScan(): void {
    const pack = this._ftyScanQueue.shift();
    if (!pack) {
      this.isFtyScanComplete.set(true);
      // filteredPackages is now computed from the completed ftyPackageSet and
      // preserves the original package list order seen by the user.
      const firstPkg = this.filteredPackages()[0]?.pack_name;
      if (firstPkg && !this.selectedPkg()) {
        // Normal case: auto-select the first confirmed package.
        // The effect will trigger _fetchStyles → _loadQuotations.
        this.selectedPkg.set(firstPkg);
      } else if (this.selectedPkg() && this.styles().length > 0) {
        // URL-nav case: selectedPkg was set before the scan started.
        // If the scan's quotation response arrived before styles were loaded,
        // styles.update() was a no-op. Load quotations now to hydrate the
        // already-visible styles.
        this._loadQuotations(this.selectedPkg());
      }
      return;
    }
    this._ftyScanPending = pack;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spLoadQuotations,
      Parameters: { pack_name: pack },
    });
  }

  private _fetchFactories(): void {
    this._sp.invoke('ExecuteStoredProcFlat', { SpName: this._spFactoryList });
  }

  private _fetchUomList(): void {
    this._sp.invoke('ExecuteStoredProcFlat', { SpName: this._spUomList });
  }

  private _fetchPackages(): void {
    this.isLoadingPkg.set(true);
    this._sp.invoke('ExecuteStoredProcFlat', { SpName: this._spPkg });
  }

  private _fetchStyles(pack_name: string): void {
    this.isLoadingStyles.set(true);
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spStyle,
      Parameters: { pack: pack_name },
    });
  }

  loadStyleBom(style: GqStyle): void {
    if (style.bomLoaded) return;
    // Auto-loader already has an in-flight request for this style — don't duplicate.
    if (this._bomAutoPending === style.style_id) return;
    this.isLoadingBom.set(true);
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spBom,
      Parameters: {
        customer: style.customer_id,
        style: style.style_id,
        season: style.season_id,
      },
    });
  }

  private _startBomAutoLoad(styles: GqStyle[]): void {
    // Only queue styles that don't already have BOM data (e.g. from a saved draft).
    this._bomAutoQueue = styles
      .filter(s => !s.bomLoaded)
      .map(s => ({ style_id: s.style_id, customer_id: s.customer_id, season_id: s.season_id }));
    this._bomAutoPending = '';
    this._advanceBomAutoLoad();
  }

  private _advanceBomAutoLoad(): void {
    // Skip already-loaded styles (draft may have been applied between queue start and now).
    while (this._bomAutoQueue.length > 0) {
      const next = this._bomAutoQueue[0];
      if (this.styles().find(s => s.style_id === next.style_id)?.bomLoaded) {
        this._bomAutoQueue.shift();
        continue;
      }
      break;
    }
    const style = this._bomAutoQueue.shift();
    if (!style) return;
    this._bomAutoPending = style.style_id;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spBom,
      Parameters: {
        customer: style.customer_id,
        style: style.style_id,
        season: style.season_id,
      },
    });
  }

  refreshStyles(): void {
    const pkg = this.selectedPkg();
    if (pkg) this._fetchStyles(pkg);
    this.toast('Data refreshed.', 'info');
  }

  // ── SignalR listeners ──────────────────────────────────────────────────────
  private _registerListeners(): void {
    this._sp.hubConn?.off('StoredProcResultFlat');
    this._sp.hubConn?.on('StoredProcResultFlat', (res: any) => {
      const rawData = res.data;
      const failed = Array.isArray(rawData)
        ? rawData.some((e: any) => e.status === 'Failed')
        : rawData?.status === 'Failed';
      const data = this._mergeToJson(res);
      this._spTrace('result:recv', {
        proc: res.procedure,
        failed,
        tables: Object.keys(data),
        table0Rows: (data['table0'] ?? []).length,
      });
      this._zone.run(() => this._handleResult(res.procedure, data, failed));
    });
  }

  private _handleResult(proc: string, data: Record<string, any[]>, spFailed = false): void {
    switch (proc) {

      case this._spSaveQuotation: {
        const pending = this._pendingSaveQuotationQueue.shift();
        if (spFailed && pending && !pending.retriedLegacy) {
          this._spTrace('result:saveQuotation:retryLegacy', {
            pendingModernKeys: Object.keys(pending.modern),
            pendingLegacyKeys: Object.keys(pending.legacy),
          });
          this._pendingSaveQuotationQueue.unshift({ ...pending, retriedLegacy: true });
          this._sp.invoke('ExecuteStoredProcFlat', {
            SpName: this._spSaveQuotation,
            Parameters: pending.legacy,
          });
          break;
        }

        if (spFailed) {
          this.toast('Quotation failed to save — refreshing from database.', 'danger');
          const pkg = this.selectedPkg();
          if (pkg) this._loadQuotations(pkg);
        }
        break;
      }

      case this._spSaveCosting: {
        const pending = this._pendingSaveCostingQueue.shift();
        if (spFailed && pending && !pending.retriedLegacy) {
          this._spTrace('result:saveCosting:retryLegacy', {
            pendingModernKeys: Object.keys(pending.modern),
            pendingLegacyKeys: Object.keys(pending.legacy),
          });
          this._pendingSaveCostingQueue.unshift({ ...pending, retriedLegacy: true });
          this._sp.invoke('ExecuteStoredProcFlat', {
            SpName: this._spSaveCosting,
            Parameters: pending.legacy,
          });
          break;
        }

        if (spFailed) {
          this.toast(
            'Costing history failed to save. Run the DB migration to add missing columns (pack_name, factory_name, factory_remarks, rate_in_usd) to tms_gq_costing_history.',
            'danger'
          );
        }
        break;
      }

      case this._spUomList: {
        const rows: GqUom[] = (data['table0'] ?? []).map((r: any) => ({
          uom_id:   String(r.UOM_ID   ?? '').trim(),
          uom_desc: String(r.UOM_DESC ?? '').trim(),
        }));
        this.uomList.set(rows);
        break;
      }

      case this._spFactoryList: {
        const rows: GqFactory[] = (data['table0'] ?? []).map((r: any) => ({
          id: String(r.Partner_ID ?? ''),
          name: String(r.Full_name ?? ''),
        }));
        this.factories.set(rows);
        break;
      }

      case this._spPkg: {
        this.isLoadingPkg.set(false);
        const pkgTable = this._pickRowsWithSource(data, 'table0');
        const rows: GqPackage[] = pkgTable.rows as GqPackage[];
        this._spTrace('result:pkgRows', {
          sourceTable: pkgTable.source,
          rowCount: rows.length,
          allTables: Object.keys(data),
        });
        this.packages.set(rows);
        if (this.factoryUserId()) {
          // For factory users: scan all packages immediately — do NOT auto-select.
          // Selection happens only after the scan confirms a package has their quotations.
          if (rows.length) {
            this.isFtyScanComplete.set(false);
            this._ftyScanQueue = rows.map(r => r.pack_name);
            this._advanceFtyScan();
          }
        } else if (rows.length && !this.selectedPkg()) {
          this.selectedPkg.set(rows[0].pack_name);
        }
        break;
      }

      case this._spStyle: {
        this.isLoadingStyles.set(false);
        const styleTable = this._pickRowsWithSource(data, 'table0');
        this._spTrace('result:styleRows', {
          sourceTable: styleTable.source,
          rowCount: styleTable.rows.length,
          allTables: Object.keys(data),
        });
        const rows: GqStyle[] = styleTable.rows.map((r: any) => ({
          customer_id: r.customer_id ?? '',
          style_id: r.style_id ?? '',
          season_id: r.season_id ?? '',
          prodcat_id: r.prodcat_id ?? '',
          model_name: r.model_name ?? '',
          composition: r.composition ?? '',
          curr_id: r.curr_id ?? 'RMB',
          rate_in_usd: parseFloat(r.rate_in_usd) || 6.55,
          style_image: r.style_image ?? '',
          selected: false,
          quotations: [],
          bom: [],
          bomLoaded: false,
        }));
        this.styles.set(rows);
        this._loadQuotations(this.selectedPkg());
        this._loadStylePricing(this.selectedPkg());
        this._loadBomDraft(this.selectedPkg());
        this._startBomAutoLoad(rows);
        break;
      }

      case this._spLoadStylePricing: {
        const rows: any[] = data['table0'] ?? [];
        if (!rows.length) break;
        const byStyle = new Map<string, { tms_fob: string; costing_curr: string }>();
        for (const r of rows) {
          byStyle.set(String(r.style_id), {
            tms_fob:      r.tms_fob != null ? String(r.tms_fob) : '',
            costing_curr: r.costing_curr ? String(r.costing_curr) : 'USD',
          });
        }
        this.styles.update(list =>
          list.map(s => {
            const p = byStyle.get(s.style_id);
            return p ? { ...s, tms_fob: p.tms_fob, costing_curr: p.costing_curr } : s;
          })
        );
        const sel = this.selectedStyle();
        if (sel) {
          const p = byStyle.get(sel.style_id);
          if (p) this.selectedStyle.update(st => st ? { ...st, tms_fob: p.tms_fob, costing_curr: p.costing_curr } : st);
        }
        break;
      }

      case this._spLoadBomDraft: {
        const rows: any[] = data['table0'] ?? [];
        for (const r of rows) {
          const bom = this._parseBomJson(r.bom_json ?? '');
          if (bom.length > 0) {
            const styleId = String(r.style_id);
            this._bomDraftCache.set(styleId, bom);
            // Apply draft to the style immediately so the style card totals
            // reflect the saved BOM without requiring the drawer to be opened.
            // Also mark bomLoaded so loadStyleBom skips the redundant DB fetch.
            this._styles_update(styleId, s => s.bomLoaded ? s : { ...s, bom, bomLoaded: true });
          }
        }
        break;
      }

      case this._spBom: {
        this.isLoadingBom.set(false);
        const bomRows = data['table0'] ?? [];

        // Auto-load queue has priority; fall back to the open drawer's style.
        const isAutoLoad = !!this._bomAutoPending;
        const styleId = this._bomAutoPending || this.selectedStyle()?.style_id || '';
        if (this._bomAutoPending) this._bomAutoPending = '';

        const current = this.styles().find(s => s.style_id === styleId) ?? this.selectedStyle() ?? null;
        if (current?.bomLoaded && current.bom.length > 0) {
          if (isAutoLoad) this._advanceBomAutoLoad();
          break;
        }

        const draft = this._bomDraftCache.get(styleId);
        if (!bomRows.length && !draft) {
          if (isAutoLoad) this._advanceBomAutoLoad();
          break;
        }

        const baseBom: GqBomItem[] = bomRows
          .filter((r: any) =>
            r.mat_class === 'FABRICS'
            || r.mat_class === 'TRIMS'
            || r.mat_class === 'ARTWORK'
            || r.mat_class === 'LABELS'
            || r.mat_class === 'PACKAGING'
          )
          .map((r: any): GqBomItem => ({
            mat_class:   r.mat_class,
            material:    r.material    != null ? String(r.material).trimEnd()    : null,
            part_desc:   r.part_desc   != null ? String(r.part_desc).trimEnd()   : '',
            consump:     r.consump     != null ? String(r.consump)               : null,
            consump_uom: r.consump_uom != null ? String(r.consump_uom).trim()    : null,
            wastage_pct: r.wastage_pct != null ? String(r.wastage_pct)           : null,
            curr_id:     r.curr_id ?? 'RMB',
            price:       r.price       != null ? String(r.price)                 : null,
            isCustom:    false,
          }));

        // Saved draft takes priority over the original DB BOM
        this._styles_update(styleId, s => ({
          ...s,
          bom: draft ?? baseBom,
          bomLoaded: true,
        }));

        if (isAutoLoad) this._advanceBomAutoLoad();
        break;
      }

      case this._spCurrencyConversion: {
        this.isRefreshingRate.set(false);
        const styleId = this._rateRefreshPendingStyleId ?? this.selectedStyle()?.style_id ?? '';
        this._rateRefreshPendingStyleId = null;
        if (!styleId) break;

        const rows: any[] = data['table0'] ?? [];
        const row = rows[0] ?? {};
        const rate = parseFloat(
          row.converted_amt || ''
        );
        if (!Number.isFinite(rate) || rate <= 0) {
          this.toast('Unable to refresh exchange rate.', 'danger');
          break;
        }

        this._styles_update(styleId, st => ({ ...st, rate_in_usd: rate }));
        const curr = this.styles().find(s => s.style_id === styleId)?.curr_id ?? 'RMB';
        this.toast(`Exchange rate refreshed: 1 USD = ${fmtNum(rate, 4)} ${curr}.`, 'success');
        break;
      }

      case this._spLoadRemarks: {
        const rows: any[] = data['table0'] ?? [];
        const s = this.selectedStyle();
        if (!s || !rows.length) break;
        const byQid = new Map<string, GqRemark[]>();
        for (const r of rows) {
          const list = byQid.get(r.qid) ?? [];
          list.push({
            rid: String(r.rid ?? r.remark_id ?? ''),
            role: r.role as UserRole,
            user: r.display_name ?? r.username ?? r.role,
            text: r.remark_text ?? '',
            ts: r.created_at
              ? new Date(r.created_at).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
              : '',
          });
          byQid.set(r.qid, list);
        }
        this._styles_update(s.style_id, st => ({
          ...st,
          quotations: st.quotations.map(q => byQid.has(q.qid) ? { ...q, remarks: byQid.get(q.qid)! } : q),
        }));
        break;
      }

      case this._spLoadQuotations: {
        const quoteTable = this._pickRowsWithSource(data, 'table0');
        const rows: any[] = quoteTable.rows;
        this._spTrace('result:quotationRows', {
          sourceTable: quoteTable.source,
          rowCount: rows.length,
          allTables: Object.keys(data),
        });
        if (!rows.length) {
          // Empty response — still advance the FTY scan so it doesn't stall on
          // packages that have no quotations at all.
          const ftyId = this.factoryUserId();
          if (ftyId && this._ftyScanPending) {
            this._ftyScanPending = '';
            this._advanceFtyScan();
          }
          break;
        }
        const byStyle = new Map<string, GqQuotation[]>();
        for (const r of rows) {
          const qs = byStyle.get(r.style_id) ?? [];
          let costing: GqCmtItem[];
          try { costing = r.cmt_json ? JSON.parse(r.cmt_json) : makeCmt(); }
          catch { costing = makeCmt(); }
          const qBom = r.bom_json ? this._parseBomJson(r.bom_json) : undefined;
          qs.push({
            qid: r.qid,
            factory: { id: r.factory_id, name: r.factory_name },
            status: r.status as QuotationStatus,
            bom_json: r.bom_json ?? '',
            bom: qBom?.length ? qBom : undefined,
            factoryCosting: costing,
            smv: r.smv ?? '',
            smv_rate: r.smv_rate ?? '',
            factory_remarks: r.factory_remarks ?? '',
            fc_qty: r.fc_qty ?? '',
            ttl_production: r.ttl_production ?? '',
            gross_margin_pct: r.gross_margin_pct ?? '',
            adjustment: r.adjustment ?? '',
            fob_price: r.fob_price ?? '',
            revised_factory_fob: r.revised_factory_fob ?? '',
            tms_comments: r.tms_comments ?? '',
            price_type: (r.price_type as PriceType) ?? 'FOB',
            fty_rate_usd: r.fty_rate_usd != null && r.fty_rate_usd !== ''
              ? (parseFloat(r.fty_rate_usd) || undefined)
              : undefined,
            rejectReason: r.reject_reason || null,
            remarks: [],
            sentAt: r.sent_at ?? '',
          });
          byStyle.set(r.style_id, qs);
        }
        this.styles.update(list =>
          list.map(s => {
            const qs = byStyle.get(s.style_id);
            return qs?.length ? { ...s, quotations: qs } : s;
          })
        );
        this._patchStyleDisplayFields(byStyle);
        const selected = this.selectedStyle();
        if (selected?.style_id) {
          const dbQs = byStyle.get(selected.style_id);
          if (dbQs?.length) {
            this.selectedStyle.update(st => {
              if (!st) return st;
              // Don't overwrite a factory quotation the user is actively editing —
              // an in-flight _loadQuotations (e.g. FTY scan final step) must not
              // discard unsaved CMT/BOM input.
              const merged = dbQs.map(dbQ => {
                const localQ = st.quotations.find(q => q.qid === dbQ.qid);
                return localQ && (localQ.status === 'sent_to_factory' || localQ.status === 'for_revision')
                  ? localQ
                  : dbQ;
              });
              return { ...st, quotations: merged };
            });
          }
        }

        // FTY package scan: track which packages have this factory's submissions.
        // Validate using pack_name from the response so a concurrent regular-load
        // response (e.g. triggered by _spStyle for the same package) is never
        // mistaken for the scan response, which would corrupt _ftyScanPending and
        // add wrong packages to ftyPackageSet.
        const ftyId = this.factoryUserId();
        if (ftyId && this._ftyScanPending) {
          const responsePack: string | undefined = rows[0]?.pack_name;
          const isScanResponse = !responsePack || responsePack === this._ftyScanPending;
          if (isScanResponse) {
            const hasFty = rows.some((r: any) => r.factory_id === ftyId);
            if (hasFty) {
              this.ftyPackageSet.update(s => new Set([...s, this._ftyScanPending]));

              // Set first scan-confirmed package as selected to trigger drawer population.
              if (!this.selectedPkg()) this.selectedPkg.set(this._ftyScanPending);
            }

            this._ftyScanPending = '';
            this._advanceFtyScan();
          }
          // If pack_name doesn't match, this is a regular-load response —
          // let it fall through to _applyPendingNav without touching scan state.
        }

        // Open drawer on Remarks if user navigated from a notification
        this._applyPendingNav();
        break;
      }

      case this._spCostingHistory: {
        this.isLoadingHistory.set(false);
        const rows: any[] = data['table0'] ?? [];
        const s = this.selectedStyle();
        const history: GqCostingHistory[] = rows.map((r: any): GqCostingHistory => {
          let cmt: import('./garment-quotation.model').GqCmtItem[] = [];
          try { cmt = r.cmt_json ? JSON.parse(r.cmt_json) : []; } catch { cmt = []; }
          const bom = this._parseBomJson(r.bom_json ?? '');
          const tmsRate = parseFloat(r.rate_in_usd) || s?.rate_in_usd || 6.55;
          const ftyRate = parseFloat(r.fty_rate_usd);
          const rate = this.role() === 'factory' && Number.isFinite(ftyRate) && ftyRate > 0
            ? ftyRate
            : tmsRate;
          const totals = calcTotals(bom.length ? bom : (s?.bom ?? []), cmt, rate);
          const adjustmentRmb = toRmb(parseFloat(r.adjustment) || 0, 'USD', rate);
          const margin = parseFloat(r.gross_margin_pct) || 0;
          return {
            historyId: r.history_id,
            qid: r.qid,
            factoryId: r.factory_id ?? '',
            factoryName: r.factory_name ?? '',
            status: r.status as QuotationStatus,
            changedBy: r.changed_by ?? '',
            changedAt: r.changed_at
              ? new Date(r.changed_at).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
              : '',
            rateInUsd: rate,
            cmt,
            bom,
            smv: r.smv ?? '',
            smvRate: r.smv_rate ?? '',
            fcQty: r.fc_qty ?? '',
            fobPrice: r.fob_price ?? '',
            grossMarginPct: r.gross_margin_pct ?? '',
            adjustment: r.adjustment ?? '',
            priceType: (r.price_type as PriceType) ?? 'FOB',
            factoryRemarks: r.factory_remarks ?? '',
            fabricTotal: totals.fabricTotal,
            trimTotal: totals.trimTotal,
            cmtTotal: totals.cmtTotal,
            total: totals.total,
            fobPriceRmb: (totals.total * (1 + margin / 100)) + adjustmentRmb,
          };
        });
        const qid = rows[0]?.qid ?? '';
        if (qid) this._historyCache.set(qid, history);
        this.costingHistory.set(history);
        break;
      }
    }
  }

  // ── Drawer open/close ──────────────────────────────────────────────────────
  openDrawer(style: GqStyle): void {
    this.selectedStyle.set({ ...style });
    this.activeTab.set('bom');
    this.activeQIdx.set(0);
    this.bomShowOriginal.set(true);
    this.drawerOpen.set(true);
    this.loadStyleBom(style);
    this._fetchRemarks(style);
  }

  closeDrawer(): void { this.drawerOpen.set(false); }

  // ── Selection ──────────────────────────────────────────────────────────────
  onStyleSelect(styleId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.styles.update(list =>
      list.map(s => s.style_id === styleId ? { ...s, selected: checked } : s)
    );
  }

  // ── BOM mutations ──────────────────────────────────────────────────────────
  onRateChange(event: Event): void {
    const val = parseFloat((event.target as HTMLInputElement).value) || 6.55;
    const s = this.selectedStyle();
    if (!s) return;
    this._styles_update(s.style_id, st => ({ ...st, rate_in_usd: val }));
  }

  onFtyRateChange(qid: string, event: Event): void {
    const v = parseFloat((event.target as HTMLInputElement).value);
    const next = Number.isFinite(v) && v > 0 ? v : undefined;
    this._mutateQ(qid, q => ({ ...q, fty_rate_usd: next }));
  }

  refreshExchangeRate(): void {
    const style = this.selectedStyle();
    if (!style || this.isRefreshingRate()) return;

    this._rateRefreshPendingStyleId = style.style_id;
    this.isRefreshingRate.set(true);
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spCurrencyConversion,
      Parameters: {
        from: 'USD',
        to: style.curr_id || 'RMB',
        amt: 1,
      },
    });
  }

  onBomFieldChange(idx: number, field: keyof GqBomItem, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    const s = this.selectedStyle();
    if (!s) return;
    if (this.canEditBomFty()) {
      const q = this.selQ();
      if (!q) return;
      this._mutateQBom(q.qid, bom => {
        const next = [...bom];
        const item: GqBomItem = { ...next[idx], [field]: value };
        const edited = new Set(item.factoryEditedFields ?? []);
        edited.add(field as string);
        item.factoryEditedFields = [...edited];
        next[idx] = item;
        return next;
      });
    } else {
      this._styles_update(s.style_id, st => {
        const bom = [...st.bom];
        bom[idx] = { ...bom[idx], [field]: value };
        return { ...st, bom };
      });
    }
  }

  private _mutateQBom(qid: string, fn: (bom: GqBomItem[]) => GqBomItem[]): void {
    const s = this.selectedStyle();
    if (!s) return;
    this._styles_update(s.style_id, st => ({
      ...st,
      quotations: st.quotations.map(q => {
        if (q.qid !== qid) return q;
        const base = q.bom?.length ? q.bom : st.bom.map(b => ({ ...b }));
        return { ...q, bom: fn(base) };
      }),
    }));
  }

  onBomAdd(matClass: string): void {
    const s = this.selectedStyle();
    if (!s) return;
    const item: GqBomItem = {
      mat_class: matClass === 'UNCLASSIFIED' ? null : matClass,
      material: '', part_desc: '',
      consump: '', consump_uom: null, wastage_pct: '0', curr_id: 'RMB', price: '', isCustom: true,
    };
    this._styles_update(s.style_id, st => ({ ...st, bom: [...st.bom, item] }));
  }

  saveBomDraft(): void {
    const s = this.selectedStyle();
    if (!s || this.isSavingBom()) return;
    this._bomDraftCache.set(s.style_id, [...s.bom]);
    this.isSavingBom.set(true);
    this._saveBomDraftToDb(s);
    this.toast(this._transloco.translate('gq.bom.draftSaved'), 'success');
    setTimeout(() => this.isSavingBom.set(false), 600);
  }

  saveFactoryCosting(): void {
    const q = this.selQ();
    if (!q || this.isSavingCosting()) return;
    this.isSavingCosting.set(true);
    this._saveQuotation(q.qid);
    this.toast(this._transloco.translate('gq.costing.draftSaved'), 'success');
    setTimeout(() => this.isSavingCosting.set(false), 600);
  }

  onBomRemove(idx: number): void {
    const s = this.selectedStyle();
    if (!s) return;
    if (this.canEditBomFty()) {
      const q = this.selQ();
      if (!q) return;
      this._mutateQBom(q.qid, bom => bom.filter((_, i) => i !== idx));
    } else {
      this._styles_update(s.style_id, st => ({
        ...st, bom: st.bom.filter((_, i) => i !== idx),
      }));
    }
  }

  // ── Add BOM Item modal ─────────────────────────────────────────────────────
  openAddBomModal(): void {
    this.addBomMatClass.set('FABRICS');
    this.addBomMaterial.set('');
    this.addBomDesc.set('');
    this.addBomConsump.set('');
    this.addBomUom.set('');
    this.addBomPrice.set('');
    this.addBomWastage.set('0');
    this.addBomCurrency.set(this.globalCurrency() || 'RMB');
    this.addBomModal.set(true);
  }

  onAddBomMatClassChange(event: Event): void {
    this.addBomMatClass.set((event.target as HTMLSelectElement).value);
  }

  onAddBomMaterialInput(event: Event): void {
    this.addBomMaterial.set((event.target as HTMLInputElement).value);
  }

  onAddBomDescInput(event: Event): void {
    this.addBomDesc.set((event.target as HTMLInputElement).value);
  }

  onAddBomCurrencyChange(event: Event): void {
    this.addBomCurrency.set((event.target as HTMLSelectElement).value);
  }

  onAddBomUomChange(event: Event): void {
    this.addBomUom.set((event.target as HTMLSelectElement).value);
  }

  confirmAddBomItem(): void {
    const s = this.selectedStyle();
    if (!s) return;
    const isFty = this.canEditBomFty();
    const item: GqBomItem = {
      mat_class:       this.addBomMatClass(),
      material:        this.addBomMaterial().trim() || null,
      part_desc:       this.addBomDesc().trim(),
      consump:         this.addBomConsump().trim() || null,
      consump_uom:     this.addBomUom() || null,
      wastage_pct:     this.addBomWastage() || '0',
      curr_id:         this.addBomCurrency(),
      price:           this.addBomPrice().trim() || null,
      isCustom:        true,
      addedByFactory:  isFty ? true : undefined,
    };

    if (isFty) {
      const q = this.selQ();
      if (!q) return;
      this._mutateQBom(q.qid, bom => this._insertBomItemOrdered(bom, item));
    } else {
      this._styles_update(s.style_id, st => ({
        ...st, bom: this._insertBomItemOrdered(st.bom, item),
      }));
    }
    this.addBomModal.set(false);
    this.toast(this._transloco.translate('gq.bom.addItemModal.added'), 'success');
  }

  private _insertBomItemOrdered(bom: GqBomItem[], item: GqBomItem): GqBomItem[] {
    const result = [...bom];
    const targetClass = item.mat_class || 'UNCLASSIFIED';
    const order = MAT_CLASS_ORDER as readonly string[];
    const targetOrder = order.indexOf(targetClass);
    let insertIdx = result.length;

    if (targetOrder >= 0) {
      // After the last existing item in the same class
      for (let i = result.length - 1; i >= 0; i--) {
        if (result[i].mat_class === targetClass) { insertIdx = i + 1; break; }
      }
      // If no same-class item found, insert before the first item of a later class
      if (insertIdx === result.length) {
        for (let i = 0; i < result.length; i++) {
          const io = order.indexOf(result[i].mat_class || 'UNCLASSIFIED');
          if (io > targetOrder) { insertIdx = i; break; }
        }
      }
    }
    result.splice(insertIdx, 0, item);
    return result;
  }

  matClassLabel(mc: string): string {
    return MAT_CLASS_LABELS[mc] ?? mc;
  }

  // ── CMT mutations ──────────────────────────────────────────────────────────
  onCmtFieldChange(qid: string, idx: number, field: keyof GqCmtItem, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this._mutateQ(qid, q => {
      const costing = [...q.factoryCosting];
      costing[idx] = { ...costing[idx], [field]: value };
      return { ...q, factoryCosting: costing };
    });
  }

  onSmvChange(qid: string, field: 'smv' | 'smv_rate', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    const q = this.selQ();
    if (!q) return;
    const smv = field === 'smv' ? parseFloat(value) || 0 : parseFloat(q.smv) || 0;
    const smvRate = field === 'smv_rate' ? parseFloat(value) || 0 : parseFloat(q.smv_rate) || 0;
    this._mutateQ(qid, qt => {
      const costing = [...qt.factoryCosting];
      if (smv > 0 && smvRate > 0) {
        costing[0] = { ...costing[0], value: (smv * smvRate).toFixed(2) };
      }
      return { ...qt, [field]: value, factoryCosting: costing };
    });
  }

  onCmtAdd(qid: string): void {
    this._mutateQ(qid, q => ({
      ...q,
      factoryCosting: [
        ...q.factoryCosting,
        { key: `custom_${Date.now()}`, label: '', value: '', description: '', curr_id: 'RMB', isCustom: true },
      ],
    }));
  }

  onCmtRemove(qid: string, idx: number): void {
    this._mutateQ(qid, q => ({
      ...q, factoryCosting: q.factoryCosting.filter((_, i) => i !== idx),
    }));
  }

  onQFieldChange(qid: string, field: string, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
    if (field === 'revised_factory_fob') {
      const found = this._findStyleAndQuotation(qid);
      if (!found) return;
      this._mutateQ(qid, q => {
        const next = { ...q, revised_factory_fob: value };
        const suggested = this._suggestedAdjustmentFor(found.style, next);
        return {
          ...next,
          adjustment: suggested === null ? '' : fmtNum(suggested),
        };
      });
      return;
    }
    this._mutateQ(qid, q => ({ ...q, [field]: value }));
  }

  onPriceTypeChange(qid: string, pt: PriceType): void {
    this._mutateQ(qid, q => ({ ...q, price_type: pt }));
  }

  // ── Remarks ────────────────────────────────────────────────────────────────
  onRemarkKeydown(event: KeyboardEvent, qid: string): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendRemark(qid);
    }
  }

  sendRemark(qid: string): void {
    const text = this.remarkText().trim();
    if (!text) return;
    const s = this.selectedStyle();
    const q = s?.quotations.find(qt => qt.qid === qid);
    if (!q || !s) return;
    const u = this._userInfo.user;
    const displayName = u
      ? `${u.firstName}${u.lastName ? ' ' + u.lastName : ''}`.trim()
      : (this.role() === 'merchandiser' ? 'Merchandiser' : `Factory (${q.factory.name})`);
    const rid = `r_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
    const remark: GqRemark = { rid, role: this.role(), user: displayName, text, ts: nowTs() };
    this._mutateQ(qid, qt => ({ ...qt, remarks: [...qt.remarks, remark] }));
    this.remarkText.set('');
    this._saveRemark(qid, rid, text, q.factory);

    // Send notification to opposite group
    const isMerchandiser = this.role() === 'merchandiser';
    const recipientGroup = isMerchandiser ? `FTY_${q.factory.id}` : 'TMS';
    this._notifHub.sendNotification(
      'remark',
      s.style_id,
      this.selectedPkg(),
      q.factory.id,
      q.factory.name,
      u?.username ?? 'unknown',
      displayName,
      recipientGroup,
      `Message regarding ${s.style_id}: ${text.substring(0, 100)}`
    );
  }

  // ── BOM global defaults ────────────────────────────────────────────────────
  onGlobalCurrencyChange(event: Event): void {
    this.globalCurrency.set((event.target as HTMLSelectElement).value);
  }

  onGlobalWastageInput(event: Event): void {
    this.globalWastagePct.set((event.target as HTMLInputElement).value);
  }

  applyGlobalCurrency(): void {
    const s = this.selectedStyle();
    if (!s) return;
    const curr = this.globalCurrency();
    this._styles_update(s.style_id, st => ({ ...st, bom: st.bom.map(b => ({ ...b, curr_id: curr })) }));
    this.toast(`Currency set to ${curr} for all BOM items.`, 'info');
  }

  applyGlobalWastage(): void {
    const s = this.selectedStyle();
    if (!s) return;
    const pct = this.globalWastagePct();
    this._styles_update(s.style_id, st => ({ ...st, bom: st.bom.map(b => ({ ...b, wastage_pct: pct })) }));
    this.toast(`Wastage ${pct}% applied to all BOM items.`, 'info');
  }

  // ── CMT global currency ────────────────────────────────────────────────────
  onGlobalCmtCurrencyChange(event: Event): void {
    this.cmtGlobalCurrency.set((event.target as HTMLSelectElement).value);
  }

  onTmsFobChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    const s = this.selectedStyle();
    if (!s) return;
    this._styles_update(s.style_id, st => ({ ...st, tms_fob: value }));
    this._saveStylePricing(s.style_id);
  }

  onCostingCurrChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const s = this.selectedStyle();
    if (!s) return;
    this._styles_update(s.style_id, st => ({ ...st, costing_curr: value }));
    this._saveStylePricing(s.style_id);
  }

  applyCmtGlobalCurrency(qid: string): void {
    const curr = this.cmtGlobalCurrency();
    this._mutateQ(qid, q => ({
      ...q,
      factoryCosting: q.factoryCosting.map(item => ({ ...item, curr_id: curr })),
    }));
    this.toast(`CMT currency set to ${curr} for all cost items.`, 'info');
  }

  exportPdf(): void { window.print(); }

  async exportExcel(): Promise<void> {
    const style = this.selectedStyle();
    const q = this.selQ();
    if (!style || !q) {
      this.toast('Open a style and select a factory quotation first.', 'danger');
      return;
    }

    const totals = calcTotals(q.bom ?? style.bom, q.factoryCosting, effRate(style, q, this.role()));
    const imageSrc = await this._imageToDataUrl(style.style_id, style.style_image);
    const html = this._buildExcelHtml(style, q, totals, imageSrc);
    const blob = new Blob([html], { type: 'application/vnd.ms-excel;charset=utf-8' });
    this._downloadBlob(blob, `GarmentQuotation_${this._safeFileName(style.style_id)}_${this._safeFileName(q.factory.name)}.xls`);
    this.toast('Excel export generated.', 'success');
  }

  // ── Actions ────────────────────────────────────────────────────────────────
  async handleAction(action: string, qid?: string): Promise<void> {
    if (action === 'open_factory_modal') {
      this.chosenFactories.set([]);
      this.factoryScope.set(this.role() === 'factory' ? 'all' : 'selected');
      this.factorySearch.set('');
      this.factoryPage.set(0);
      this.batchRows.set({});
      this._seedBatchRows();
      this.factModal.set(true);
      return;
    }
    if (action === 'open_reject') {
      this.pendingQid.set(qid ?? null);
      this.rejectReason.set('');
      this.rejectModal.set(true);
      return;
    }
    if (!qid) return;

    const found = this._findStyleAndQuotation(qid);
    if (!found) return;
    const { style: s, q } = found;

    if (action === 'approve') {
      const confirmed = await this._confirmDlg.confirm({
        title: this._transloco.translate('gq.confirmDialogs.approveCosting.title'),
        message: this._transloco.translate('gq.confirmDialogs.approveCosting.message', {
          factory: q.factory.name,
          style: s.style_id,
        }),
        confirmLabel: this._transloco.translate('gq.confirmDialogs.approveCosting.confirmLabel'),
        cancelLabel: this._transloco.translate('gq.confirmDialogs.approveCosting.cancelLabel'),
        variant: 'info',
      });
      if (!confirmed) return;
    }

    if (action === 'revise') {
      const confirmed = await this._confirmDlg.confirm({
        title: this._transloco.translate('gq.confirmDialogs.reviseCosting.title'),
        message: this._transloco.translate('gq.confirmDialogs.reviseCosting.message', {
          factory: q.factory.name,
          style: s.style_id,
        }),
        confirmLabel: this._transloco.translate('gq.confirmDialogs.reviseCosting.confirmLabel'),
        cancelLabel: this._transloco.translate('gq.confirmDialogs.reviseCosting.cancelLabel'),
        variant: 'warning',
      });
      if (!confirmed) return;
    }

    if (action === 'unapprove') {
      const confirmed = await this._confirmDlg.confirm({
        title: 'Unapprove Costing',
        message: `Revert approved costing for ${s.style_id} / ${q.factory.name} back to Submitted?`,
        confirmLabel: 'Unapprove',
        cancelLabel: this._transloco.translate('gq.confirmDialogs.reviseCosting.cancelLabel'),
        variant: 'warning',
      });
      if (!confirmed) return;
    }

    this._mutateQ(qid, qt => {
      switch (action) {
        // Merchandiser recalls a sent quotation — preserves all factory costing data
        case 'recall': return { ...qt, status: 'new' };
        // Merchandiser resends a recalled or rejected quotation to the same factory
        case 'resend': return { ...qt, status: 'sent_to_factory', rejectReason: null, sentAt: nowTs() };
        case 'approve': return { ...qt, status: 'approved' };
        // Merchandiser requests factory to revise their costing
        case 'revise': return { ...qt, status: 'for_revision', rejectReason: null };
        case 'unapprove': return { ...qt, status: 'factory_submitted', rejectReason: null };
        case 'factory_submit': return { ...qt, status: 'factory_submitted', rejectReason: null };
        // Factory recalls their own costing submission for revision
        case 'factory_recall': return { ...qt, status: 'sent_to_factory' };
        default: return qt;
      }
    });

    // Persist current state after every status transition
    if (['recall', 'resend', 'approve', 'revise', 'unapprove', 'factory_submit', 'factory_recall'].includes(action)) {
      this._saveQuotation(qid);
    }

    // Snapshot costing history on meaningful costing events
    if (action === 'resend') this._saveCosting(qid, 'sent_to_factory');
    if (action === 'factory_submit') this._saveCosting(qid, 'factory_submitted');
    if (action === 'revise') this._saveCosting(qid, 'for_revision');
    if (action === 'unapprove') this._saveCosting(qid, 'factory_submitted');
    if (action === 'approve') {
      this._saveCosting(qid, 'approved');
      this._approveCosting(qid);
    }
    // 'rejected' snapshot is saved in confirmReject() after the reason is captured

    // Send notifications
    const u = this._userInfo.user;
    const isMerchandiser = this.role() === 'merchandiser';
    const recipientGroup = isMerchandiser ? `FTY_${q.factory.id}` : 'TMS';
    const notifMsgs: Record<string, [type: string, msg: string]> = {
      recall: ['recall', `Quotation for ${s.style_id} recalled`],
      resend: ['sent', `Quotation for ${s.style_id} resent`],
      approve: ['approval', `Quotation for ${s.style_id} approved`],
      revise: ['revision', `Costing for ${s.style_id} sent back for revision`],
      unapprove: ['revision', `Approval for ${s.style_id} reverted to submitted`],
      factory_submit: ['submission', `Costing for ${s.style_id} submitted`],
      factory_recall: ['recall', `Costing submission recalled for ${s.style_id}`],
    };

    if (notifMsgs[action]) {
      const [notifType, notifMsg] = notifMsgs[action];
      const displayName = u
        ? `${u.firstName}${u.lastName ? ' ' + u.lastName : ''}`.trim()
        : (isMerchandiser ? 'Merchandiser' : `Factory (${q.factory.name})`);
      this._notifHub.sendNotification(
        notifType,
        s.style_id,
        this.selectedPkg(),
        q.factory.id,
        q.factory.name,
        u?.username ?? 'unknown',
        displayName,
        recipientGroup,
        notifMsg
      );
    }

    const msgs: Record<string, [string, GqToast['type']]> = {
      recall: ['Quotation recalled. BOM is editable again.', 'info'],
      resend: ['Quotation resent to factory.', 'success'],
      approve: ['Costing approved!', 'success'],
      revise: ['Costing sent back to factory for revision.', 'info'],
      unapprove: ['Approval reverted. Costing is back to Submitted.', 'info'],
      factory_submit: ['Costing submitted for review.', 'success'],
      factory_recall: ['Submission recalled. You can revise and resubmit.', 'info'],
    };
    const [msg, type] = msgs[action] ?? ['Done.', 'info'];
    this.toast(msg, type);
  }

  // ── Factory modal ──────────────────────────────────────────────────────────
  onFactorySearchInput(event: Event): void {
    this.factorySearch.set((event.target as HTMLInputElement).value);
    this.factoryPage.set(0);
  }

  factoryPagePrev(): void { this.factoryPage.update(p => Math.max(0, p - 1)); }

  factoryPageNext(): void {
    this.factoryPage.update(p => Math.min(this.factoryPageCount() - 1, p + 1));
  }

  batchDisplayRows(): BatchDisplayRow[] {
    if (this.role() === 'factory') {
      return this.baseStyles().flatMap(style =>
        style.quotations.map(q => ({
          key: q.qid,
          style,
          q,
          factory: q.factory,
          isDraft: false,
        }))
      );
    }

    const styleIds = new Set(this._factoryScopeStyleIds());
    const selectedFactories = this.chosenFactories();
    return this.styles()
      .filter(style => styleIds.has(style.style_id))
      .flatMap(style => {
        const rows: BatchDisplayRow[] = style.quotations.map(q => ({
          key: q.qid,
          style,
          q,
          factory: q.factory,
          isDraft: false,
        }));

        for (const fid of selectedFactories) {
          if (style.quotations.some(q => q.factory.id === fid)) continue;
          const factory = this.factories().find(f => f.id === fid) ?? null;
          if (!factory) continue;
          rows.push({
            key: this._batchDraftKey(style.style_id, fid),
            style,
            q: null,
            factory,
            isDraft: true,
          });
        }

        if (!rows.length) {
          rows.push({ key: this._batchStyleKey(style.style_id), style, q: null, factory: null, isDraft: true });
        }
        return rows;
      });
  }

  batchRow(key: string, row?: BatchDisplayRow): BatchRowState {
    const existing = this.batchRows()[key];
    if (existing) return existing;
    if (row) return this._defaultBatchRow(row);
    return { comments: '', tmsPrice: '', factoryPrice: '', revisedPrice: '' };
  }

  onFactoryScopeChange(scope: 'selected' | 'all'): void {
    this.factoryScope.set(scope);
    this._seedBatchRows();
  }

  onBatchFieldChange(rowKey: string, field: 'comments' | 'tmsPrice' | 'factoryPrice' | 'revisedPrice', event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    this.batchRows.update(rows => ({
      ...rows,
      [rowKey]: { ...this.batchRow(rowKey), [field]: value },
    }));

    if (field === 'revisedPrice') {
      const row = this.batchDisplayRows().find(displayRow => displayRow.key === rowKey);
      if (row?.q && row.q.status === 'for_revision') {
        const adjustment = this._adjustmentForRevisedFactoryFob(row.style, row.q, value);
        this._mutateQ(row.q.qid, q => ({
          ...q,
          revised_factory_fob: value,
          adjustment: adjustment === null ? '' : fmtNum(adjustment),
        }));
      }
    }
  }

  batchFactoryPrice(row: BatchDisplayRow): number | null {
    if (!row.q) return null;
    if (this._hasFactoryCostingValues(row.q)) {
      const value = this.baseFobPriceDisplay(row.style, row.q).usd;
      return Number.isFinite(value) && value > 0 ? value : null;
    }
    return row.q.fob_price ? parseFloat(row.q.fob_price) || null : null;
  }

  canEditBatchPrice(row: BatchDisplayRow): boolean {
    return row.q?.status === 'sent_to_factory';
  }

  canEditBatchRevisedPrice(row: BatchDisplayRow): boolean {
    return row.q?.status === 'for_revision';
  }

  batchMargin(row: BatchDisplayRow): string {
    const state = this.batchRow(row.key, row);
    const tms = parseFloat(state.tmsPrice) || 0;
    const factory = parseFloat(state.revisedPrice || state.factoryPrice) || this.batchFactoryPrice(row) || 0;
    if (!tms || !factory) return '-';
    const margin = ((tms - factory) / tms) * 100;
    return `${margin >= 0 ? '+' : ''}${fmtNum(margin, 1)}%`;
  }

  batchStatus(row: BatchDisplayRow): string {
    if (row.isDraft) return 'To Send';
    return this.statusLabelKey(row.q?.status ?? deriveStatus(row.style));
  }

  batchAction(row: BatchDisplayRow): 'submit' | 'recall' | null {
    const status = row.q?.status;
    if (status === 'sent_to_factory' || status === 'for_revision') return 'submit';
    if (status === 'factory_submitted') return 'recall';
    return null;
  }

  batchSendCount(): number {
    return this._batchDispatchRows().length;
  }

  batchSendFactoryCount(): number {
    return new Set(this._batchDispatchRows().map(row => row.factory?.id).filter(Boolean)).size;
  }

  async batchTmsAction(row: BatchDisplayRow, action: 'recall' | 'approve' | 'revise' | 'reject' | 'unapprove'): Promise<void> {
    if (this.role() !== 'merchandiser' || !row.q) return;
    this._applyBatchTmsRow(row);
    await this.handleAction(action === 'reject' ? 'open_reject' : action, row.q.qid);
    this.batchRows.set({});
    this._seedBatchRows();
  }

  toggleFactoryChoice(id: string): void {
    this.chosenFactories.update(list =>
      list.includes(id) ? list.filter(x => x !== id) : [...list, id]
    );
    this._seedBatchRows();
  }

  isFactoryChosen(id: string): boolean {
    return this.chosenFactories().includes(id);
  }

  isFactoryAlreadyAssigned(id: string): boolean {
    const styleIds = new Set(this._factoryScopeStyleIds());
    const scoped = this.styles().filter(s => styleIds.has(s.style_id));
    return scoped.length > 0 && scoped.every(s => s.quotations?.some(q => q.factory.id === id));
  }

  isFavorite(id: string): boolean {
    return this.favoriteFactories().has(id);
  }

  toggleFavorite(id: string): void {
    this.favoriteFactories.update(favs => {
      const next = new Set(favs);
      if (next.has(id)) next.delete(id); else next.add(id);
      try { localStorage.setItem('gq_fav_factories', JSON.stringify([...next])); } catch { }
      return next;
    });
    this.factoryPage.set(0);
  }

  private _loadFavorites(): Set<string> {
    try {
      const raw = localStorage.getItem('gq_fav_factories');
      return new Set(raw ? JSON.parse(raw) : []);
    } catch { return new Set(); }
  }

  private _factoryScopeStyleIds(): string[] {
    if (this.role() === 'factory') return this.baseStyles().map(st => st.style_id);
    const scope = this.factoryScope();
    const s = this.selectedStyle();
    return scope === 'all'
      ? this.styles().map(st => st.style_id)
      : this.selectedIds().length > 0 ? this.selectedIds() : s ? [s.style_id] : [];
  }

  private _batchDraftKey(styleId: string, factoryId: string): string {
    return `draft::${styleId}::${factoryId}`;
  }

  private _batchStyleKey(styleId: string): string {
    return `style::${styleId}`;
  }

  private _defaultBatchRow(row: BatchDisplayRow): BatchRowState {
    const factoryPrice = this.batchFactoryPrice(row);
    return {
      comments: row.q?.tms_comments ?? row.q?.factory_remarks ?? '',
      tmsPrice: row.style.tms_fob ?? '',
      factoryPrice: factoryPrice === null ? '' : fmtNum(factoryPrice),
      revisedPrice: row.q?.revised_factory_fob ?? '',
    };
  }

  private _hasFactoryCostingValues(q: GqQuotation): boolean {
    return q.factoryCosting.some(item => (parseFloat(item.value) || 0) > 0);
  }

  private _applyBatchTmsRow(row: BatchDisplayRow): void {
    if (!row.q) return;
    const qid = row.q.qid;
    const state = this.batchRow(row.key, row);
    const nextTmsPrice = state.tmsPrice.trim();
    const nextComments = state.comments.trim();

    this._styles_update(row.style.style_id, st => ({
      ...st,
      tms_fob: nextTmsPrice || st.tms_fob,
      quotations: st.quotations.map(qt => qt.qid === qid
        ? {
            ...qt,
            tms_comments: nextComments,
            factory_remarks: nextComments || qt.factory_remarks,
          }
        : qt),
    }));

    if (nextTmsPrice && nextTmsPrice !== row.style.tms_fob) {
      this._saveStylePricing(row.style.style_id);
    }
  }

  private _batchRowFor(styleId: string, factoryId?: string, qid?: string): BatchRowState {
    const rows = this.batchRows();
    if (qid && rows[qid]) return rows[qid];
    if (factoryId && rows[this._batchDraftKey(styleId, factoryId)]) return rows[this._batchDraftKey(styleId, factoryId)];
    if (rows[this._batchStyleKey(styleId)]) return rows[this._batchStyleKey(styleId)];
    return { comments: '', tmsPrice: '', factoryPrice: '', revisedPrice: '' };
  }

  private _firstBatchRowForStyle(styleId: string): BatchRowState | null {
    const row = this.batchDisplayRows().find(displayRow => displayRow.style.style_id === styleId);
    return row ? this.batchRow(row.key, row) : null;
  }

  private _batchDispatchRows(): BatchDisplayRow[] {
    if (this.role() !== 'merchandiser') return [];
    const chosen = new Set(this.chosenFactories());
    return this.batchDisplayRows().filter(row =>
      row.isDraft &&
      !!row.factory &&
      chosen.has(row.factory.id)
    );
  }

  private _seedBatchRows(): void {
    const current = this.batchRows();
    const next: Record<string, BatchRowState> = { ...current };
    for (const row of this.batchDisplayRows()) {
      if (!next[row.key]) next[row.key] = this._defaultBatchRow(row);
    }
    this.batchRows.set(next);
  }

  confirmFactory(): void {
    const rowsToSend = this._batchDispatchRows();
    if (!rowsToSend.length) {
      this.toast('No new quotation rows to send.', 'info');
      return;
    }

    const s = this.selectedStyle();
    const idsToSend = [...new Set(rowsToSend.map(row => row.style.style_id))];
    const factoriesByStyle = new Map<string, string[]>();
    for (const row of rowsToSend) {
      if (!row.factory) continue;
      factoriesByStyle.set(row.style.style_id, [
        ...(factoriesByStyle.get(row.style.style_id) ?? []),
        row.factory.id,
      ]);
    }

    // Capture existing assignments before update so we know which quotations are new
    const prevByStyle = new Map<string, Set<string>>();
    for (const styleId of idsToSend) {
      const st = this.styles().find(x => x.style_id === styleId);
      if (st) prevByStyle.set(styleId, new Set(st.quotations.map(q => q.factory.id)));
    }

    this.styles.update(list =>
      list.map(st => {
        if (!idsToSend.includes(st.style_id)) return st;
        const existing = new Set(st.quotations.map(q => q.factory.id));
        const newQs = (factoriesByStyle.get(st.style_id) ?? [])
          .filter(fid => !existing.has(fid))
          .map(fid => this._makeBatchQuotation(this.factories().find((f: GqFactory) => f.id === fid)!, this._batchRowFor(st.style_id, fid)));
        const row = this._firstBatchRowForStyle(st.style_id);
        return { ...st, selected: false, tms_fob: row?.tmsPrice || st.tms_fob, quotations: [...st.quotations, ...newQs] };
      })
    );

    if (s && idsToSend.includes(s.style_id)) {
      this.selectedStyle.update(st => {
        if (!st) return st;
        const existing = new Set(st.quotations.map(q => q.factory.id));
        const newQs = (factoriesByStyle.get(st.style_id) ?? [])
          .filter(fid => !existing.has(fid))
          .map(fid => this._makeBatchQuotation(this.factories().find((f: GqFactory) => f.id === fid)!, this._batchRowFor(st.style_id, fid)));
        return { ...st, tms_fob: this._firstBatchRowForStyle(st.style_id)?.tmsPrice || st.tms_fob, quotations: [...st.quotations, ...newQs] };
      });
    }

    // Persist newly created quotations and send notifications
    const updatedStyles = this.styles();
    const u = this._userInfo.user;
    const displayName = u
      ? `${u.firstName}${u.lastName ? ' ' + u.lastName : ''}`.trim()
      : 'Merchandiser';

    for (const styleId of idsToSend) {
      const st = updatedStyles.find(x => x.style_id === styleId);
      if (!st) continue;
      const row = this._firstBatchRowForStyle(styleId);
      if (row?.tmsPrice && row.tmsPrice !== st.tms_fob) {
        this._styles_update(st.style_id, cur => ({ ...cur, tms_fob: row.tmsPrice }));
        this._saveStylePricing(st.style_id);
      }
      const prev = prevByStyle.get(styleId) ?? new Set<string>();
      const newQuotations = st.quotations.filter(q => !prev.has(q.factory.id));

      for (const q of newQuotations) {
        this._saveQuotationState(st, q);
        // Log dispatch in costing history so dashboard's avg-turnaround
        // metric (sent → submitted) has a baseline timestamp.
        this._saveCostingState(st, q, 'sent_to_factory');

        // Send 'sent' notification to factory group
        this._notifHub.sendNotification(
          'sent',
          st.style_id,
          this.selectedPkg(),
          q.factory.id,
          q.factory.name,
          u?.username ?? 'unknown',
          displayName,
          `FTY_${q.factory.id}`,
          `Quotation for ${st.style_id} sent for review`
        );

        if (q.tms_comments) {
          const rid = `r_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
          this._saveRemarkForStyle(st, q.qid, rid, q.tms_comments, q.factory);
        }
      }
    }

    this.factModal.set(false);
    this.toast(`Sent ${rowsToSend.length} quotation${rowsToSend.length === 1 ? '' : 's'} to ${this.batchSendFactoryCount()} factor${this.batchSendFactoryCount() === 1 ? 'y' : 'ies'}.`, 'success');
  }

  saveFactoryBatchQuotation(): void {
    if (this.role() !== 'factory') return;
    const rows = this.batchRows();
    const ftyId = this.factoryUserId();
    const touchedQids: string[] = [];

    for (const displayRow of this.batchDisplayRows()) {
      const row = rows[displayRow.key] ?? this.batchRow(displayRow.key, displayRow);
      const st = displayRow.style;
      const q = displayRow.q ?? st.quotations.find(qt => !ftyId || qt.factory.id === ftyId) ?? st.quotations[0];
      if (!q || displayRow.isDraft) continue;
      if (q.status !== 'sent_to_factory' && q.status !== 'for_revision') continue;
      touchedQids.push(q.qid);

      this._styles_update(st.style_id, cur => ({
        ...cur,
        quotations: cur.quotations.map(qt => {
          if (qt.qid !== q.qid) return qt;
          return {
            ...qt,
            fob_price: q.status === 'sent_to_factory' ? (row.factoryPrice || qt.fob_price) : qt.fob_price,
            revised_factory_fob: q.status === 'for_revision' ? row.revisedPrice : qt.revised_factory_fob,
            adjustment: q.status === 'for_revision'
              ? this._adjustmentStringForRevisedFactoryFob(st, qt, row.revisedPrice, '')
              : qt.adjustment,
          };
        }),
      }));
    }

    for (const qid of touchedQids) {
      const st = this.styles().find(style => style.quotations.some(q => q.qid === qid));
      const q = st?.quotations.find(qt => qt.qid === qid);
      if (st && q) this._saveQuotationState(st, q, q.fob_price);
    }

    this.factModal.set(false);
    this.toast('Batch quotation prices saved.', 'success');
  }

  batchQuickAction(row: BatchDisplayRow, action: 'submit' | 'recall'): void {
    if (this.role() !== 'factory') return;
    const style = row.style;
    const q = row.q;
    if (!q) return;

    const state = this.batchRow(row.key, row);
    const nextStatus: QuotationStatus = action === 'submit' ? 'factory_submitted' : 'sent_to_factory';

    this._styles_update(style.style_id, st => ({
      ...st,
      quotations: st.quotations.map(qt => {
        if (qt.qid !== q.qid) return qt;
        return {
          ...qt,
          status: nextStatus,
          rejectReason: null,
          fob_price: action === 'submit' && qt.status === 'sent_to_factory' ? (state.factoryPrice || qt.fob_price) : qt.fob_price,
          revised_factory_fob: action === 'submit' ? state.revisedPrice : qt.revised_factory_fob,
          adjustment: action === 'submit' && qt.status === 'for_revision'
            ? this._adjustmentStringForRevisedFactoryFob(style, qt, state.revisedPrice, '')
            : qt.adjustment,
        };
      }),
    }));

    const updated = this.styles().find(st => st.style_id === style.style_id);
    const updatedQ = updated?.quotations.find(qt => qt.qid === q.qid);
    if (!updated || !updatedQ) return;

    this._saveQuotationState(updated, updatedQ, updatedQ.fob_price);
    if (action === 'submit') this._saveCostingState(updated, updatedQ, 'factory_submitted');

    const u = this._userInfo.user;
    const displayName = u
      ? `${u.firstName}${u.lastName ? ' ' + u.lastName : ''}`.trim()
      : `Factory (${updatedQ.factory.name})`;
    this._notifHub.sendNotification(
      action === 'submit' ? 'submission' : 'recall',
      updated.style_id,
      this.selectedPkg(),
      updatedQ.factory.id,
      updatedQ.factory.name,
      u?.username ?? 'unknown',
      displayName,
      'TMS',
      action === 'submit'
        ? `Costing for ${updated.style_id} submitted`
        : `Costing submission recalled for ${updated.style_id}`
    );

    this._seedBatchRows();
    this.toast(action === 'submit' ? 'Quotation submitted.' : 'Quotation recalled.', 'success');
  }

  private _makeBatchQuotation(factory: GqFactory, row?: BatchRowState): GqQuotation {
    const q = makeQuotation(factory);
    const comments = row?.comments?.trim() ?? '';
    return {
      ...q,
      factory_remarks: comments,
      tms_comments: comments,
      fob_price: row?.factoryPrice?.trim() || '',
      revised_factory_fob: row?.revisedPrice?.trim() || '',
    };
  }

  removeFactory(qid: string): void {
    const s = this.selectedStyle();
    const q = s?.quotations.find(qt => qt.qid === qid);
    if (!s || !q) return;

    const hasHistory = q.status !== 'new';
    if (hasHistory) {
      this.pendingRemoveQid.set(qid);
      this.removeFactoryModal.set(true);
      return;
    }

    this._doRemoveFactory(qid);
  }

  confirmRemoveFactory(): void {
    const qid = this.pendingRemoveQid();
    if (!qid) return;
    this.removeFactoryModal.set(false);
    this.pendingRemoveQid.set(null);
    this._doRemoveFactory(qid);
  }

  private _doRemoveFactory(qid: string): void {
    const s = this.selectedStyle();
    if (!s) return;
    this._styles_update(s.style_id, st => ({
      ...st,
      quotations: st.quotations.filter(q => q.qid !== qid),
    }));
    const remaining = this.selectedStyle()?.quotations.length ?? 0;
    if (this.activeQIdx() >= remaining) {
      this.activeQIdx.set(Math.max(0, remaining - 1));
    }
    this._historyCache.delete(qid);
    this._deleteQuotation(qid);
    this.toast('Factory removed.', 'info');
  }

  // ── Reject modal ───────────────────────────────────────────────────────────
  confirmReject(): void {
    const reason = this.rejectReason().trim();
    const qid = this.pendingQid();
    if (!reason || !qid) return;

    const found = this._findStyleAndQuotation(qid);
    if (!found) return;
    const { style: s, q } = found;

    this._mutateQ(qid, qt => ({ ...qt, status: 'rejected', rejectReason: reason }));
    this._saveQuotation(qid);
    this._saveCosting(qid, 'rejected');

    // Send rejection notification to factory
    const u = this._userInfo.user;
    const displayName = u
      ? `${u.firstName}${u.lastName ? ' ' + u.lastName : ''}`.trim()
      : 'Merchandiser';
    this._notifHub.sendNotification(
      'rejection',
      s.style_id,
      this.selectedPkg(),
      q.factory.id,
      q.factory.name,
      u?.username ?? 'unknown',
      displayName,
      `FTY_${q.factory.id}`,
      `Quotation for ${s.style_id} rejected: ${reason}`
    );

    this.rejectModal.set(false);
    this.pendingQid.set(null);
    this.toast('Costing rejected.', 'danger');
  }

  // ── Package dropdown ───────────────────────────────────────────────────────
  togglePkgDropdown(): void {
    const next = !this.pkgDropdownOpen();
    this.pkgDropdownOpen.set(next);
    if (next) this.pkgSearch.set('');
  }

  onPkgSearchInput(event: Event): void {
    this.pkgSearch.set((event.target as HTMLInputElement).value);
  }

  selectPackage(packName: string): void {
    if (packName !== this.selectedPkg()) {
      this.selectedPkg.set(packName);
      this.statusFilter.set('all');
      this.searchQuery.set('');
    }
    this.pkgDropdownOpen.set(false);
    this.pkgSearch.set('');
  }

  // ── Package/filter change ──────────────────────────────────────────────────
  onPackageChange(event: Event): void {
    const pkg = (event.target as HTMLSelectElement).value;
    this.selectedPkg.set(pkg);
    this.statusFilter.set('all');
    this.searchQuery.set('');
  }

  onSearchInput(event: Event): void {
    this.searchQuery.set((event.target as HTMLInputElement).value);
  }

  clearSearch(): void { this.searchQuery.set(''); }

  // ── Toast ──────────────────────────────────────────────────────────────────
  toast(msg: string, type: GqToast['type'] = 'info'): void {
    // LT ODM: app-wide Spartan toaster instead of the module's own toast stack.
    if (type === 'success') sonnerToast.success(msg);
    else if (type === 'danger') sonnerToast.error(msg);
    else sonnerToast.info(msg);
  }

  // ── Template helper methods ────────────────────────────────────────────────
  deriveStatus(style: GqStyle): QuotationStatus { return deriveStatus(style); }
  fmtNum(n: number | string, d = 2): string { return fmtNum(n, d); }

  getStatusCfg(status: string) { return STATUS_CFG[status] ?? STATUS_CFG['new']; }
  statusTone(status: string): Tone { return this.getStatusCfg(status).tone; }

  // Tailwind colour helpers (tms/shared/ui/tones.ts)
  protected readonly toneBadge = toneBadge;
  protected readonly toneDot = toneDot;
  protected readonly toneText = toneText;
  protected readonly toneBorder = toneBorder;
  statusLabelKey(status: string): string {
    return 'gq.status.' + (STATUS_CFG[status] ? status : 'new');
  }
  cmtLabel(item: GqCmtItem): string | null {
    return item.isCustom ? null : 'gq.costing.cmt.' + item.key;
  }

  compareMinIdx(row: CompareRow): number {
    const nonNull = row.values.filter((v): v is number => v !== null && (row.isPercent ? true : v > 0));
    if (nonNull.length < 2) return -1;
    const min = Math.min(...nonNull);
    const max = Math.max(...nonNull);
    if (min === max) return -1;
    return row.values.findIndex(v => v === min);
  }

  compareMaxIdx(row: CompareRow): number {
    const nonNull = row.values.filter((v): v is number => v !== null && (row.isPercent ? true : v > 0));
    if (nonNull.length < 2) return -1;
    const min = Math.min(...nonNull);
    const max = Math.max(...nonNull);
    if (min === max) return -1;
    return row.values.findIndex(v => v === max);
  }

  bomHasFtyEdits(bom: GqBomItem[]): boolean {
    return bom.some(b => (b.factoryEditedFields?.length ?? 0) > 0);
  }

  toggleHistBom(id: number): void {
    this.histBomOpen.update(s => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  bomDiffRows(histBom: GqBomItem[], styleBom: GqBomItem[]): Array<{
    item:    GqBomItem;
    orig:    GqBomItem | undefined;
    changed: string[];
  }> {
    // consump_uom intentionally excluded: factory often doesn't set UOM, so null vs
    // the TMS original would produce false-positive "changed" rows on every item.
    const FIELDS = ['price', 'consump', 'curr_id', 'wastage_pct'] as const;
    const origMap = new Map<string, GqBomItem>();
    styleBom.forEach(b => origMap.set(`${b.material ?? ''}||${b.part_desc}`, b));

    return histBom.map(item => {
      const orig = origMap.get(`${item.material ?? ''}||${item.part_desc}`);
      const changed: string[] = !orig ? ['new'] : FIELDS.filter(f => {
        const normalise = (b: GqBomItem, field: string): string => {
          if (field === 'consump')     return b.consump ?? b.consum ?? '';
          if (field === 'wastage_pct') return String(parseFloat(b.wastage_pct ?? '0') || 0);
          return String((b as unknown as Record<string, unknown>)[field] ?? '');
        };
        return normalise(item, f) !== normalise(orig, f);
      });
      return { item, orig, changed };
    }).filter(r => r.changed.length > 0);
  }

  bomGroups(bom: GqBomItem[]): Array<{ key: string; label: string; items: (GqBomItem & { _idx: number; lineTotal: number })[] }> {
    const groups = new Map<string, (GqBomItem & { _idx: number; lineTotal: number })[]>();
    bom.forEach((b, i) => {
      const key = b.mat_class || 'UNCLASSIFIED';
      const rows = groups.get(key) ?? [];
      rows.push({ ...b, _idx: i, lineTotal: this.actualCost(b) });
      groups.set(key, rows);
    });

    const order = MAT_CLASS_ORDER as readonly string[];
    return Array.from(groups.entries())
      .sort(([a], [b]) => {
        const ai = order.indexOf(a);
        const bi = order.indexOf(b);
        return (ai < 0 ? 999 : ai) - (bi < 0 ? 999 : bi);
      })
      .map(([key, items]) => ({
        key,
        label: MAT_CLASS_LABELS[key]
          ?? key.split('_').map(p => p.charAt(0) + p.slice(1).toLowerCase()).join(' '),
        items,
      }));
  }

  lineTotal(item: GqBomItem, _rate: number): number {
    return this.actualCost(item);
  }

  bomConsum(item: GqBomItem): string {
    return item.consump ?? item.consum ?? '';
  }

  uomDesc(uomId: string): string {
    return this.uomList().find(u => u.uom_id === uomId)?.uom_desc ?? '';
  }

  openUomDropdown(idx: number, event: MouseEvent): void {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const panelHeight = Math.min(window.innerHeight * 0.5, 340);
    const spaceBelow = window.innerHeight - rect.bottom;
    const openUp = spaceBelow < panelHeight + 8 && rect.top > panelHeight + 8;
    this.uomDropdownPos.set({
      // open-down: top edge aligns to bottom of trigger
      // open-up:   bottom edge aligns to top of trigger (CSS handles via translateY(-100%))
      top:  openUp ? rect.top - 2 : rect.bottom + 2,
      left: rect.left,
      openUp,
    });
    this.uomSearch.set('');
    this.uomOpenIdx.set(idx);
  }

  closeUomDropdown(): void {
    this.uomOpenIdx.set(-1);
    this.uomSearch.set('');
  }

  selectUom(bomIdx: number, uomId: string): void {
    const s = this.selectedStyle();
    if (!s) return;
    if (this.canEditBomFty()) {
      const q = this.selQ();
      if (!q) return;
      this._mutateQBom(q.qid, bom => {
        const next = [...bom];
        const item: GqBomItem = { ...next[bomIdx], consump_uom: uomId || null };
        const edited = new Set(item.factoryEditedFields ?? []);
        uomId ? edited.add('consump_uom') : edited.delete('consump_uom');
        item.factoryEditedFields = [...edited];
        next[bomIdx] = item;
        return next;
      });
    } else {
      this._styles_update(s.style_id, st => {
        const bom = [...st.bom];
        bom[bomIdx] = { ...bom[bomIdx], consump_uom: uomId || null };
        return { ...st, bom };
      });
    }
    this.closeUomDropdown();
  }

  onUomSearchInput(event: Event): void {
    this.uomSearch.set((event.target as HTMLInputElement).value);
  }

  bomWastage(item: GqBomItem): string {
    return item.wastage_pct ?? '';
  }

  priceStep(val: string | null | undefined): string {
    const s = String(val ?? '').trim();
    const dot = s.indexOf('.');
    if (dot < 0 || dot === s.length - 1) return '0.01';
    const places = Math.min(s.length - dot - 1, 4);
    return Math.pow(10, -places).toFixed(places);
  }

  actualCost(item: GqBomItem): number {
    const consumRaw = parseFloat(item.consump ?? item.consum ?? '');
    const consump = Number.isFinite(consumRaw) ? consumRaw : 1;
    const price = parseFloat(item.price ?? '0') || 0;
    const wastagePct = parseFloat(item.wastage_pct ?? '0') || 0;
    const base = consump * price;
    return base + (base * wastagePct / 100);
  }

  cmtInRmb(item: GqCmtItem, rate: number): number { return toRmb(item.value, item.curr_id, rate); }

  smvCmp(smv: string, rate: string): number {
    return (parseFloat(smv) || 0) * (parseFloat(rate) || 0);
  }

  cardTotals(style: GqStyle): ReturnType<typeof calcTotals> {
    return calcTotals(style.bom, [], style.rate_in_usd);
  }

  qTotals(style: GqStyle, q: GqQuotation): ReturnType<typeof calcTotals> {
    return calcTotals(q.bom ?? style.bom, q.factoryCosting, effRate(style, q, this.role()));
  }

  bomTotalRmb(style: GqStyle): number {
    const totals = calcTotals(style.bom, [], style.rate_in_usd);
    return totals.fabricTotal + totals.trimTotal + totals.othersTotal;
  }

  ttlProductionRmb(style: GqStyle, q: GqQuotation): number {
    const totals = calcTotals(q.bom ?? style.bom, q.factoryCosting, effRate(style, q, this.role()));
    return totals.fabricTotal + totals.trimTotal + totals.othersTotal + totals.cmtTotal;
  }

  ttlProductionBaseRmb(style: GqStyle, q: GqQuotation): number {
    const totals = calcTotals(q.bom ?? style.bom, q.factoryCosting, effRate(style, q, this.role()));
    return totals.total;
  }

  fobPriceRmb(style: GqStyle, q: GqQuotation): number {
    const ttlRmb = this.ttlProductionRmb(style, q);
    const marginPct = parseFloat(q.gross_margin_pct) || 0;
    return (ttlRmb * (1 + marginPct / 100)) + this.adjustmentRmb(style, q);
  }

  baseFobPriceRmb(style: GqStyle, q: GqQuotation): number {
    const ttlRmb = this.ttlProductionRmb(style, q);
    const marginPct = parseFloat(q.gross_margin_pct) || 0;
    return ttlRmb * (1 + marginPct / 100);
  }

  adjustmentRmb(style: GqStyle, q: GqQuotation): number {
    const usd = this.effectiveAdjustmentUsd(style, q);
    return toRmb(usd, 'USD', effRate(style, q, this.role()));
  }

  effectiveAdjustmentUsd(style: GqStyle, q: GqQuotation): number {
    if (parseFloat(q.revised_factory_fob ?? '') || 0) {
      const suggested = this._suggestedAdjustmentFor(style, q);
      if (suggested !== null) return suggested;
    }
    return parseFloat(q.adjustment ?? '0') || 0;
  }

  suggestedAdjustmentRmb(style: GqStyle, q: GqQuotation): number | null {
    return this._suggestedAdjustmentFor(style, q);
  }

  private _suggestedAdjustmentFor(style: GqStyle, q: GqQuotation): number | null {
    const targetRmb = this.revisedFactoryFobRmb(style, q);
    if (!targetRmb) return null;
    const rate = effRate(style, q, this.role());
    const totals = calcTotals(q.bom ?? style.bom, q.factoryCosting, rate);
    const marginPct = parseFloat(q.gross_margin_pct) || 0;
    const baseFobRmb = totals.total * (1 + marginPct / 100);
    const adjustmentRmb = targetRmb - baseFobRmb;
    return this.usdFromRmb(adjustmentRmb, rate);
  }

  private _adjustmentForRevisedFactoryFob(style: GqStyle, q: GqQuotation, revisedFactoryFob: string): number | null {
    return this._suggestedAdjustmentFor(style, { ...q, revised_factory_fob: revisedFactoryFob });
  }

  private _adjustmentStringForRevisedFactoryFob(style: GqStyle, q: GqQuotation, revisedFactoryFob: string, fallback: string): string {
    const adjustment = this._adjustmentForRevisedFactoryFob(style, q, revisedFactoryFob);
    return adjustment === null ? fallback : fmtNum(adjustment);
  }

  revisedFactoryFobRmb(style: GqStyle, q: GqQuotation): number {
    const raw = parseFloat(q.revised_factory_fob ?? '') || 0;
    if (!raw) return 0;
    return toRmb(raw, 'USD', effRate(style, q, this.role()));
  }

  suggestedAdjustmentUsd(style: GqStyle, q: GqQuotation): number | null {
    return this.suggestedAdjustmentRmb(style, q);
  }

  marginMode(qid: string): 'pct' | 'value' {
    return this.marginEntryMode()[qid] ?? 'pct';
  }

  setMarginMode(qid: string, mode: 'pct' | 'value'): void {
    this.marginEntryMode.update(m => ({ ...m, [qid]: mode }));
  }

  grossMarginValueRmb(style: GqStyle, q: GqQuotation): number {
    return this.ttlProductionBaseRmb(style, q) * ((parseFloat(q.gross_margin_pct) || 0) / 100);
  }

  grossMarginValueTxn(style: GqStyle, q: GqQuotation): number {
    return this.fromRmb(this.grossMarginValueRmb(style, q), 'USD', effRate(style, q, this.role()));
  }

  grossMarginInputValue(style: GqStyle, q: GqQuotation): string {
    if (this.marginMode(q.qid) === 'value') return this.grossMarginValueTxn(style, q) ? fmtNum(this.grossMarginValueTxn(style, q)) : '';
    return q.gross_margin_pct ?? '';
  }

  onGrossMarginChange(style: GqStyle, qid: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    const mode = this.marginMode(qid);
    const q = style.quotations.find(qt => qt.qid === qid);
    if (!q) return;
    if (mode === 'pct') {
      this._mutateQ(qid, qt => ({ ...qt, gross_margin_pct: value }));
      return;
    }
    const base = this.ttlProductionBaseRmb(style, q);
    const rawUsd = parseFloat(value) || 0;
    const rawRmb = toRmb(rawUsd, 'USD', effRate(style, q, this.role()));
    const pct = base > 0 ? (rawRmb / base) * 100 : 0;
    this._mutateQ(qid, qt => ({ ...qt, gross_margin_pct: pct ? fmtNum(pct, 2) : '' }));
  }

  tmsFobRmb(style: GqStyle): number {
    return toRmb(parseFloat(style.tms_fob ?? '0') || 0, 'USD', style.rate_in_usd);
  }

  // Convert an RMB amount back into the style's costing currency (inverse of toRmb).
  fromRmb(rmb: number, curr: string, rate: number): number {
    const r = rate || 6.55;
    if (!rmb) return 0;
    switch (curr) {
      case 'USD': return rmb / r;
      case 'EUR': return rmb / (r * 1.08);
      case 'HKD': return rmb * 7.83 / r;
      default:    return rmb;
    }
  }

  displayCurrency(style: GqStyle): string {
    return style.curr_id ?? 'RMB';
  }

  usdFromRmb(rmb: number, rate: number): number {
    const r = rate || 6.55;
    return rmb ? rmb / r : 0;
  }

  usdFromTxn(amount: string | number, curr: string, rate: number): number {
    return this.usdFromRmb(toRmb(amount, curr, rate), rate);
  }

  moneyUsd(value: number | string): string {
    return `$${fmtNum(value)} USD`;
  }

  usdValueFromRmb(style: GqStyle, q: GqQuotation, rmb: number): number {
    return this.usdFromRmb(rmb, effRate(style, q, this.role()));
  }

  moneyTxn(value: number | string, curr: string): string {
    return `${fmtNum(value)} ${curr}`;
  }

  moneyTxnFromRmb(rmb: number, style: GqStyle, q?: GqQuotation): string {
    const curr = this.displayCurrency(style);
    const rate = q ? effRate(style, q, this.role()) : style.rate_in_usd;
    return this.moneyTxn(this.txnFromRmb(rmb, curr, rate), curr);
  }

  txnFromRmb(rmb: number, curr: string, rate: number): number {
    return this.fromRmb(rmb, curr, rate);
  }

  usdPrimaryFromRmb(rmb: number, curr: string, rate: number): { usd: number; txn: number; curr: string } {
    return {
      usd: this.usdFromRmb(rmb, rate),
      txn: this.txnFromRmb(rmb, curr, rate),
      curr,
    };
  }

  usdPrimaryFromTxn(value: string | number, curr: string, rate: number): { usd: number; txn: number; curr: string } | null {
    const txn = parseFloat(String(value));
    if (!Number.isFinite(txn) || txn === 0) return null;
    return {
      usd: this.usdFromTxn(txn, curr, rate),
      txn,
      curr,
    };
  }

  batchTxnCurrency(row: BatchDisplayRow): string {
    return this.displayCurrency(row.style);
  }

  batchRate(row: BatchDisplayRow): number {
    return effRate(row.style, row.q, this.role());
  }

  batchPriceDisplay(row: BatchDisplayRow, value: string | number): { usd: number; txn: number; curr: string } | null {
    const usd = parseFloat(String(value));
    if (!Number.isFinite(usd) || usd === 0) return null;
    return {
      usd,
      txn: this.txnFromRmb(toRmb(usd, 'USD', this.batchRate(row)), this.batchTxnCurrency(row), this.batchRate(row)),
      curr: this.batchTxnCurrency(row),
    };
  }

  batchTmsPriceDisplay(value: string | number): { usd: number } | null {
    const usd = parseFloat(String(value));
    return Number.isFinite(usd) && usd !== 0 ? { usd } : null;
  }

  fobPriceDisplay(style: GqStyle, q: GqQuotation): { value: number; curr: string; usd: number; txn: number } {
    const rmb = this.fobPriceRmb(style, q);
    const curr = this.displayCurrency(style);
    const display = this.usdPrimaryFromRmb(rmb, curr, effRate(style, q, this.role()));
    return { value: display.txn, curr, usd: display.usd, txn: display.txn };
  }

  baseFobPriceDisplay(style: GqStyle, q: GqQuotation): { value: number; curr: string; usd: number; txn: number } {
    const rmb = this.baseFobPriceRmb(style, q);
    const curr = this.displayCurrency(style);
    const display = this.usdPrimaryFromRmb(rmb, curr, effRate(style, q, this.role()));
    return { value: display.txn, curr, usd: display.usd, txn: display.txn };
  }

  fobMarginPct(style: GqStyle, q: GqQuotation): number | null {
    const tmsFob = this.tmsFobRmb(style);
    if (!tmsFob) return null;
    return ((tmsFob - this.fobPriceRmb(style, q)) / tmsFob) * 100;
  }

  hasFactoryData(q: GqQuotation): boolean {
    return q.status === 'factory_submitted'
      || q.status === 'for_revision'
      || q.status === 'approved'
      || q.status === 'rejected';
  }

  statusFilterCount(key: string): number {
    return key === 'all' ? this.baseStyles().length : (this.statusCounts()[key] || 0);
  }

  getQField(q: GqQuotation, key: string): string {
    return (q as unknown as Record<string, string>)[key] ?? '';
  }

  adjustmentDisplayUsd(style: GqStyle, q: GqQuotation): string {
    if (!(parseFloat(q.revised_factory_fob ?? '') || 0)) return '';
    return fmtNum(this.effectiveAdjustmentUsd(style, q));
  }

  onRemarkTextInput(event: Event): void {
    this.remarkText.set((event.target as HTMLTextAreaElement).value);
  }

  onRejectReasonInput(event: Event): void {
    this.rejectReason.set((event.target as HTMLTextAreaElement).value);
  }

  // TrackBy
  trackByStyleId(_: number, s: GqStyle): string { return s.style_id; }
  trackByPkg(_: number, p: GqPackage): string { return p.pack_name; }
  trackByQid(_: number, q: GqQuotation): string { return q.qid; }
  trackByIdx(i: number): number { return i; }
  trackByKey(_: number, item: GqCmtItem): string { return item.key; }

  // ── Private DB helpers ─────────────────────────────────────────────────────
  private _fetchRemarks(style: GqStyle): void {
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spLoadRemarks,
      Parameters: { style_id: style.style_id, customer_id: style.customer_id, season_id: style.season_id },
    });
  }

  private _saveRemark(qid: string, rid: string, text: string, factory: GqFactory): void {
    const s = this.selectedStyle();
    if (!s) return;
    this._saveRemarkForStyle(s, qid, rid, text, factory);
  }

  private _saveRemarkForStyle(s: GqStyle, qid: string, rid: string, text: string, factory: GqFactory): void {
    const u = this._userInfo.user;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spSaveRemark,
      Parameters: {
        rid,
        qid,
        style_id: s.style_id,
        customer_id: s.customer_id,
        season_id: s.season_id,
        factory_id: factory.id,
        role: this.role(),
        username: u?.username ?? 'anonymous',
        display_name: u ? `${u.firstName}${u.lastName ? ' ' + u.lastName : ''}`.trim() : this.role(),
        remark_text: text,
      },
    });
  }

  deleteRemark(qid: string, rid: string): void {
    this._mutateQ(qid, q => ({ ...q, remarks: q.remarks.filter(r => r.rid !== rid) }));
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spDeleteRemark,
      Parameters: { rid },
    });
    this.toast('Message deleted.', 'info');
  }

  private _deleteQuotation(qid: string): void {
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spDeleteQuotation,
      Parameters: { qid },
    });
  }

  private _approveCosting(qid: string): void {
    const found = this._findStyleAndQuotation(qid);
    if (!found) return;
    const { style: s, q } = found;
    const u = this._userInfo.user;
    const totals = calcTotals(q.bom ?? s.bom, q.factoryCosting, s.rate_in_usd);
    const ftyPrice = (totals.total * (1 + (parseFloat(q.gross_margin_pct) || 0) / 100)) + this.adjustmentRmb(s, q);
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spApproveCosting,
      Parameters: {
        customer:     s.customer_id,
        style:        s.style_id,
        season:       s.season_id,
        factory:      q.factory.id,
        fty_price:    ftyPrice,
        fty_curr:     s.curr_id,
        total_fab:    totals.fabricTotal,
        total_trims:  totals.trimTotal,
        total_others: totals.othersTotal,
        total_cmt:    totals.cmtTotal,
        tms_price:    parseFloat(s.tms_fob ?? '0') || 0,
        tms_curr:     s.costing_curr ?? 'USD',
        rate:         s.rate_in_usd,
        userid:       u?.username ?? 'anonymous',
      },
    });
  }

  private _resolveFobPrice(s: GqStyle, q: GqQuotation): string {
    // FOB is a computed-only field in the UI — q.fob_price reflects the
    // last DB value, not user input. Always recompute from live costing
    // so saves stay in sync with what the merchandiser/factory sees.
    const rate = effRate(s, q, this.role());
    if (!rate) return '';
    const usd = this.fobPriceRmb(s, q) / rate;
    return Number.isFinite(usd) && usd > 0 ? usd.toFixed(2) : '';
  }

  private _saveCosting(qid: string, status: QuotationStatus): void {
    const found = this._findStyleAndQuotation(qid);
    if (!found) return;
    this._saveCostingState(found.style, found.q, status);
  }

  private _saveCostingState(s: GqStyle, q: GqQuotation, status: QuotationStatus): void {
    const u = this._userInfo.user;
    this._historyCache.delete(q.qid); // invalidate so next load fetches fresh data
    const fobPrice = this._resolveFobPrice(s, q);
    const modernParams: Record<string, unknown> = {
      qid: q.qid,
      pack_name: this.selectedPkg(),
      style_id: s.style_id,
      customer_id: s.customer_id,
      season_id: s.season_id,
      factory_id: q.factory.id,
      factory_name: q.factory.name,
      status,
      changed_by: u?.username ?? 'anonymous',
      bom_json: JSON.stringify(q.bom ?? s.bom),
      cmt_json: JSON.stringify(q.factoryCosting),
      factory_remarks: q.factory_remarks,
      smv: q.smv,
      smv_rate: q.smv_rate,
      fc_qty: q.fc_qty,
      fob_price: fobPrice,
      gross_margin_pct: q.gross_margin_pct,
      adjustment: fmtNum(this.effectiveAdjustmentUsd(s, q)),
      price_type: q.price_type,
      rate_in_usd: s.rate_in_usd,
      fty_rate_usd: q.fty_rate_usd ?? null,
    };

    // Legacy server compatibility: older web_rd_gq_save_costing signatures.
    const legacyParams: Record<string, unknown> = {
      qid: q.qid,
      style_id: s.style_id,
      customer_id: s.customer_id,
      season_id: s.season_id,
      factory_id: q.factory.id,
      status,
      changed_by: u?.username ?? 'anonymous',
      bom_json: JSON.stringify(q.bom ?? s.bom),
      cmt_json: JSON.stringify(q.factoryCosting),
      smv: q.smv,
      smv_rate: q.smv_rate,
      fc_qty: q.fc_qty,
      fob_price: fobPrice,
      gross_margin_pct: q.gross_margin_pct,
      price_type: q.price_type,
    };

    this._pendingSaveCostingQueue.push({
      modern: modernParams,
      legacy: legacyParams,
      retriedLegacy: false,
    });

    this._spTrace('invoke:saveCosting', {
      proc: this._spSaveCosting,
      qid: q.qid,
      status,
      modernKeys: Object.keys(modernParams),
      legacyKeys: Object.keys(legacyParams),
    });

    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spSaveCosting,
      Parameters: modernParams,
    });
  }

  private _loadHistory(qid: string): void {
    if (!qid) return;
    const cached = this._historyCache.get(qid);
    if (cached) { this.costingHistory.set(cached); return; }
    this.isLoadingHistory.set(true);
    this.costingHistory.set([]);
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spCostingHistory,
      Parameters: { qid },
    });
  }

  private _loadQuotations(pack_name: string): void {
    if (!pack_name) return;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spLoadQuotations,
      Parameters: { pack_name },
    });
  }

  private _loadStylePricing(pack_name: string): void {
    if (!pack_name) return;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spLoadStylePricing,
      Parameters: { pack_name },
    });
  }

  private _loadBomDraft(pack_name: string): void {
    if (!pack_name) return;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spLoadBomDraft,
      Parameters: { pack_name },
    });
  }

  private _saveBomDraftToDb(s: GqStyle): void {
    const u = this._userInfo.user;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spSaveBomDraft,
      Parameters: {
        style_id:    s.style_id,
        customer_id: s.customer_id,
        season_id:   s.season_id,
        pack_name:   this.selectedPkg(),
        bom_json:    JSON.stringify(s.bom),
        updated_by:  u?.username ?? 'anonymous',
      },
    });
  }

  private _patchStyleDisplayFields(byStyle: Map<string, GqQuotation[]>): void {
    const allStyles = this.styles();
    byStyle.forEach((_, styleId) => {
      const s = allStyles.find(st => st.style_id === styleId);
      if (!s || !s.style_image) return;
      this._sp.invoke('ExecuteStoredProcFlat', {
        SpName: this._spUpdStyleDisplay,
        Parameters: {
          style_id:    s.style_id,
          model_name:  s.model_name,
          style_image: s.style_image,
          rate_in_usd: s.rate_in_usd || null,
        },
      });
    });
  }

  private _saveStylePricing(styleId: string): void {
    const s = this.styles().find(st => st.style_id === styleId);
    if (!s) return;
    const u = this._userInfo.user;
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spSaveStylePricing,
      Parameters: {
        pack_name:    this.selectedPkg(),
        style_id:     s.style_id,
        customer_id:  s.customer_id,
        season_id:    s.season_id,
        tms_fob:      s.tms_fob ?? '',
        costing_curr: s.costing_curr ?? 'USD',
        updated_by:   u?.username ?? 'anonymous',
      },
    });
  }

  private _saveQuotation(qid: string): void {
    const found = this._findStyleAndQuotation(qid);
    if (!found) return;
    this._saveQuotationState(found.style, found.q);
  }

  private _saveQuotationState(s: GqStyle, q: GqQuotation, fobPriceOverride?: string): void {
    const u = this._userInfo.user;
    const fobPrice = fobPriceOverride ?? this._resolveFobPrice(s, q);
    const modernParams: Record<string, unknown> = {
      qid: q.qid,
      pack_name: this.selectedPkg(),
      style_id: s.style_id,
      customer_id: s.customer_id,
      season_id: s.season_id,
      factory_id: q.factory.id,
      factory_name: q.factory.name,
      status: q.status,
      bom_json: JSON.stringify(q.bom ?? s.bom),
      cmt_json: JSON.stringify(q.factoryCosting),
      smv: q.smv,
      smv_rate: q.smv_rate,
      factory_remarks: q.factory_remarks,
      fc_qty: q.fc_qty,
      ttl_production: q.ttl_production,
      gross_margin_pct: q.gross_margin_pct,
      adjustment: fmtNum(this.effectiveAdjustmentUsd(s, q)),
      fob_price: fobPrice,
      revised_factory_fob: q.revised_factory_fob,
      tms_comments: q.tms_comments,
      price_type: q.price_type,
      reject_reason: q.rejectReason ?? '',
      sent_at: q.sentAt,
      updated_by: u?.username ?? 'anonymous',
      // FOB locked to TMS rate at approval — fty_rate_usd is informational only.
      fty_rate_usd: q.fty_rate_usd ?? null,
      // style display fields — stored so approved-products SP can serve them
      model_name:  s.model_name  ?? null,
      style_image: s.style_image ?? null,
      rate_in_usd: s.rate_in_usd || null,
    };

    // Legacy server compatibility: pre-migration save_quotation signature.
    const legacyParams: Record<string, unknown> = {
      qid: q.qid,
      pack_name: this.selectedPkg(),
      style_id: s.style_id,
      customer_id: s.customer_id,
      season_id: s.season_id,
      factory_id: q.factory.id,
      factory_name: q.factory.name,
      status: q.status,
      bom_json: JSON.stringify(q.bom ?? s.bom),
      cmt_json: JSON.stringify(q.factoryCosting),
      smv: q.smv,
      smv_rate: q.smv_rate,
      factory_remarks: q.factory_remarks,
      fc_qty: q.fc_qty,
      ttl_production: q.ttl_production,
      gross_margin_pct: q.gross_margin_pct,
      fob_price: fobPrice,
      price_type: q.price_type,
      reject_reason: q.rejectReason ?? '',
      sent_at: q.sentAt,
      updated_by: u?.username ?? 'anonymous',
      fty_rate_usd: q.fty_rate_usd ?? null,
    };

    this._pendingSaveQuotationQueue.push({
      modern: modernParams,
      legacy: legacyParams,
      retriedLegacy: false,
    });

    this._spTrace('invoke:saveQuotation', {
      proc: this._spSaveQuotation,
      qid: q.qid,
      status: q.status,
      modernKeys: Object.keys(modernParams),
      legacyKeys: Object.keys(legacyParams),
    });

    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spSaveQuotation,
      Parameters: modernParams,
    });
  }

  private _spTrace(event: string, payload: Record<string, unknown> = {}): void {
    if (!this._spDebugEnabled) return;
    const seq = ++this._spDebugSeq;
    console.info('GQ_SP_TRACE', {
      tag: 'SPDBG',
      seq,
      event,
      at: new Date().toISOString(),
      selectedPkg: this.selectedPkg(),
      selectedStyleId: this.selectedStyle()?.style_id ?? null,
      ...payload,
    });
  }

  private _pickRowsWithSource(data: Record<string, any[]>, preferred = 'table0'): { source: string; rows: any[] } {
    const preferredRows = data[preferred];
    if (Array.isArray(preferredRows) && preferredRows.length > 0) {
      return { source: preferred, rows: preferredRows };
    }

    const tableKeys = Object.keys(data)
      .filter(k => k.startsWith('table'))
      .sort((a, b) => {
        const ai = parseInt(a.replace('table', ''), 10);
        const bi = parseInt(b.replace('table', ''), 10);
        return (Number.isNaN(ai) ? 999 : ai) - (Number.isNaN(bi) ? 999 : bi);
      });

    for (const key of tableKeys) {
      const rows = data[key];
      if (Array.isArray(rows) && rows.length > 0) {
        return { source: key, rows };
      }
    }

    return { source: preferred, rows: Array.isArray(preferredRows) ? preferredRows : [] };
  }

  private _parseBomJson(raw: string): GqBomItem[] {
    try {
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return parsed.map((r: any): GqBomItem => ({
        mat_class:   r.mat_class ?? null,
        material:    r.material    != null ? String(r.material)    : null,
        part_desc:   r.part_desc   ?? '',
        consump:     r.consump     != null ? String(r.consump)     : (r.consum != null ? String(r.consum) : null),
        consump_uom: r.consump_uom != null ? String(r.consump_uom) : null,
        wastage_pct: r.wastage_pct != null ? String(r.wastage_pct) : '0',
        curr_id:     r.curr_id ?? 'RMB',
        price:       r.price       != null ? String(r.price)       : null,
        isCustom:        !!r.isCustom,
        addedByFactory:  r.addedByFactory ? true : undefined,
        factoryEditedFields: Array.isArray(r.factoryEditedFields) ? r.factoryEditedFields : undefined,
      }));
    } catch {
      return [];
    }
  }

  private _buildExcelHtml(style: GqStyle, q: GqQuotation, totals: GqTotals, imageSrc: string | null): string {
    const exportRate = effRate(style, q, this.role());
    const txnCurr = this.displayCurrency(style);
    const toUsd = (rmb: number): string => exportRate > 0 ? `$${fmtNum(rmb / exportRate)}` : '-';
    const toTxn = (rmb: number): string => fmtNum(this.txnFromRmb(rmb, txnCurr, exportRate));
    const adjustedFobRmb = this._calcFobPriceRmb(totals.total, q.gross_margin_pct)
      + toRmb(parseFloat(q.adjustment ?? '') || 0, 'USD', exportRate);

    const printBom = q.bom ?? style.bom;
    const bomRows = printBom.map((b, idx) => {
      const actualCost = this._bomActualCost(b);
      const actualCostTxn = actualCost === '' ? '' : toTxn(actualCost as number);
      const actualCostUsd = actualCost === '' ? '' : toUsd(actualCost as number);
      return `
        <tr>
          <td>${idx + 1}</td>
          <td>${this._escapeHtml(b.mat_class ?? '')}</td>
          <td>${this._escapeHtml(b.material ?? '')}</td>
          <td>${this._escapeHtml(b.part_desc)}</td>
          <td class="num">${this._escapeHtml(b.consump ?? '')}</td>
          <td>${this._escapeHtml(b.consump_uom ?? '')}</td>
          <td class="num">${this._escapeHtml(b.wastage_pct ?? '0')}</td>
          <td>${this._escapeHtml(b.curr_id)}</td>
          <td class="num">${this._escapeHtml(b.price ?? '')}</td>
          <td class="num">${actualCostTxn}</td>
          <td class="num usd">${actualCostUsd}</td>
        </tr>`;
    }).join('');

    const cmtRows = q.factoryCosting.map((c) => {
      const rmb = toRmb(c.value, c.curr_id, exportRate);
      return `
        <tr>
          <td>${this._escapeHtml(c.label)}</td>
          <td>${this._escapeHtml(c.description)}</td>
          <td class="num">${this._escapeHtml(c.value)}</td>
          <td>${this._escapeHtml(c.curr_id)}</td>
          <td class="num">${toTxn(rmb)}</td>
          <td class="num usd">${toUsd(rmb)}</td>
        </tr>`;
    }).join('');

    const metaRows = [
      ['Factory', q.factory.name],
      ['Factory ID', q.factory.id],
      ['Quotation Status', q.status],
      ['Sent At', q.sentAt || '-'],
      ['Package', this.selectedPkg()],
      ['Transaction Currency', txnCurr],
      ['Exchange Rate', `1 USD = ${fmtNum(exportRate, 4)} ${txnCurr}`],
      ['Gross Margin %', q.gross_margin_pct || '0'],
      [`FOB Price (${txnCurr})`, `${toTxn(adjustedFobRmb)} ${txnCurr}`],
    ].map(([label, value]) => `<tr><th>${this._escapeHtml(label)}</th><td>${this._escapeHtml(value)}</td></tr>`).join('');

    const imageCell = imageSrc
      ? `<img src="${imageSrc}" alt="${this._escapeHtml(style.model_name)}" class="style-image" />`
      : `<div class="image-fallback">${this._escapeHtml(style.prodcat_id || 'No Image')}</div>`;

    return `<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    body { font-family: 'DM Sans', Arial, sans-serif; color: #111827; }
    .sheet { padding: 18px; }
    .title { font-size: 20px; font-weight: 700; color: #1e3a5f; margin-bottom: 4px; }
    .subtitle { font-size: 12px; color: #6b7280; margin-bottom: 16px; }
    .section { margin-top: 18px; }
    .section h2 { font-size: 13px; color: #2563eb; margin: 0 0 8px; text-transform: uppercase; letter-spacing: .06em; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border: 1px solid #e5e7eb; padding: 6px 8px; font-size: 11px; vertical-align: top; }
    th { background: #f8fafc; text-align: left; color: #374151; }
    .meta th { width: 180px; }
    .style-grid { display: flex; gap: 16px; align-items: flex-start; }
    .style-details { flex: 1; min-width: 0; }
    .image-wrap { width: 180px; min-width: 180px; }
    .style-image { width: 180px; height: 180px; object-fit: cover; border: 1px solid #e5e7eb; }
    .image-fallback { width: 180px; height: 180px; display: flex; align-items: center; justify-content: center; border: 1px solid #e5e7eb; background: #f8fafc; color: #9ca3af; font-size: 11px; }
    .num { text-align: right; }
    .usd { color: #059669; }
    .totals td { font-weight: 700; }
    .note { font-size: 11px; color: #6b7280; margin-top: 6px; }
  </style>
</head>
<body>
  <div class="sheet">
    <div class="title">Garment Quotation Export</div>
    <div class="subtitle">Selected factory snapshot only</div>

    <div class="section">
      <h2>Style Summary</h2>
      <div class="style-grid">
        <div class="image-wrap">${imageCell}</div>
        <div class="style-details">
          <table class="meta">
            <tbody>
              <tr><th>Style ID</th><td>${this._escapeHtml(style.style_id)}</td></tr>
              <tr><th>Model Name</th><td>${this._escapeHtml(style.model_name)}</td></tr>
              <tr><th>Description</th><td>${this._escapeHtml(style.composition)}</td></tr>
              <tr><th>Customer</th><td>${this._escapeHtml(style.customer_id)}</td></tr>
              <tr><th>Season</th><td>${this._escapeHtml(style.season_id)}</td></tr>
              <tr><th>Category</th><td>${this._escapeHtml(style.prodcat_id)}</td></tr>
              <tr><th>Package</th><td>${this._escapeHtml(this.selectedPkg())}</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="section">
      <h2>Selected Factory</h2>
      <table class="meta"><tbody>${metaRows}</tbody></table>
    </div>

    <div class="section">
      <h2>BOM Items</h2>
      <table>
        <thead>
          <tr>
            <th>#</th>
            <th>Mat Class</th>
            <th>Material</th>
            <th>Description</th>
            <th class="num">Consumption</th>
            <th>UOM</th>
            <th class="num">Wastage %</th>
            <th>Currency</th>
            <th class="num">Unit Price</th>
            <th class="num">Actual Cost (${this._escapeHtml(txnCurr)})</th>
            <th class="num usd">Actual Cost (USD)</th>
          </tr>
        </thead>
        <tbody>${bomRows || '<tr><td colspan="11">No BOM items</td></tr>'}</tbody>
      </table>
    </div>

    <div class="section">
      <h2>Factory Costing</h2>
      <table>
        <thead>
          <tr>
            <th>Label</th>
            <th>Description</th>
            <th class="num">Value</th>
            <th>Currency</th>
            <th class="num">${this._escapeHtml(txnCurr)} Equivalent</th>
            <th class="num usd">USD Equivalent</th>
          </tr>
        </thead>
        <tbody>${cmtRows || '<tr><td colspan="6">No costing items</td></tr>'}</tbody>
      </table>
    </div>

    <div class="section">
      <h2>Totals</h2>
      <table class="totals">
        <thead>
          <tr><th></th><th class="num">${this._escapeHtml(txnCurr)}</th><th class="num usd">USD</th></tr>
        </thead>
        <tbody>
          <tr><th>Fabric Total</th><td class="num">${toTxn(totals.fabricTotal)}</td><td class="num usd">${toUsd(totals.fabricTotal)}</td></tr>
          <tr><th>Trim Total</th><td class="num">${toTxn(totals.trimTotal)}</td><td class="num usd">${toUsd(totals.trimTotal)}</td></tr>
          <tr><th>Others Total</th><td class="num">${toTxn(totals.othersTotal)}</td><td class="num usd">${toUsd(totals.othersTotal)}</td></tr>
          <tr><th>CMT Total</th><td class="num">${toTxn(totals.cmtTotal)}</td><td class="num usd">${toUsd(totals.cmtTotal)}</td></tr>
          <tr><th>TTL Production</th><td class="num">${toTxn(totals.total)}</td><td class="num usd">${toUsd(totals.total)}</td></tr>
          <tr><th>Gross Margin %</th><td class="num" colspan="2">${this._escapeHtml(q.gross_margin_pct || '0')}%</td></tr>
          <tr><th>FOB Price</th><td class="num">${toTxn(adjustedFobRmb)} ${this._escapeHtml(txnCurr)}</td><td class="num usd">${toUsd(adjustedFobRmb)}</td></tr>
        </tbody>
      </table>
      <div class="note">Exported from Garment Quotation. Exchange rate: 1 USD = ${fmtNum(exportRate, 4)} ${this._escapeHtml(txnCurr)}</div>
    </div>
  </div>
</body>
</html>`;
  }

  private _bomActualCost(item: GqBomItem): number | '' {
    const consump = Number(item.consump ?? item.consum ?? 0);
    const price = Number(item.price ?? 0);
    if (!Number.isFinite(consump) || !Number.isFinite(price)) return '';
    const wastage = Number(item.wastage_pct ?? 0);
    const factor = Number.isFinite(wastage) ? 1 + wastage / 100 : 1;
    return consump * price * factor;
  }

  private async _imageToDataUrl(_styleId: string, src: string): Promise<string | null> {
    const imageCandidates: Array<HTMLImageElement | null> = [
      document.querySelector<HTMLImageElement>('.gq-print__img'),
      document.querySelector<HTMLImageElement>('.gq-drawer__content img.gq-card__img'),
    ];
    for (const img of imageCandidates) {
      if (!img || !img.complete || !img.naturalWidth) continue;
      const dataUrl = this._canvasImageToDataUrl(img);
      if (dataUrl) return dataUrl;
    }

    if (!src) return null;
    if (src.startsWith('data:')) return src;
    try {
      const response = await fetch(src, { credentials: 'include' });
      if (!response.ok) return null;
      const blob = await response.blob();
      return await new Promise<string | null>((resolve) => {
        const reader = new FileReader();
        reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : null);
        reader.onerror = () => resolve(null);
        reader.readAsDataURL(blob);
      });
    } catch {
      return null;
    }
  }

  private _canvasImageToDataUrl(img: HTMLImageElement): string | null {
    try {
      const canvas = document.createElement('canvas');
      canvas.width = img.naturalWidth;
      canvas.height = img.naturalHeight;
      const ctx = canvas.getContext('2d');
      if (!ctx) return null;
      ctx.drawImage(img, 0, 0);
      return canvas.toDataURL('image/png');
    } catch {
      return null;
    }
  }

  private _downloadBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  private _safeFileName(value: string): string {
    return (value || 'export').replace(/[\\/:*?"<>|]+/g, '_').trim() || 'export';
  }

  private _calcFobPriceRmb(ttlProductionRmb: number, grossMarginPct: string): number {
    const margin = Number(grossMarginPct ?? 0);
    const factor = Number.isFinite(margin) ? 1 + margin / 100 : 1;
    return ttlProductionRmb * factor;
  }

  private _escapeHtml(value: any): string {
    return String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  // ── Private helpers ────────────────────────────────────────────────────────
  private _styles_update(styleId: string, fn: (s: GqStyle) => GqStyle): void {
    this.styles.update(list =>
      list.map(s => s.style_id === styleId ? fn({ ...s }) : s)
    );
    this.selectedStyle.update(s =>
      s?.style_id === styleId ? fn({ ...s }) : s
    );
  }

  private _findStyleAndQuotation(qid: string): { style: GqStyle; q: GqQuotation } | null {
    for (const style of this.styles()) {
      const q = style.quotations.find(qt => qt.qid === qid);
      if (q) return { style, q };
    }
    const selected = this.selectedStyle();
    const selectedQ = selected?.quotations.find(qt => qt.qid === qid);
    return selected && selectedQ ? { style: selected, q: selectedQ } : null;
  }

  private _mutateQ(qid: string, fn: (q: GqQuotation) => GqQuotation): void {
    const found = this._findStyleAndQuotation(qid);
    if (!found) return;
    this._styles_update(found.style.style_id, st => ({
      ...st,
      quotations: st.quotations.map(q => q.qid === qid ? fn({ ...q }) : q),
    }));
  }

  private _mergeToJson(response: any): Record<string, any[]> {
    const result: Record<string, any[]> = {};
    const mapTable = (table: any, idx: number) => {
      const key = `table${idx}`;
      if (!result[key]) result[key] = [];
      result[key].push(...table.rows.map((row: any[]) => {
        const obj: any = {};
        for (let i = 0; i < table.columns.length; i++) obj[table.columns[i]] = row[i];
        return obj;
      }));
    };
    if (Array.isArray(response.data)) {
      response.data.forEach((exec: any) => {
        if (exec.status !== 'Success' || !Array.isArray(exec.data)) return;
        exec.data.forEach(mapTable);
      });
    } else {
      const exec = response.data;
      if (exec?.status === 'Success' && Array.isArray(exec.data)) exec.data.forEach(mapTable);
    }
    return result;
  }
}
