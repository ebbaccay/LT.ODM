import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalculator, lucideCheck, lucideImage, lucideLayers, lucidePlus, lucideSearch, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { StoredProcSignalRService } from '@services/spHub.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { FlatResult, procName, spError, spTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { ConceptPicker } from '@tms/shared/ui/concept-picker';
import { Modal } from '@tms/shared/ui/modal';
import { ConceptStudioResult, parseList, resolveImageUrl } from '../concept-studio/concept-studio.model';
import { GqBomItem, GqCmtItem, calcTotals } from '../garment-quotation/garment-quotation.model';

/** Collection header (web_rd_get_collection_builder): a copy of the concept taken when the collection was opened. */
interface CollectionHeader {
  collectionRecid: number;
  conceptRecid: number;
  conceptName: string;
  customer: string;
  season: string;
  targetMarket: string;
  targetFob: number;
  activeTags: string;
  conceptBrief: string;
  fabricDirection: string;
  sustainabilityNotes: string;
}

interface CollectionItem {
  itemRecid: number;
  submissionRecid: number;
  productName: string;
  styleCode: string;
  category: string;
  sbu: string;
  country: string;
  imageUrl: string;
  fobPrice: number;
  gqQid: string;
}

/** An approved offer (SBU submission) or approved garment quotation that can be added to the lineup. */
interface PoolItem {
  key: string;
  sourceType: 'manual' | 'gq';
  submissionRecid: number;
  gqQid: string;
  productName: string;
  styleCode: string;
  category: string;
  sbu: string;
  country: string;
  imageUrl: string;
  fobPrice: number;
  costBreakdown: string;
}

export type LineupCategory = 'Top' | 'Bottom' | 'Outerwear' | 'Accessories';
export const LINEUP_CATEGORIES: LineupCategory[] = ['Top', 'Bottom', 'Outerwear', 'Accessories'];

const SP = {
  CONCEPTS: 'web_rd_get_concept_studio_results',
  OPEN: 'web_rd_ins_collection_builder',
  HEADER: 'web_rd_get_collection_builder',
  ITEMS: 'web_rd_get_collection_builder_items',
  POOL: 'web_rd_get_collection_builder_approved_pool',
  ADD: 'web_rd_ins_collection_builder_item',
  REMOVE: 'web_rd_del_collection_builder_item',
} as const;

/** Same keyword rules as TMS: category first, then the product name. */
export function lineupCategory(category: string, productName: string): LineupCategory {
  const c = (category ?? '').toLowerCase();
  if (/outer|jacket|coat|blazer/.test(c)) return 'Outerwear';
  if (/bottom|pants|short|trouser|skirt|dress/.test(c)) return 'Bottom';
  if (/accessor|bag/.test(c)) return 'Accessories';
  if (/top|shirt|blouse|knit/.test(c)) return 'Top';
  const p = (productName ?? '').toLowerCase();
  if (/jacket|hoodie|coat|parka|blazer/.test(p)) return 'Outerwear';
  if (/short|pants|jogger|trouser|skirt|dress/.test(p)) return 'Bottom';
  if (/bag|belt|cap|accessor/.test(p)) return 'Accessories';
  return 'Top';
}

/**
 * Collection Builder (ported from TMS): one collection per concept, built from approved SBU offers and approved
 * garment quotations. Opening a concept creates its collection the first time (web_rd_ins_collection_builder).
 * The database checks that an added product is approved and belongs to the collection's concept.
 */
