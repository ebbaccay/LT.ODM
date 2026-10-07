import { DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideCheck,
  lucideCircleAlert,
  lucideClock,
  lucideImage,
  lucidePin,
  lucideSearch,
  lucideSend,
  lucideSparkles,
  lucideUndo2,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { environment } from '@env/environment';
import { StoredProcSignalRService } from '@services/spHub.service';
import { FlatResult, procName, spError, spTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { ConceptPicker } from '@tms/shared/ui/concept-picker';
import { Modal } from '@tms/shared/ui/modal';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { problemMessage } from '../../../../core/auth/auth.service';
import { ConceptStudioResult, parseList, resolveImageUrl } from '../concept-studio/concept-studio.model';
import {
  CO_SP as SP,
  COST_LABELS,
  CollectionHeader,
  CostLabel,
  CostSession,
  CostStyle,
  Suggestion,
  SuggestionIdea,
  costLines,
  resultOk,
  resultText,
} from './cost-optimization.model';

interface Factory {
  id: string;
  name: string;
}

const FAVORITES_KEY = 'ltodm.co.favoriteFactories';

/**
 * Cost Optimization (TMS product-cost-optimization): pick a style from a concept's collection, get AI savings ideas,
 * choose which to apply, and send the cost plan to a factory for review (which creates a "For Review" offer the factory
 * answers in SBU Submission).
 *
 * Changes from TMS:
 * - No sample data: TMS started from a built-in "Eco Denim Joggers" example and fell back to its costs, which could be
 *   saved into a real session. Here every number comes from the style or its saved session.
 * - Approved quotations' CMT ("Fty FOB") is counted as labour (TMS dropped it).
 * - AI ideas come from POST /api/v1/cost-optimization/suggestions; saving them to the session is done in order
 *   (clear, then save, then reload) instead of parallel calls that could overwrite each other.
 * - "Send for review" sends the ideas you selected (TMS's "Implement" first selected all of them).
 * - "Accept adjustments" only showed a message in TMS and changed nothing; it is not ported.
 */
@Component({
  selector: 'app-cost-optimization',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, TranslocoPipe, NgIcon, Modal, ConceptPicker, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports,
    HlmInputImports, HlmLabelImports, HlmSkeletonImports, HlmSpinnerImports,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft, lucideCheck, lucideCircleAlert, lucideClock, lucideImage, lucidePin, lucideSearch, lucideSend, lucideSparkles, lucideUndo2,
    }),
  ],
  templateUrl: './cost-optimization.html',
})
export class CostOptimization implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly http = inject(HttpClient);
  private readonly zone = inject(NgZone);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);

  protected readonly toneBadge = toneBadge;
  protected readonly labels = COST_LABELS;

  // ── Picking ────────────────────────────────────────────────
  readonly concepts = signal<ConceptStudioResult[]>([]);
  readonly conceptsLoaded = signal(false);
  readonly header = signal<CollectionHeader | null>(null);
  readonly selectedConceptRecid = signal<number | null>(null);
  readonly styles = signal<CostStyle[]>([]);
  readonly stylesLoading = signal(false);
  /** A concept was chosen but has no collection yet. */
  readonly noCollection = signal(false);
  readonly styleSearch = signal('');
  readonly filteredStyles = computed(() => {
    const q = this.styleSearch().toLowerCase().trim();
    return q ? this.styles().filter((s) => [s.styleName, s.styleId, s.factoryName].some((v) => v.toLowerCase().includes(q))) : this.styles();
  });

  /** From the URL: collectionRecid (from Collection Builder) and qid (the style being optimized). */
  private collectionRecid: number | null = null;
  readonly qid = signal<string | null>(null);
  readonly style = computed(() => this.styles().find((s) => s.qid === this.qid()) ?? null);

  // ── Optimizing ─────────────────────────────────────────────
  readonly session = signal<CostSession | null>(null);
  readonly sessionLoading = signal(false);
  readonly costs = signal<Record<CostLabel, number> | null>(null);
  readonly currentFob = signal(0);
  readonly targetFob = computed(() => this.header()?.targetFob ?? 0);
  readonly suggestions = signal<Suggestion[]>([]);
  readonly generating = signal(false);
  /** Saving AI ideas to the session (clear -> save -> reload). */
  readonly persisting = signal(false);
  private pendingIdeas: SuggestionIdea[] = [];
  readonly recalling = signal(false);

  readonly isDraft = computed(() => this.session()?.status === 'draft');
  readonly appliedSavings = computed(() => +this.suggestions().filter((s) => s.applied).reduce((sum, s) => sum + s.saving, 0).toFixed(2));
  readonly projectedFob = computed(() => +(this.currentFob() - this.appliedSavings()).toFixed(2));
  readonly reductionPct = computed(() => (this.currentFob() > 0 ? +((this.appliedSavings() / this.currentFob()) * 100).toFixed(1) : 0));
  readonly gapToTarget = computed(() => +(this.currentFob() - this.targetFob()).toFixed(2));
  readonly projectedGap = computed(() => +(this.projectedFob() - this.targetFob()).toFixed(2));

  /** Simulator lines: current vs after the selected ideas. Margin is unchanged (TMS showed the saving as extra margin). */
  readonly lines = computed(() => {
    const c = this.costs();
    if (!c) return [];
    const applied = this.suggestions().filter((s) => s.applied);
    const max = Math.max(...COST_LABELS.map((l) => c[l]), 0.01);
    return COST_LABELS.map((label) => {
      const saving = applied.filter((s) => s.category === label).reduce((sum, s) => sum + s.saving, 0);
      const optimized = +Math.max(0, c[label] - saving).toFixed(2);
      return { label, current: c[label], optimized, currentPct: (c[label] / max) * 100, optimizedPct: (optimized / max) * 100 };
    });
  });

  // ── Review dialog ──────────────────────────────────────────
  readonly reviewOpen = signal(false);
  readonly factories = signal<Factory[]>([]);
  readonly factorySearch = signal('');
  readonly chosenFactory = signal<Factory | null>(null);
  readonly reviewNotes = signal('');
  readonly submitting = signal(false);
  readonly favorites = signal<Set<string>>(loadFavorites());
  readonly visibleFactories = computed(() => {
    const q = this.factorySearch().toLowerCase().trim();
    const favs = this.favorites();
    const list = q ? this.factories().filter((f) => f.name.toLowerCase().includes(q) || f.id.toLowerCase().includes(q)) : this.factories();
    return [...list.filter((f) => favs.has(f.id)), ...list.filter((f) => !favs.has(f.id))].slice(0, 60);
  });
  readonly factoryMatches = computed(() => {
    const q = this.factorySearch().toLowerCase().trim();
    return q ? this.factories().filter((f) => f.name.toLowerCase().includes(q) || f.id.toLowerCase().includes(q)).length : this.factories().length;
  });

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));
    // The team's concepts (every concept for Admins); the API decides from the sign-in token.
    // A collection opened from Collection Builder loads by id, so it may belong to another team.
    this.call(SP.CONCEPTS, {});

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('collectionRecid')) || null;
      const qid = params.get('qid');
      if (id !== this.collectionRecid) {
        this.collectionRecid = id;
        if (id) this.loadCollection(id);
      }
      if (qid !== this.qid()) {
        this.qid.set(qid);
        this.resetOptimizer();
        if (qid && !this.stylesLoading()) this.openSession();
      }
    });
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  private call(proc: string, parameters?: Record<string, unknown> | Record<string, unknown>[]): void {
    void this.sp.invoke('ExecuteStoredProcFlat', parameters ? { SpName: proc, Parameters: parameters } : { SpName: proc });
  }

  // ── Picking ────────────────────────────────────────────────

  selectConcept(recid: number): void {
    this.selectedConceptRecid.set(recid);
    this.noCollection.set(false);
    this.styles.set([]);
    this.stylesLoading.set(true);
    this.call(SP.HEADER, { concept_recid: recid });
  }

  private loadCollection(collectionRecid: number): void {
    this.stylesLoading.set(true);
    this.call(SP.ITEMS, { collection_recid: collectionRecid });
  }

  openStyle(s: CostStyle): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { qid: s.qid }, queryParamsHandling: 'merge' });
  }

  backToStyles(): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { qid: null }, queryParamsHandling: 'merge' });
  }

  styleStatus(s: CostStyle): { key: string; tone: Tone } | null {
    switch (s.sessionStatus) {
      case 'under_review':
        return { key: 'co.step2.statusUnderReview', tone: 'amber' };
      case 'accepted':
        return { key: 'co.step2.statusApproved', tone: 'green' };
      case 'rejected':
        return { key: 'co.step2.statusRejected', tone: 'red' };
    }
    return s.hasSession ? { key: 'co.step2.statusInProgress', tone: 'primary' } : null;
  }

  gap(s: CostStyle): number {
    return +(s.currentFob - this.targetFob()).toFixed(2);
  }

  // ── Session ────────────────────────────────────────────────

  private resetOptimizer(): void {
    this.session.set(null);
    this.costs.set(null);
    this.currentFob.set(0);
    this.suggestions.set([]);
    this.generating.set(false);
    this.persisting.set(false);
    this.reviewOpen.set(false);
  }

  /** Opens (or creates) the style's cost session. Costs come from the collection snapshot; a saved session's costs win. */
  private openSession(): void {
    const qid = this.qid();
    if (!qid) return;
    const s = this.style();
    let breakdown: { item: string; amount: number }[] = [];
    try {
      breakdown = JSON.parse(s?.costBreakdown || '[]');
    } catch {
      breakdown = [];
    }
    const fob = s?.currentFob ?? 0;
    const lines = s ? costLines(Array.isArray(breakdown) ? breakdown : [], fob) : null;
    this.currentFob.set(fob);
    this.costs.set(lines);
    this.sessionLoading.set(true);
    const concept = this.concepts().find((c) => c.recid === this.header()?.conceptRecid);
    // created_by, location and user_group are filled by the API from the sign-in token.
    this.call(SP.LOAD_SESSION, {
      gq_qid: qid,
      style_id: s?.styleId ?? null,
      season_id: concept?.season_id || null,
      style_name: s?.styleName ?? null,
      target_fob: this.targetFob() || null,
      current_fob: fob || null,
      fabric_cost: lines?.Fabric ?? null,
      trim_cost: lines?.Trim ?? null,
      labor_cost: lines?.Labor ?? null,
      overhead_cost: lines?.Overhead ?? null,
      margin_cost: lines?.Margin ?? null,
    });
  }

  private applySession(row: Record<string, any>): void {
    this.sessionLoading.set(false);
    const id = Number(row['sessionId']) || 0;
    if (!id) {
      toast.error(resultText(row) || this.transloco.translate('co.sessionFailed'));
      return;
    }
    const review = String(row['latestReviewStatus'] ?? '') as CostSession['reviewStatus'];
    this.session.set({
      id,
      status: String(row['sessionStatus'] ?? 'draft'),
      reviewStatus: review,
      sentToName: String(row['latestSentToName'] ?? ''),
      reviewerComment: String(row['latestReviewerComment'] ?? ''),
    });
    // A saved session keeps the costs it was started with.
    if (Number(row['currentFob']) > 0) {
      this.currentFob.set(Number(row['currentFob']));
      this.costs.set({
        Fabric: Number(row['fabricCost']) || 0,
        Trim: Number(row['trimCost']) || 0,
        Labor: Number(row['laborCost']) || 0,
        Overhead: Number(row['overheadCost']) || 0,
        Margin: Number(row['marginCost']) || 0,
      });
    }
    this.call(SP.GET_SUGGESTIONS, { session_id: id });
  }

  // ── Suggestions ────────────────────────────────────────────

  generate(): void {
    const c = this.costs();
    if (!c || !this.isDraft() || this.generating()) return;
    const s = this.style();
    const fabricDirection = parseList(this.header()?.fabricDirection).join(', ');
    this.generating.set(true);
    this.http
      .post<SuggestionIdea[]>(`${environment.apiBaseUrl}/api/v1/cost-optimization/suggestions`, {
        styleName: s?.styleName ?? '',
        currentFob: this.currentFob(),
        targetFob: this.targetFob(),
        fabricCost: c.Fabric,
        fabricDesc: '',
        trimCost: c.Trim,
        trimDesc: '',
        laborCost: c.Labor,
        overheadCost: c.Overhead,
        marginCost: c.Margin,
        // TMS sent its sample fabric/trim text here; LT ODM sends only what is known about the style.
        bomSummary: fabricDirection ? `Concept fabric direction: ${fabricDirection}` : '',
        cmtSummary: `Labor/CMT: $${c.Labor.toFixed(2)}; Overhead: $${c.Overhead.toFixed(2)}`,
      })
      .subscribe({
        next: (ideas) => {
          this.generating.set(false);
          this.persistIdeas(ideas);
        },
        error: (e) => {
          this.generating.set(false);
          toast.error(this.transloco.translate('co.aiFailed'), { description: problemMessage(e) });
        },
      });
  }

  /** Replaces the session's ideas, in order: clear -> save all (one batch call) -> reload with database ids. */
  private persistIdeas(ideas: SuggestionIdea[]): void {
    const session = this.session();
    if (!session || !ideas.length) return;
    this.pendingIdeas = ideas;
    this.persisting.set(true);
    this.call(SP.CLEAR_SUGGESTIONS, { session_id: session.id });
  }

  toggle(s: Suggestion): void {
    if (!this.isDraft() || this.persisting()) return;
    const applied = !s.applied;
    this.suggestions.update((list) => list.map((x) => (x.id === s.id ? { ...x, applied } : x)));
    this.call(SP.TOGGLE_SUGGESTION, { suggestion_id: s.id, is_applied: applied ? 1 : 0 });
  }

  applyAll(): void {
    const toApply = this.suggestions().filter((s) => !s.applied);
    if (!this.isDraft() || !toApply.length) return;
    this.suggestions.update((list) => list.map((x) => ({ ...x, applied: true })));
    this.call(SP.TOGGLE_SUGGESTION, toApply.map((s) => ({ suggestion_id: s.id, is_applied: 1 })));
  }

  // ── Review ─────────────────────────────────────────────────

  openReview(): void {
    this.factorySearch.set('');
    this.chosenFactory.set(null);
    this.reviewNotes.set('');
    this.reviewOpen.set(true);
    if (!this.factories().length) this.call(SP.FACTORIES);
  }

  toggleFavorite(id: string): void {
    this.favorites.update((favs) => {
      const next = new Set(favs);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      try {
        localStorage.setItem(FAVORITES_KEY, JSON.stringify([...next]));
      } catch {
        /* per-browser convenience only */
      }
      return next;
    });
  }

  submitReview(): void {
    const session = this.session();
    const factory = this.chosenFactory();
    if (!session || !factory || this.submitting() || !this.appliedSavings()) return;
    this.submitting.set(true);
    const applied = this.suggestions().filter((s) => s.applied);
    // submitted_by, location and user_group are filled by the API from the sign-in token.
    this.call(SP.SUBMIT_REVIEW, {
      session_id: session.id,
      sent_to_type: 'factory',
      sent_to_id: factory.id,
      sent_to_name: factory.name,
      total_saving: this.appliedSavings(),
      projected_fob: this.projectedFob(),
      target_fob: this.targetFob(),
      current_fob: this.currentFob(),
      applied_suggestions: JSON.stringify(
        applied.map((s) => ({ id: `ai-${s.id}`, title: s.title, category: s.category, saving: s.saving, fromPrice: s.fromPrice, toPrice: s.toPrice })),
      ),
      notes: this.reviewNotes().trim() || null,
    });
  }

  /** The factory sees the plan as a "For Review" offer in SBU Submission (location = the factory it was sent to). */
  private mirrorToSbuSubmission(factory: Factory, notes: string): void {
    const h = this.header();
    const s = this.style();
    if (!h) return;
    const concept = this.concepts().find((c) => c.recid === h.conceptRecid);
    let originalCosts: unknown = [];
    try {
      originalCosts = JSON.parse(s?.costBreakdown || '[]');
    } catch {
      originalCosts = [];
    }
    this.call(SP.INS_SBU_REVIEW, {
      concept_recid: h.conceptRecid,
      concept_name: h.conceptName,
      product_name: s?.styleName ?? '',
      style_code: s?.styleId || this.qid() || '',
      factory_name: factory.name,
      image_url: s?.imageUrl || null,
      projected_fob: this.projectedFob(),
      target_fob: this.targetFob(),
      cost_breakdown: JSON.stringify({
        costOptimization: true,
        originalFob: this.currentFob(),
        projectedFob: this.projectedFob(),
        originalCosts,
        aiSuggestions: this.suggestions()
          .filter((x) => x.applied)
          .map((x) => ({ item: x.title, amount: +x.saving.toFixed(2) })),
      }),
      notes: notes || null,
      location: factory.id,
      season_id: concept?.season_id || null,
    });
  }

  recall(): void {
    const session = this.session();
    if (!session || this.recalling()) return;
    this.recalling.set(true);
    // recalled_by is filled by the API from the sign-in token.
    this.call(SP.RECALL_REVIEW, { session_id: session.id, style_id: this.style()?.styleId || this.qid() });
  }

  // ── Hub results ────────────────────────────────────────────

  private onResult(res: FlatResult): void {
    const proc = procName(res.procedure);
    const error = spError(res);
    const data = spTables(res);
    const rows = data['table0'] ?? [];
    const row = rows[0];

    switch (proc) {
      case SP.CONCEPTS:
        this.concepts.set(rows as ConceptStudioResult[]);
        this.conceptsLoaded.set(true);
        break;

      case SP.HEADER: {
        const h = row
          ? ({
              collectionRecid: Number(row['collectionRecid']),
              conceptRecid: Number(row['conceptRecid']),
              conceptName: String(row['conceptName'] ?? ''),
              customer: String(row['customer'] ?? ''),
              season: String(row['season'] ?? ''),
              targetFob: Number(row['targetFob']) || 0,
              fabricDirection: String(row['fabricDirection'] ?? '[]'),
            } satisfies CollectionHeader)
          : null;
        if (!h) {
          this.header.set(null);
          this.styles.set([]);
          this.stylesLoading.set(false);
          this.noCollection.set(true);
          break;
        }
        this.header.set(h);
        this.selectedConceptRecid.set(h.conceptRecid);
        if (h.collectionRecid !== this.collectionRecid) {
          void this.router.navigate([], { relativeTo: this.route, queryParams: { collectionRecid: h.collectionRecid, qid: null }, queryParamsHandling: 'merge' });
        } else if (this.qid() && !this.session() && !this.sessionLoading()) {
          this.openSession(); // target FOB now known
        }
        break;
      }

      case SP.ITEMS: {
        const styles = rows.map((r) => this.toStyle(r));
        this.styles.set(styles);
        this.stylesLoading.set(false);
        this.noCollection.set(false);
        const conceptRecid = styles[0]?.conceptRecid;
        if (conceptRecid && this.header()?.collectionRecid !== this.collectionRecid) {
          this.call(SP.HEADER, { concept_recid: conceptRecid });
        } else if (this.qid() && !this.session() && !this.sessionLoading()) {
          this.openSession();
        }
        break;
      }

      case SP.LOAD_SESSION:
        if (row) this.applySession(row);
        else {
          this.sessionLoading.set(false);
          toast.error(error ?? this.transloco.translate('co.sessionFailed'));
        }
        break;

      case SP.GET_SUGGESTIONS:
        this.persisting.set(false);
        this.suggestions.set(
          rows.map((r) => ({
            id: Number(r['suggestionId']),
            category: r['category'],
            title: String(r['title'] ?? ''),
            detail: String(r['detail'] ?? ''),
            fromPrice: Number(r['fromPrice']) || 0,
            toPrice: Number(r['toPrice']) || 0,
            saving: Number(r['saving']) || 0,
            applied: r['isApplied'] === 1 || r['isApplied'] === true,
          })),
        );
        break;

      case SP.CLEAR_SUGGESTIONS: {
        const session = this.session();
        if (!session) break;
        if (error || !resultOk(row)) {
          this.persisting.set(false);
          toast.error(error ?? (resultText(row) || this.transloco.translate('co.saveIdeasFailed')));
          break;
        }
        this.call(
          SP.SAVE_SUGGESTION,
          this.pendingIdeas.map((s, i) => ({
            session_id: session.id,
            category: s.category,
            title: s.title,
            detail: s.detail,
            from_price: s.fromPrice,
            to_price: s.toPrice,
            saving: s.saving,
            sort_order: i,
          })),
        );
        break;
      }

      case SP.SAVE_SUGGESTION: {
        const session = this.session();
        if (error) toast.error(error);
        if (session) this.call(SP.GET_SUGGESTIONS, { session_id: session.id });
        break;
      }

      case SP.TOGGLE_SUGGESTION:
        if (error || rows.some((r) => !resultOk(r))) {
          toast.error(error ?? (resultText(rows.find((r) => !resultOk(r))) || this.transloco.translate('common.error')));
          const session = this.session();
          if (session) this.call(SP.GET_SUGGESTIONS, { session_id: session.id });
        }
        break;

      case SP.FACTORIES:
        this.factories.set(rows.map((r) => ({ id: String(r['Partner_ID'] ?? r['Partner_Id'] ?? ''), name: String(r['Full_name'] ?? r['Full_Name'] ?? '') })));
        break;

      case SP.SUBMIT_REVIEW: {
        this.submitting.set(false);
        const factory = this.chosenFactory();
        if (error || !resultOk(row) || !factory) {
          toast.error(error ?? (resultText(row) || this.transloco.translate('co.submitFailed')));
          break;
        }
        this.mirrorToSbuSubmission(factory, this.reviewNotes().trim());
        this.session.update((s) => (s ? { ...s, status: 'under_review', reviewStatus: 'pending', sentToName: String(row?.['sentToName'] || factory.name) } : s));
        this.styles.update((list) => list.map((x) => (x.qid === this.qid() ? { ...x, hasSession: true, sessionStatus: 'under_review' } : x)));
        this.reviewOpen.set(false);
        toast.success(this.transloco.translate('co.submitted', { name: factory.name }));
        break;
      }

      case SP.INS_SBU_REVIEW:
        if (error || !resultOk(row)) toast.error(this.transloco.translate('co.mirrorFailed'), { description: error ?? resultText(row) });
        break;

      case SP.RECALL_REVIEW:
        this.recalling.set(false);
        if (error || !resultOk(row)) {
          toast.error(error ?? (resultText(row) || this.transloco.translate('common.error')));
          break;
        }
        this.session.update((s) => (s ? { ...s, status: 'draft', reviewStatus: '', sentToName: '', reviewerComment: '' } : s));
        this.styles.update((list) => list.map((x) => (x.qid === this.qid() ? { ...x, sessionStatus: '' } : x)));
        toast.success(this.transloco.translate('co.recalled'));
        break;
    }
  }

  private toStyle(r: Record<string, any>): CostStyle {
    const gqQid = String(r['gqQid'] ?? '').trim();
    return {
      qid: gqQid || `item-${r['itemRecid']}`,
      itemRecid: Number(r['itemRecid']),
      conceptRecid: Number(r['conceptRecid']) || 0,
      styleName: String(r['productName'] ?? ''),
      styleId: String(r['styleCode'] ?? ''),
      factoryName: String(r['sbu'] || r['country'] || ''),
      currentFob: Number(r['fobPrice']) || 0,
      imageUrl: resolveImageUrl(r['imageUrl']),
      costBreakdown: String(r['costBreakdown'] ?? '[]'),
      hasSession: Number(r['hasSession']) > 0,
      sessionStatus: String(r['sessionStatus'] ?? ''),
    };
  }
}

function loadFavorites(): Set<string> {
  try {
    const raw = localStorage.getItem(FAVORITES_KEY);
    return new Set(raw ? (JSON.parse(raw) as string[]) : []);
  } catch {
    return new Set();
  }
}
