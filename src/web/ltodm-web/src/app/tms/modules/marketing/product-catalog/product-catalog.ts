import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideSend, lucideSparkles } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { StoredProcSignalRService } from '@services/spHub.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { FlatResult, procName, spError, spTables, SpTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { ConceptPicker } from '@tms/shared/ui/concept-picker';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { ConceptStudioResult, parseList, resolveImageUrl } from '../concept-studio/concept-studio.model';
import { GqBomItem, GqCmtItem, calcTotals } from '../garment-quotation/garment-quotation.model';
import { CostBreakdownItem, SBU_SUBMISSION_SP, STATUS_TONE, SbuSubmission, SubmissionStatus, costItems } from '../manage-offering/sbu-submission.model';

type SortOption = 'match' | 'newest' | 'oldest' | 'price-asc' | 'price-desc';

interface CatalogProduct {
  id: string;
  recid?: number;
  source: 'sbu' | 'gq';
  name: string;
  styleCode: string;
  category: string;
  sbu: string;
  country: string;
  fobPrice: number;
  imageUrl: string;
  costs: CostBreakdownItem[];
  /** JSON snapshot recorded with an offer */
  costBreakdown: string;
  createdAt: string;
}

interface ScoredProduct extends CatalogProduct {
  score: number;
  reasons: string[];
  /** Status of this product's latest offer for the selected concept (null = not offered yet). */
  offerStatus: SubmissionStatus | null;
}

const SP = {
  CONCEPTS: 'web_rd_get_concept_studio_results',
  PRODUCTS: 'web_rd_get_sbu_products',
  GQ_APPROVED: 'web_rd_get_gq_approved_products',
  SUBMISSIONS: SBU_SUBMISSION_SP.READ,
  INSERT: SBU_SUBMISSION_SP.INSERT,
} as const;

const words = (text: string) => text.toLowerCase();
const containsAny = (product: string, concept: string, keywords: string[]) => keywords.some((k) => product.includes(k) && concept.includes(k));

/**
 * Product Matching (TMS product-catalog): SBU products and approved garment quotations ranked against a saved concept.
 * The match score uses the same keyword and FOB rules as TMS (it is not an AI model). "Submit offer" records a real
 * SBU submission for the concept (TMS only changed the card's label for a moment and saved nothing).
 */