@Component({
  selector: 'app-collection-builder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, TranslocoPipe, NgIcon, Modal, ConceptPicker, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports,
    HlmSkeletonImports, HlmSpinnerImports,
  ],
  providers: [provideIcons({ lucideCalculator, lucideCheck, lucideImage, lucideLayers, lucidePlus, lucideSearch, lucideX })],
  templateUrl: './collection-builder.html',
})
export class CollectionBuilder implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly zone = inject(NgZone);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDlg = inject(ConfirmDialogService);

  protected readonly categories = LINEUP_CATEGORIES;
  protected readonly parseList = parseList;

  readonly concepts = signal<ConceptStudioResult[]>([]);
  readonly conceptsLoaded = signal(false);
  readonly selectedConceptRecid = signal<number | null>(null);
  readonly selectedConcept = computed(() => this.concepts().find((c) => c.recid === this.selectedConceptRecid()) ?? null);

  readonly header = signal<CollectionHeader | null>(null);
  readonly items = signal<CollectionItem[]>([]);
  readonly pool = signal<PoolItem[]>([]);
  /** Opening the collection, loading its items or the approved pool. */
  readonly syncing = signal(false);
  readonly poolLoading = signal(false);

  readonly activeCategory = signal<LineupCategory | 'All'>('All');
  readonly poolOpen = signal(false);
  readonly poolSearch = signal('');
  readonly addingKey = signal<string | null>(null);
  readonly removingRecid = signal<number | null>(null);

  readonly lineup = computed(() => this.items().map((i) => ({ ...i, lineupCategory: lineupCategory(i.category, i.productName) })));
  readonly visibleLineup = computed(() => {
    const cat = this.activeCategory();
    return cat === 'All' ? this.lineup() : this.lineup().filter((i) => i.lineupCategory === cat);
  });
  readonly categoryCounts = computed(() => {
    const counts: Record<string, number> = { All: this.lineup().length };
    for (const c of this.categories) counts[c] = this.lineup().filter((i) => i.lineupCategory === c).length;
    return counts;
  });

  readonly avgFob = computed(() => {
    const prices = this.items().map((i) => Number(i.fobPrice)).filter((n) => Number.isFinite(n) && n > 0);
    return prices.length ? prices.reduce((a, b) => a + b, 0) / prices.length : 0;
  });
  readonly targetFob = computed(() => this.header()?.targetFob ?? (parseFloat(this.selectedConcept()?.fob_price ?? '') || 0));
  readonly previews = computed(() => this.items().map((i) => i.imageUrl).filter(Boolean).slice(0, 4));

  readonly filteredPool = computed(() => {
    const q = this.poolSearch().toLowerCase().trim();
    return q
      ? this.pool().filter((p) => [p.productName, p.styleCode, p.category, p.sbu].some((v) => (v ?? '').toLowerCase().includes(q)))
      : this.pool();
  });

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));
    // The team's concepts (every concept for Admins and factory users); the API decides from the sign-in token.
    this.call(SP.CONCEPTS, {});
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  private call(proc: string, parameters?: Record<string, unknown>): void {
    void this.sp.invoke('ExecuteStoredProcFlat', parameters ? { SpName: proc, Parameters: parameters } : { SpName: proc });
  }

  // ── Loading ────────────────────────────────────────────────

  selectConcept(recid: number): void {
    this.selectedConceptRecid.set(recid);
    this.header.set(null);
    this.items.set([]);
    this.pool.set([]);
    this.activeCategory.set('All');
    this.syncing.set(true);
    // Creates the concept's collection the first time; returns the existing one afterwards.
    this.call(SP.OPEN, { concept_recid: recid });
  }

  private fetchItems(): void {
    const id = this.header()?.collectionRecid;
    if (!id) return;
    this.call(SP.ITEMS, { collection_recid: id });
  }

  private fetchPool(): void {
    const id = this.header()?.collectionRecid;
    const conceptRecid = this.selectedConceptRecid();
    if (!id || conceptRecid === null) return;
    this.poolLoading.set(true);
    this.call(SP.POOL, { collection_recid: id, concept_recid: conceptRecid });
  }

  private onResult(res: FlatResult): void {
    const proc = procName(res.procedure);
    const error = spError(res);
    const data = spTables(res);
    const row = data['table0']?.[0];
    const status = row?.['result_status'];
    const ok = !error && (status === undefined || status === 'Success');
    const reason = error ?? (status ? String(status) : '');

    switch (proc) {
      case SP.CONCEPTS: {
        const rows = (data['table0'] ?? []) as ConceptStudioResult[];
        this.concepts.set(rows);
        this.conceptsLoaded.set(true);
        if (rows.length && this.selectedConceptRecid() === null) this.selectConcept(rows[0].recid);
        break;
      }
      case SP.OPEN:
        if (ok) {
          this.call(SP.HEADER, { concept_recid: this.selectedConceptRecid() });
        } else {
          this.syncing.set(false);
          toast.error(reason || this.transloco.translate('cb.notifications.collectionFailed'));
        }
        break;
      case SP.HEADER: {
        const h = (data['table0']?.[0] as CollectionHeader | undefined) ?? null;
        // Ignore a late answer for a concept the user has already left.
        if (h && h.conceptRecid !== this.selectedConceptRecid()) break;
        this.header.set(h ? { ...h, targetFob: Number(h.targetFob) || 0 } : null);
        if (h) {
          this.fetchItems();
          this.fetchPool();
        } else {
          this.syncing.set(false);
        }
        break;
      }
      case SP.ITEMS:
        this.items.set(
          ((data['table0'] ?? []) as CollectionItem[]).map((i) => ({ ...i, imageUrl: resolveImageUrl(i.imageUrl), fobPrice: Number(i.fobPrice) || 0 })),
        );
        this.syncing.set(false);
        break;
      case SP.POOL:
        this.pool.set((data['table0'] ?? []).map((r) => this.poolItem(r)));
        this.poolLoading.set(false);
        break;
      case SP.ADD:
        this.addingKey.set(null);
        if (ok) {
          toast.success(this.transloco.translate('cb.notifications.addSuccess'));
          this.fetchItems();
          this.fetchPool();
        } else {
          toast.error(reason || this.transloco.translate('cb.notifications.addFailed'));
        }
        break;
      case SP.REMOVE:
        this.removingRecid.set(null);
        if (ok) {
          toast.success(this.transloco.translate('cb.notifications.removeSuccess'));
          this.fetchItems();
          this.fetchPool();
        } else {
          toast.error(reason || this.transloco.translate('cb.notifications.removeFailed'));
        }
        break;
    }
  }

  /** Approved quotations come with BOM + CMT; FOB and cost lines are derived like the GQ screen (as in TMS). */
  private poolItem(r: Record<string, any>): PoolItem {
    const gq = r['sourceType'] === 'gq';
    let fobPrice = parseFloat(r['fobPrice']) || 0;
    let costBreakdown: string = r['costBreakdown'] ?? '[]';
    if (gq) {
      const json = <T>(v: unknown): T => {
        try {
          return (typeof v === 'string' && v ? JSON.parse(v) : []) as T;
        } catch {
          return [] as T;
        }
      };
      const t = calcTotals(json<GqBomItem[]>(r['bomJson']), json<GqCmtItem[]>(r['cmtJson']), parseFloat(r['rateInUsd']) || 6.55);
      const round = (n: number) => +n.toFixed(2);
      fobPrice = fobPrice > 0 ? fobPrice : round(t.totalUSD);
      costBreakdown = JSON.stringify([
        { item: 'Fabric', amount: round(t.fabricTotalUSD) },
        { item: 'Trims', amount: round(t.trimTotalUSD) },
        { item: 'Other Cost', amount: round(t.othersTotalUSD) },
        { item: 'Fty FOB', amount: round(t.cmtTotalUSD) },
        { item: 'FTY Margin', amount: round(fobPrice - t.totalUSD) },
      ]);
    }
    const gqQid = r['gqQid'] ?? '';
    return {
      key: gq ? `gq_${gqQid}` : `sub_${r['submissionRecid']}`,
      sourceType: gq ? 'gq' : 'manual',
      submissionRecid: r['submissionRecid'] ?? 0,
      gqQid,
      productName: r['productName'] ?? '',
      styleCode: r['styleCode'] ?? '',
      category: r['category'] ?? '',
      sbu: r['sbu'] ?? '',
      country: r['country'] ?? '',
      imageUrl: resolveImageUrl(r['imageUrl']),
      fobPrice,
      costBreakdown,
    };
  }

  // ── Actions ────────────────────────────────────────────────

  openPool(): void {
    if (!this.header()) return;
    this.poolSearch.set('');
    this.poolOpen.set(true);
    this.fetchPool();
  }

  add(item: PoolItem): void {
    const id = this.header()?.collectionRecid;
    if (!id || this.addingKey()) return;
    this.addingKey.set(item.key);
    // @added_by is set by the API from the sign-in token.
    this.call(SP.ADD, {
      collection_recid: id,
      submission_recid: item.sourceType === 'manual' ? item.submissionRecid : 0,
      gq_qid: item.sourceType === 'gq' ? item.gqQid : null,
      cost_breakdown: item.sourceType === 'gq' ? item.costBreakdown : null,
    });
  }

  async remove(item: CollectionItem): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('cb.removeProductConfirmTitle'),
      message: this.transloco.translate('cb.removeProductConfirmNamed', { name: item.productName }),
      confirmLabel: this.transloco.translate('actions.remove'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.removingRecid.set(item.itemRecid);
    this.call(SP.REMOVE, { item_recid: item.itemRecid });
  }

  /** Opens Cost Optimization for this collection (TMS "Generate Cost Summary"). */
  costSummary(): void {
    const id = this.header()?.collectionRecid;
    if (id) void this.router.navigate(['/cost-optimization'], { queryParams: { collectionRecid: id } });
  }

  fobClass(price: number): string {
    const target = this.targetFob();
    if (!target || !price) return '';
    return price > target ? 'text-red-600 dark:text-red-400' : 'text-emerald-600 dark:text-emerald-400';
  }
}
