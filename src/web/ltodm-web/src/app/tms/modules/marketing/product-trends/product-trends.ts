import { DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideInfo, lucideRefreshCw, lucideSparkles, lucideTrendingDown, lucideTrendingUp } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { environment } from '@env/environment';
import { StoredProcSignalRService } from '@services/spHub.service';
import { FlatResult, procName, spError, spTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { ConceptPicker } from '@tms/shared/ui/concept-picker';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { problemMessage } from '../../../../core/auth/auth.service';
import { parseList, resolveImageUrl } from '../concept-studio/concept-studio.model';
import {
  CollectionPick,
  RegionId,
  RegionScore,
  STRENGTH_HEX,
  StyleScore,
  TREND_SP as SP,
  TagType,
  TrendAnalysis,
  TrendHeader,
  TrendStyle,
} from './product-trends.model';

type SaveStep = 'clear' | 'regions' | 'styles' | 'complete';

/**
 * Market Trends (TMS product-trends): an AI read-out of how a collection's styles fit current market trends,
 * by region and by style, saved per collection so it can be reopened without calling the AI again.
 *
 * Changes from TMS:
 * - No sample dashboard: TMS showed an invented "mock mode" (periods, heat map, FOB suggestions) until a collection
 *   was chosen. Here the page asks for a collection.
 * - Regions are a ranked bar chart with a zero line instead of circles on a world map. The map added no information
 *   for six fixed regions and downloaded its map data from a public CDN at runtime.
 * - Growth figures are labelled as AI estimates.
 * - A new analysis replaces the saved one only after the AI answers (TMS cleared the saved scores first, so a failed
 *   call lost them), and saving runs in order instead of as parallel calls.
 */
@Component({
  selector: 'app-product-trends',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, TranslocoPipe, NgIcon, ConceptPicker, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmSkeletonImports,
    HlmSpinnerImports,
  ],
  providers: [provideIcons({ lucideImage, lucideInfo, lucideRefreshCw, lucideSparkles, lucideTrendingDown, lucideTrendingUp })],
  templateUrl: './product-trends.html',
})
export class ProductTrends implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly http = inject(HttpClient);
  private readonly zone = inject(NgZone);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);

  protected readonly toneBadge = toneBadge;

  readonly collections = signal<CollectionPick[]>([]);
  readonly collectionsLoaded = signal(false);
  readonly collectionRecid = signal<number | null>(null);

  readonly sessionId = signal<number | null>(null);
  readonly status = signal<'draft' | 'complete'>('draft');
  readonly analyzedAt = signal('');
  readonly analyzedBy = signal('');
  readonly header = signal<TrendHeader | null>(null);
  readonly styles = signal<TrendStyle[]>([]);
  readonly regions = signal<RegionScore[]>([]);
  readonly styleScores = signal<StyleScore[]>([]);
  readonly insights = signal<string[]>([]);

  readonly loading = signal(false);
  readonly analyzing = signal(false);
  private pending: TrendAnalysis | null = null;
  private readonly saveStep = signal<SaveStep | null>(null);

  readonly hasAnalysis = computed(() => this.status() === 'complete' && (this.regions().length > 0 || this.styleScores().length > 0));
  readonly busy = computed(() => this.analyzing() || this.saveStep() !== null);

  /** Regions sorted by growth; bars share one scale with zero in the middle when any value is negative. */
  readonly regionBars = computed(() => {
    const list = [...this.regions()].sort((a, b) => b.growthPct - a.growthPct);
    const maxPos = Math.max(0, ...list.map((r) => r.growthPct));
    const maxNeg = Math.max(0, ...list.map((r) => -r.growthPct));
    const span = maxPos + maxNeg || 1;
    const zero = (maxNeg / span) * 100;
    return {
      zero,
      hasNegative: maxNeg > 0,
      rows: list.map((r) => ({
        ...r,
        left: r.growthPct >= 0 ? zero : zero - (-r.growthPct / span) * 100,
        width: (Math.abs(r.growthPct) / span) * 100,
      })),
    };
  });

  /** Styles with their scores, best fit first (unscored styles last). */
  readonly scoredStyles = computed(() => {
    const scores = new Map(this.styleScores().map((s) => [s.itemRecid, s]));
    return this.styles()
      .map((s) => ({ ...s, score: scores.get(s.itemRecid) ?? null }))
      .sort((a, b) => (b.score?.trendScore ?? -1) - (a.score?.trendScore ?? -1));
  });

  readonly collectionScore = computed(() => {
    const s = this.styleScores();
    return s.length ? Math.round(s.reduce((sum, x) => sum + x.trendScore, 0) / s.length) : null;
  });
  readonly topRegion = computed(() => this.regionBars().rows[0] ?? null);

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));
    this.call(SP.COLLECTIONS, {});
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('collectionId')) || null;
      if (id === this.collectionRecid()) return;
      this.collectionRecid.set(id);
      this.reset();
      if (id) {
        this.loading.set(true);
        // @username is filled from the sign-in token.
        this.call(SP.LOAD_SESSION, { collection_recid: id });
      }
    });
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  private call(proc: string, parameters?: Record<string, unknown> | Record<string, unknown>[]): void {
    void this.sp.invoke('ExecuteStoredProcFlat', parameters ? { SpName: proc, Parameters: parameters } : { SpName: proc });
  }

  private reset(): void {
    for (const s of [this.styles, this.regions, this.styleScores, this.insights]) s.set([]);
    this.sessionId.set(null);
    this.header.set(null);
    this.status.set('draft');
    this.analyzedAt.set('');
    this.analyzedBy.set('');
    this.analyzing.set(false);
    this.pending = null;
    this.saveStep.set(null);
  }

  selectCollection(recid: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { collectionId: recid }, queryParamsHandling: 'merge' });
  }

  regionName(id: RegionId): string {
    return this.transloco.translate(`pt.region.${id}`);
  }

  scoreTone(score: number): Tone {
    return score >= 75 ? 'green' : score >= 50 ? 'primary' : score >= 25 ? 'amber' : 'red';
  }

  tagTone(tag: TagType): Tone {
    return tag === 'growing' ? 'green' : tag === 'declining' ? 'red' : tag === 'restocked' ? 'violet' : 'primary';
  }

  // ── Analysis ───────────────────────────────────────────────

  analyze(): void {
    const h = this.header();
    const styles = this.styles();
    if (!h || !styles.length || !this.sessionId() || this.busy()) return;
    this.analyzing.set(true);
    this.http
      .post<TrendAnalysis>(`${environment.apiBaseUrl}/api/v1/market-trends/analysis`, {
        conceptName: h.conceptName,
        season: h.season,
        targetMarket: h.targetMarket,
        targetFob: h.targetFob,
        activeTags: parseList(h.activeTags),
        fabricDirection: parseList(h.fabricDirection),
        sustainabilityNotes: parseList(h.sustainabilityNotes),
        styles: styles.map((s) => ({
          itemRecid: s.itemRecid,
          styleName: s.productName,
          styleCode: s.styleCode,
          category: s.category,
          sbu: s.sbu,
          country: s.country,
          fobPrice: s.fobPrice,
        })),
      })
      .subscribe({
        next: (result) => {
          this.analyzing.set(false);
          this.save(result);
        },
        error: (e) => {
          this.analyzing.set(false);
          // The saved analysis (if any) is untouched.
          toast.error(this.transloco.translate('pt.error.analysisFailed'), { description: problemMessage(e) });
        },
      });
  }

  /** Replaces the saved analysis in order: clear -> regions (batch) -> styles (batch) -> mark complete. */
  private save(result: TrendAnalysis): void {
    const sessionId = this.sessionId();
    if (!sessionId) return;
    this.pending = result;
    this.saveStep.set('clear');
    this.call(SP.CLEAR, { session_id: sessionId });
  }

  private static readonly SAVE_ORDER: SaveStep[] = ['clear', 'regions', 'styles', 'complete'];

  /** Called with each save step's result; sends the next step that has something to save. */
  private continueSave(failed: string | null): void {
    const sessionId = this.sessionId();
    const result = this.pending;
    const step = this.saveStep();
    if (!sessionId || !result || !step) return;
    if (failed) {
      this.saveStep.set(null);
      this.pending = null;
      toast.error(this.transloco.translate('pt.error.saveFailed'), { description: failed });
      this.reloadSession();
      return;
    }
    for (let i = ProductTrends.SAVE_ORDER.indexOf(step) + 1; i < ProductTrends.SAVE_ORDER.length; i++) {
      const next = ProductTrends.SAVE_ORDER[i];
      this.saveStep.set(next);
      if (this.sendSaveStep(next, sessionId, result)) return;
    }
    this.saveStep.set(null);
    this.pending = null;
    toast.success(this.transloco.translate('pt.saved'));
    this.reloadSession();
  }

  /** Returns false when the step has nothing to save (no regions / no styles). */
  private sendSaveStep(step: SaveStep, sessionId: number, result: TrendAnalysis): boolean {
    switch (step) {
      case 'regions':
        if (!result.regions.length) return false;
        this.call(
          SP.SAVE_REGION,
          result.regions.map((r) => ({
            session_id: sessionId,
            region_id: r.id,
            trend_label: r.label,
            sub_label: r.subLabel || null,
            strength: r.strength,
            growth_pct: r.growthPct,
            color: STRENGTH_HEX[r.strength],
          })),
        );
        return true;
      case 'styles': {
        if (!result.styles.length) return false;
        const names = new Map(this.styles().map((s) => [s.itemRecid, s.productName]));
        this.call(
          SP.SAVE_STYLE,
          result.styles.map((s) => ({
            session_id: sessionId,
            item_recid: s.itemRecid,
            style_name: names.get(s.itemRecid) ?? '',
            trend_score: s.trendScore,
            trend_tags: s.trendTags.join(','),
            insight: s.insight,
            tag_type: s.tagType,
            growth_pct: s.growthPct,
          })),
        );
        return true;
      }
      case 'complete':
        // @username (analysed by) is filled from the sign-in token.
        this.call(SP.MARK_COMPLETE, { session_id: sessionId, ai_insights: JSON.stringify(result.insights) });
        return true;
      default:
        return false;
    }
  }

  /** Reloads the session (status, insights, analysed at/by), then its scores. */
  private reloadSession(): void {
    const id = this.collectionRecid();
    if (id) this.call(SP.LOAD_SESSION, { collection_recid: id });
  }

  // ── Hub results ────────────────────────────────────────────

  private onResult(res: FlatResult): void {
    const proc = procName(res.procedure);
    const error = spError(res);
    const rows = spTables(res)['table0'] ?? [];
    const failedRow = rows.find((r) => r['resultStatus'] !== undefined && r['resultStatus'] !== 'Success');
    const failure = error ?? (failedRow ? String(failedRow['resultStatus']) : null);

    switch (proc) {
      case SP.COLLECTIONS: {
        const seen = new Set<number>();
        this.collections.set(
          rows
            .map((r) => ({
              recid: Number(r['collectionRecid']),
              concept_name: String(r['conceptName'] ?? ''),
              customer: String(r['customer'] ?? ''),
              season: String(r['season'] ?? ''),
              styleCount: Number(r['styleCount']) || 0,
            }))
            .filter((c) => c.recid > 0 && !seen.has(c.recid) && seen.add(c.recid)),
        );
        this.collectionsLoaded.set(true);
        break;
      }

      case SP.LOAD_SESSION: {
        const r = rows[0];
        const id = Number(r?.['sessionId']) || 0;
        if (!r || id <= 0) {
          this.loading.set(false);
          toast.error(failure ?? this.transloco.translate('pt.error.sessionFailed'));
          break;
        }
        if (Number(r['collectionRecid']) !== this.collectionRecid()) break; // late answer
        this.sessionId.set(id);
        this.status.set(r['status'] === 'complete' ? 'complete' : 'draft');
        this.analyzedAt.set(String(r['analyzedAt'] ?? ''));
        this.analyzedBy.set(String(r['analyzedBy'] ?? ''));
        this.insights.set(r['status'] === 'complete' ? parseList(r['aiInsights']) : []);
        this.header.set({
          collectionRecid: Number(r['collectionRecid']),
          conceptName: String(r['conceptName'] ?? ''),
          customer: String(r['customer'] ?? ''),
          season: String(r['season'] ?? ''),
          targetMarket: String(r['targetMarket'] ?? ''),
          targetFob: Number(r['targetFob']) || 0,
          activeTags: String(r['activeTags'] ?? '[]'),
          fabricDirection: String(r['fabricDirection'] ?? '[]'),
          sustainabilityNotes: String(r['sustainabilityNotes'] ?? '[]'),
        });
        this.call(SP.STYLES, { collection_recid: Number(r['collectionRecid']) });
        if (this.status() === 'complete') {
          this.call(SP.REGION_SCORES, { session_id: id });
          this.call(SP.STYLE_SCORES, { session_id: id });
        } else {
          this.regions.set([]);
          this.styleScores.set([]);
        }
        break;
      }

      case SP.STYLES:
        this.styles.set(
          rows.map((r) => ({
            itemRecid: Number(r['itemRecid']),
            productName: String(r['productName'] ?? ''),
            styleCode: String(r['styleCode'] ?? ''),
            category: String(r['category'] ?? ''),
            sbu: String(r['sbu'] ?? ''),
            country: String(r['country'] ?? ''),
            imageUrl: resolveImageUrl(r['imageUrl']),
            fobPrice: Number(r['fobPrice']) || 0,
          })),
        );
        this.loading.set(false);
        break;

      case SP.REGION_SCORES:
        this.regions.set(
          rows.map((r) => ({
            regionId: String(r['regionId']) as RegionId,
            trendLabel: String(r['trendLabel'] ?? ''),
            subLabel: String(r['subLabel'] ?? ''),
            strength: (r['strength'] ?? 'Emerging') as RegionScore['strength'],
            growthPct: Number(r['growthPct']) || 0,
          })),
        );
        break;

      case SP.STYLE_SCORES:
        this.styleScores.set(
          rows.map((r) => ({
            itemRecid: Number(r['itemRecid']),
            trendScore: Number(r['trendScore']) || 0,
            trendTags: String(r['trendTags'] ?? '')
              .split(',')
              .map((t) => t.trim())
              .filter(Boolean),
            insight: String(r['insight'] ?? ''),
            tagType: (r['tagType'] ?? 'emerging') as TagType,
            growthPct: Number(r['growthPct']) || 0,
          })),
        );
        break;

      case SP.CLEAR:
      case SP.SAVE_REGION:
      case SP.SAVE_STYLE:
      case SP.MARK_COMPLETE:
        if (this.saveStep()) this.continueSave(failure);
        break;
    }
  }
}