@Component({
  selector: 'app-product-catalog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, TranslocoPipe, NgIcon, ConceptPicker, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmNativeSelectImports,
    HlmSkeletonImports, HlmSpinnerImports,
  ],
  providers: [provideIcons({ lucideImage, lucideSend, lucideSparkles })],
  templateUrl: './product-catalog.html',
})
export class ProductCatalog implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly zone = inject(NgZone);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDlg = inject(ConfirmDialogService);

  protected readonly toneBadge = toneBadge;
  protected readonly sortOptions: SortOption[] = ['match', 'newest', 'oldest', 'price-asc', 'price-desc'];
  protected readonly statusOptions = ['all', 'not-offered', 'Submitted', 'For Review', 'Factory Submitted', 'Accepted', 'Rejected'];

  readonly concepts = signal<ConceptStudioResult[]>([]);
  readonly conceptsLoaded = signal(false);
  readonly selectedConceptRecid = signal<number | null>(null);
  readonly selectedConcept = computed(() => this.concepts().find((c) => c.recid === this.selectedConceptRecid()) ?? null);

  readonly products = signal<CatalogProduct[]>([]);
  readonly productsPending = signal(0);
  readonly submissions = signal<SbuSubmission[]>([]);

  readonly category = signal('All');
  readonly sortBy = signal<SortOption>('match');
  readonly statusFilter = signal('all');
  readonly submittingId = signal<string | null>(null);

  readonly categories = computed(() => ['All', ...[...new Set(this.products().map((p) => p.category))].sort((a, b) => a.localeCompare(b))]);

  readonly scored = computed<ScoredProduct[]>(() => {
    const concept = this.selectedConcept();
    const subs = this.submissions();
    return this.products().map((p) => ({ ...p, ...this.score(p, concept), offerStatus: this.offerStatus(p, subs) }));
  });

  readonly visible = computed(() => {
    let list = this.scored();
    if (this.category() !== 'All') list = list.filter((p) => p.category === this.category());
    const status = this.statusFilter();
    if (status === 'not-offered') list = list.filter((p) => !p.offerStatus);
    else if (status !== 'all') list = list.filter((p) => p.offerStatus === status);
    const sorted = [...list];
    switch (this.sortBy()) {
      case 'match':
        sorted.sort((a, b) => b.score - a.score || a.name.localeCompare(b.name));
        break;
      case 'newest':
        sorted.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
        break;
      case 'oldest':
        sorted.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
        break;
      case 'price-asc':
        sorted.sort((a, b) => a.fobPrice - b.fobPrice);
        break;
      case 'price-desc':
        sorted.sort((a, b) => b.fobPrice - a.fobPrice);
        break;
    }
    return sorted;
  });

  readonly offeredCount = computed(() => this.scored().filter((p) => p.offerStatus).length);
  readonly acceptedCount = computed(() => this.scored().filter((p) => p.offerStatus === 'Accepted').length);

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));
    this.call(SP.CONCEPTS, {});
    // Factory users get only their own factory's products (filter set by the API from the sign-in token).
    this.productsPending.set(2);
    this.call(SP.PRODUCTS);
    this.call(SP.GQ_APPROVED);
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  private call(proc: string, parameters?: Record<string, unknown>): void {
    void this.sp.invoke('ExecuteStoredProcFlat', parameters ? { SpName: proc, Parameters: parameters } : { SpName: proc });
  }

  selectConcept(recid: number): void {
    this.selectedConceptRecid.set(recid);
    this.submissions.set([]);
    this.call(SP.SUBMISSIONS, { concept_recid: recid });
  }

  private onResult(res: FlatResult): void {
    const data = spTables(res);
    switch (procName(res.procedure)) {
      case SP.CONCEPTS: {
        const rows = (data['table0'] ?? []) as ConceptStudioResult[];
        this.concepts.set(rows);
        this.conceptsLoaded.set(true);
        if (rows.length && this.selectedConceptRecid() === null) this.selectConcept(rows[0].recid);
        break;
      }
      case SP.PRODUCTS:
        this.merge('sbu', this.sbuRows(data));
        break;
      case SP.GQ_APPROVED:
        this.merge('gq', this.gqRows(data));
        break;
      case SP.SUBMISSIONS: {
        const rows = (data['table0'] ?? []) as SbuSubmission[];
        if (!rows.length || rows[0].conceptRecid === this.selectedConceptRecid()) this.submissions.set(rows);
        break;
      }
      case SP.INSERT: {
        this.submittingId.set(null);
        const error = spError(res);
        const status = data['table0']?.[0]?.['result_status'];
        if (!error && status === 'Success') {
          toast.success(this.transloco.translate('catalog.offerSubmitted'));
          const recid = this.selectedConceptRecid();
          if (recid !== null) this.call(SP.SUBMISSIONS, { concept_recid: recid });
        } else {
          toast.error(this.transloco.translate('mo.notifications.saveFailed', { reason: error ?? status ?? 'unknown error' }));
        }
        break;
      }
    }
  }

  private merge(source: 'sbu' | 'gq', rows: CatalogProduct[]): void {
    this.products.update((current) => [...current.filter((p) => p.source !== source), ...rows]);
    this.productsPending.update((n) => Math.max(0, n - 1));
  }

  private sbuRows(data: SpTables): CatalogProduct[] {
    return (data['table0'] ?? []).map((r) => ({
      id: String(r['id'] ?? r['recid']),
      recid: r['recid'],
      source: 'sbu' as const,
      name: r['name'] ?? '',
      styleCode: r['styleCode'] ?? '',
      category: normalizeCategory(r['category']),
      sbu: r['sbu'] || 'Unknown SBU',
      country: r['country'] ?? '',
      fobPrice: Number(r['fobPrice']) || 0,
      imageUrl: resolveImageUrl(r['image']),
      costs: costItems(r['costBreakdown']),
      costBreakdown: r['costBreakdown'] ?? '[]',
      createdAt: r['createdDate'] ?? '',
    }));
  }

  private gqRows(data: SpTables): CatalogProduct[] {
    const json = <T>(v: unknown): T => {
      try {
        return (typeof v === 'string' && v ? JSON.parse(v) : []) as T;
      } catch {
        return [] as T;
      }
    };
    const round = (n: number) => +n.toFixed(2);
    return (data['table0'] ?? []).map((r) => {
      const t = calcTotals(json<GqBomItem[]>(r['bom_json']), json<GqCmtItem[]>(r['cmt_json']), parseFloat(r['rate_in_usd']) || 6.55);
      const quoted = parseFloat(r['fob_price']) || 0;
      const fob = quoted > 0 ? quoted : round(t.totalUSD);
      // Same snapshot as SBU Submission records for a quotation offer.
      const breakdown: CostBreakdownItem[] = [
        { item: 'Fabric', amount: round(t.fabricTotalUSD) },
        { item: 'Trims', amount: round(t.trimTotalUSD) },
        { item: 'Other Cost', amount: round(t.othersTotalUSD) },
        { item: 'Fty FOB', amount: round(t.cmtTotalUSD) },
        { item: 'FTY Margin', amount: round(fob - t.totalUSD) },
      ];
      return {
        id: `gq_${r['qid']}`,
        source: 'gq' as const,
        name: r['model_name'] ?? r['style_id'] ?? '',
        styleCode: r['style_id'] ?? '',
        category: normalizeCategory(r['category']),
        sbu: r['factory_name'] || 'GQ Factory',
        country: r['country'] ?? '',
        fobPrice: fob,
        imageUrl: resolveImageUrl(r['style_image']),
        costs: breakdown.slice(0, 4).filter((c) => c.amount > 0),
        costBreakdown: JSON.stringify(breakdown),
        createdAt: r['approved_at'] ?? '',
      };
    });
  }

  /** Latest offer for this product in the selected concept: catalog products by recid, quotations by style code. */
  private offerStatus(p: CatalogProduct, subs: SbuSubmission[]): SubmissionStatus | null {
    const match = subs.find((s) => (p.source === 'sbu' ? s.productRecid === p.recid : !s.productRecid && s.styleCode === p.styleCode));
    return match?.status ?? null;
  }

  /** TMS keyword + FOB rules, unchanged (0-100). */
  private score(p: CatalogProduct, c: ConceptStudioResult | null): { score: number; reasons: string[] } {
    if (!c) return { score: 0, reasons: [] };
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(`catalog.reason.${key}`, params);
    const reasons: string[] = [];
    let score = 0;
    const target = parseFloat(c.fob_price || '0') || 0;
    const conceptText = words([c.concept_name, c.customer, c.season, c.target_market, c.concept_brief].join(' '));
    const suggested = words(parseList(c.suggested_products).join(' '));
    const tags = words(parseList(c.active_tags).join(' '));
    const fabrics = words(parseList(c.fabric_direction).join(' '));
    const notes = words(parseList(c.sustainability_notes).join(' '));
    const productText = words([p.name, p.category, p.sbu, p.costs.map((x) => x.item).join(' ')].join(' '));
    const direction = `${suggested} ${conceptText}`;

    if (
      containsAny(direction, words(p.name), ['dress', 'jacket', 'bag', 'hoodie', 'shirt', 'short', 'pant', 'tote']) ||
      containsAny(direction, words(p.category), ['dress', 'jacket', 'bag', 'bottom', 'top', 'outerwear'])
    ) {
      score += 35;
      reasons.push(t('direction', { category: p.category.toLowerCase() }));
    }
    if (target > 0) {
      const gap = Math.abs(p.fobPrice - target) / target;
      if (gap <= 0.1) {
        score += 25;
        reasons.push(t('fobClose'));
      } else if (gap <= 0.2) {
        score += 15;
        reasons.push(t('fobWorkable'));
      } else if (gap <= 0.35) {
        score += 8;
      }
    }
    if (containsAny(productText, `${tags} ${notes}`, ['sustainable', 'recycled', 'organic', 'eco'])) {
      score += 15;
      reasons.push(t('sustainability'));
    }
    if (containsAny(productText, `${fabrics} ${conceptText}`, ['denim', 'canvas', 'stretch', 'utility', 'performance'])) {
      score += 15;
      reasons.push(t('material'));
    }
    if (containsAny(productText, conceptText, ['bag', 'jacket', 'dress', 'bottom', 'top', 'outerwear'])) score += 10;
    if (containsAny(productText, conceptText, ['gen z', 'urban', 'street', 'active', 'utility'])) {
      score += 10;
      reasons.push(t('market'));
    }
    return { score: Math.min(100, Math.round(score)), reasons: reasons.slice(0, 3) };
  }

  scoreTone(score: number): Tone {
    return score >= 60 ? 'green' : score >= 35 ? 'amber' : 'neutral';
  }

  statusTone(status: SubmissionStatus): Tone {
    return STATUS_TONE[status] ?? 'neutral';
  }

  /** Records an SBU submission (status Submitted) for the selected concept, with the product snapshot. */
  async submitOffer(p: ScoredProduct): Promise<void> {
    const concept = this.selectedConcept();
    if (!concept || this.submittingId()) return;
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('catalog.submitConfirmTitle'),
      message: this.transloco.translate('catalog.submitConfirm', { product: p.name, concept: concept.concept_name }),
      confirmLabel: this.transloco.translate('mo.submitOffer'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'info',
    });
    if (!ok) return;
    const gq = p.source === 'gq';
    this.submittingId.set(p.id);
    this.call(SP.INSERT, {
      concept_recid: concept.recid,
      concept_name: concept.concept_name ?? '',
      product_recid: gq ? null : (p.recid ?? null),
      target_fob: parseFloat(concept.fob_price) || null,
      notes: null,
      status: 'Submitted',
      ...(gq
        ? {
            snap_product_name: p.name,
            snap_style_code: p.styleCode,
            snap_sbu: p.sbu,
            snap_image_url: p.imageUrl,
            snap_fob_price: p.fobPrice,
            snap_cost_breakdown: p.costBreakdown,
          }
        : {}),
    });
  }

  resetFilters(): void {
    this.category.set('All');
    this.statusFilter.set('all');
  }
}

function normalizeCategory(category: unknown): string {
  const value = String(category ?? '').trim();
  if (!value) return 'Uncategorized';
  return value === 'Outwear' ? 'Outerwear' : value;
}
