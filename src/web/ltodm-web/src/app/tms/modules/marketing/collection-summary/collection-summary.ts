import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalculator, lucideFileDown, lucideImage, lucidePackageOpen } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { StoredProcSignalRService } from '@services/spHub.service';
import { FlatResult, procName, spTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { ConceptPicker } from '@tms/shared/ui/concept-picker';
import { ConceptStudioResult, parseList, resolveImageUrl } from '../concept-studio/concept-studio.model';

interface ProposalHeader {
  collectionRecid: number;
  conceptRecid: number;
  conceptName: string;
  customer: string;
  season: string;
  targetMarket: string;
  targetFob: number;
  conceptBrief: string;
  fabricDirection: string[];
  sustainabilityNotes: string[];
  activeTags: string[];
}

interface ProposalItem {
  itemRecid: number;
  name: string;
  styleCode: string;
  category: string;
  origin: string;
  country: string;
  fob: number;
  imageUrl: string;
}

const SP = {
  CONCEPTS: 'web_rd_get_concept_studio_results',
  HEADER: 'web_rd_get_collection_builder',
  ITEMS: 'web_rd_get_collection_builder_items',
} as const;

/** Section anchors of the proposal (TMS had tabs for sections that never existed or never had data). */
const SECTIONS = ['concept', 'lineup', 'costs', 'offer'] as const;

/**
 * Customer Proposal (TMS collection-summary): a printable proposal for a concept's collection, built from the
 * collection header (a copy of the concept) and its lineup. /collection/:id, where id is the concept recid (as in TMS).
 *
 * Not ported: the Supplier Recommendation, Production Timeline and supplier-metrics sections. TMS never loaded data for
 * them (the screen showed empty sections and hard-coded zeros such as "Audit $0.00, Capacity 0%").
 */
@Component({
  selector: 'app-collection-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, TranslocoPipe, NgIcon, ConceptPicker, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCalculator, lucideFileDown, lucideImage, lucidePackageOpen })],
  templateUrl: './collection-summary.html',
  styleUrls: ['./collection-summary.print.scss'],
})
export class CollectionSummary implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly zone = inject(NgZone);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly sections = SECTIONS;
  protected readonly today = new Date();

  readonly concepts = signal<ConceptStudioResult[]>([]);
  readonly conceptsLoaded = signal(false);
  readonly conceptRecid = signal<number | null>(null);
  readonly header = signal<ProposalHeader | null>(null);
  readonly items = signal<ProposalItem[]>([]);
  readonly loading = signal(false);
  /** The concept has no collection yet. */
  readonly noCollection = signal(false);

  readonly meta = computed(() => {
    const h = this.header();
    return h ? [h.customer, h.season, h.targetMarket].filter(Boolean).join(' · ') : '';
  });
  readonly targetFob = computed(() => this.header()?.targetFob ?? 0);
  readonly costed = computed(() => this.items().filter((i) => i.fob > 0));
  readonly avgFob = computed(() => {
    const c = this.costed();
    return c.length ? c.reduce((s, i) => s + i.fob, 0) / c.length : 0;
  });
  readonly fobGap = computed(() => (this.avgFob() && this.targetFob() ? this.avgFob() - this.targetFob() : 0));
  readonly withinTarget = computed(() => this.costed().filter((i) => this.targetFob() && i.fob <= this.targetFob()).length);
  /** Bars are scaled to the larger of the target and the highest FOB (TMS scaled to the target and overflowed). */
  readonly barMax = computed(() => Math.max(this.targetFob(), ...this.items().map((i) => i.fob), 0.01));
  readonly previews = computed(() => this.items().filter((i) => i.imageUrl).slice(0, 3));

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));
    // The team's concepts (every concept for Admins and factory users); the API decides from the sign-in token.
    this.call(SP.CONCEPTS, {});
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('id')) || null;
      if (id === this.conceptRecid()) return;
      this.conceptRecid.set(id);
      this.header.set(null);
      this.items.set([]);
      this.noCollection.set(false);
      if (id) {
        this.loading.set(true);
        this.call(SP.HEADER, { concept_recid: id });
      }
    });
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  private call(proc: string, parameters?: Record<string, unknown>): void {
    void this.sp.invoke('ExecuteStoredProcFlat', parameters ? { SpName: proc, Parameters: parameters } : { SpName: proc });
  }

  selectConcept(recid: number): void {
    void this.router.navigate(['/collection', recid]);
  }

  scrollTo(section: string): void {
    document.getElementById(`proposal-${section}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  print(): void {
    window.print();
  }

  private onResult(res: FlatResult): void {
    const rows = spTables(res)['table0'] ?? [];
    switch (procName(res.procedure)) {
      case SP.CONCEPTS:
        this.concepts.set(rows as ConceptStudioResult[]);
        this.conceptsLoaded.set(true);
        break;
      case SP.HEADER: {
        const r = rows[0];
        if (!r) {
          this.loading.set(false);
          this.noCollection.set(true);
          break;
        }
        if (Number(r['conceptRecid']) !== this.conceptRecid()) break; // late answer for a concept already left
        this.header.set({
          collectionRecid: Number(r['collectionRecid']),
          conceptRecid: Number(r['conceptRecid']),
          conceptName: String(r['conceptName'] ?? ''),
          customer: String(r['customer'] ?? ''),
          season: String(r['season'] ?? ''),
          targetMarket: String(r['targetMarket'] ?? ''),
          targetFob: Number(r['targetFob']) || 0,
          conceptBrief: String(r['conceptBrief'] ?? ''),
          fabricDirection: parseList(r['fabricDirection']),
          sustainabilityNotes: parseList(r['sustainabilityNotes']),
          activeTags: parseList(r['activeTags']),
        });
        this.call(SP.ITEMS, { collection_recid: Number(r['collectionRecid']) });
        break;
      }
      case SP.ITEMS:
        if (rows.length && this.header() && Number(rows[0]['collectionRecid'] ?? this.header()!.collectionRecid) !== this.header()!.collectionRecid) break;
        this.items.set(
          rows.map((r) => ({
            itemRecid: Number(r['itemRecid']),
            name: String(r['productName'] ?? ''),
            styleCode: String(r['styleCode'] ?? ''),
            category: String(r['category'] ?? ''),
            origin: String(r['sbu'] || r['country'] || ''),
            country: String(r['country'] ?? ''),
            fob: Number(r['fobPrice']) || 0,
            imageUrl: resolveImageUrl(r['imageUrl']),
          })),
        );
        this.loading.set(false);
        break;
    }
  }
}
