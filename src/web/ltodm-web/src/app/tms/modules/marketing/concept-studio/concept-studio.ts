import { DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, NgZone, OnDestroy, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFilePlus2,
  lucideImagePlus,
  lucideLightbulb,
  lucidePlus,
  lucideSave,
  lucideSearch,
  lucideSettings2,
  lucideSparkles,
  lucideX,
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
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { FlatResult, procName, spError, spTables, SpTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { AuthService, problemMessage } from '../../../../core/auth/auth.service';
import { compressImage } from '@tms/shared/image-compress';
import { GqBomItem, GqCmtItem, calcTotals } from '../garment-quotation/garment-quotation.model';
import {
  CONCEPT_STUDIO_SP as SP,
  ConceptDraft,
  ConceptScope,
  ConceptDraftRequest,
  ConceptStudioResult,
  SbuMatchProduct,
  TargetMarket,
  TrendTag,
  parseList,
  resolveImageUrl,
} from './concept-studio.model';
import { SavedConcepts } from './saved-concepts/saved-concepts';
import { ConceptForMatch, ConceptStyleMatches } from '../../../../features/ai-studio/concept-style-matches';

type TabId = 'new' | 'saved';

/** Inspiration images are resized to at most this many pixels per side and sent as JPEG. */
const MAX_DIMENSION = 1200;

/**
 * Concept Studio (ported from TMS): describe a concept, let AI write the brief, collect inspiration images,
 * see SBU / approved-quotation products near the target FOB, and save concepts.
 *
 * Changes from TMS:
 * - AI brief: POST /api/v1/concept-studio/draft (signed-in, rate-limited) instead of a hub method; errors are shown.
 * - Images: uploaded to the API (signed-in, checked by content); removing one from the board no longer deletes
 *   the file, so a saved concept keeps its images until it is saved without them.
 * - Switching tabs keeps the form; "New" asks before discarding unsaved work.
 * - username / location / user group are filled from the sign-in token by the API, not sent from the browser.
 * - Saved concepts are shared with the team (same user group + location); Admins can list every concept. Only the
 *   creator or an Admin can change one; loading someone else's concept loads a copy. The API enforces both.
 */

@Component({
  selector: 'app-concept-studio',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, TranslocoPipe, NgIcon, AuthImgDirective, SavedConcepts, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports,
    HlmLabelImports, HlmSkeletonImports, HlmSpinnerImports, ConceptStyleMatches,
  ],
  providers: [
    provideIcons({ lucideFilePlus2, lucideImagePlus, lucideLightbulb, lucidePlus, lucideSave, lucideSearch, lucideSettings2, lucideSparkles, lucideX }),
  ],
  templateUrl: './concept-studio.html',
})
export class ConceptStudio implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly zone = inject(NgZone);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDlg = inject(ConfirmDialogService);

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('imageFileInput');
  private readonly api = `${environment.apiBaseUrl}/api/v1/concept-studio`;

  readonly activeTab = signal<TabId>('new');

  // ── Form ───────────────────────────────────────────────────
  readonly conceptName = signal('');
  readonly customer = signal('');
  readonly customerId = signal('');
  readonly season = signal('');
  readonly seasonId = signal('');
  readonly market = signal('');
  readonly fobPrice = signal('');
  readonly activeTags = signal<string[]>([]);

  // ── Reference data ─────────────────────────────────────────
  readonly customers = signal<{ Partner_Id: string; Full_Name: string }[]>([]);
  readonly seasons = signal<{ Season_id: string; Description: string }[]>([]);
  readonly trendTags = signal<TrendTag[]>([]);
  readonly markets = signal<TargetMarket[]>([]);
  readonly tagEditMode = signal(false);
  readonly marketEditMode = signal(false);
  readonly newTagName = signal('');
  readonly newMarketName = signal('');

  // ── AI brief ───────────────────────────────────────────────
  readonly brief = signal('');
  readonly suggestedProducts = signal<string[]>([]);
  readonly fabricDirection = signal<string[]>([]);
  readonly sustainabilityNotes = signal<string[]>([]);
  readonly isDrafting = signal(false);

  // ── Inspiration board ──────────────────────────────────────
  readonly images = signal<string[]>([]);
  readonly uploadsInFlight = signal(0);
  readonly isUploading = computed(() => this.uploadsInFlight() > 0);

  // ── Saved concepts ─────────────────────────────────────────
  readonly savedConcepts = signal<ConceptStudioResult[]>([]);
  readonly savedLoaded = signal(false);
  readonly savedScope = signal<ConceptScope>('team');
  /** Admins may list every concept (the API ignores 'all' for anyone else). */
  readonly canSeeAll = computed(() => (this.auth.user()?.roles ?? []).some((r) => r.toLowerCase() === 'admin'));
  readonly editingRecid = signal<number | null>(null);
  readonly isSaving = signal(false);
  private pendingDeleteRecid: number | null = null;

  // ── Matching products ──────────────────────────────────────
  /** The concept as it stands on screen, for "proven styles in the library" (AI Studio). */
  readonly conceptForMatch = computed<ConceptForMatch>(() => ({
    name: this.conceptName(), customer: this.customer(), season: this.season(), targetMarket: this.market(), targetPrice: this.fobPrice(),
    trends: this.activeTags(), brief: this.brief(), products: this.suggestedProducts(), fabrics: this.fabricDirection(), notes: this.sustainabilityNotes(),
  }));

  readonly sbuProducts = signal<SbuMatchProduct[]>([]);
  readonly sbuPending = signal(0);
  readonly targetFob = computed(() => parseFloat(this.fobPrice().replace(/[^0-9.]/g, '')) || 0);

  /** Products within 50%-175% of the target FOB, closest first (same rule as TMS). */
  readonly matchingProducts = computed(() => {
    const target = this.targetFob();
    if (target <= 0) return [];
    return this.sbuProducts()
      .filter((p) => p.fobPrice >= target * 0.5 && p.fobPrice <= target * 1.75)
      .sort((a, b) => Math.abs(a.fobPrice - target) - Math.abs(b.fobPrice - target))
      .slice(0, 8);
  });

  readonly hasWork = computed(
    () =>
      this.editingRecid() !== null ||
      [this.conceptName(), this.customer(), this.season(), this.market(), this.fobPrice(), this.brief()].some((v) => v.trim() !== '') ||
      this.activeTags().length > 0 ||
      this.images().length > 0,
  );

  readonly canGenerate = computed(
    () => !this.isDrafting() && (this.conceptName().trim() !== '' || this.customer().trim() !== '' || this.activeTags().length > 0),
  );

  /** Same rule as TMS: a new concept needs a brief; an existing one can always be updated. */
  readonly canSave = computed(
    () => !this.isSaving() && !this.isUploading() && (this.editingRecid() !== null || this.brief().trim() !== '') && this.conceptName().trim() !== '',
  );

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));

    this.call(SP.GET_CUSTOMERS);
    this.call(SP.GET_SEASONS);
    this.call(SP.GET_TAGS);
    this.call(SP.GET_MARKETS);
    this.sbuPending.set(2);
    this.call(SP.SBU_PRODUCTS);
    this.call(SP.GQ_APPROVED);
    this.fetchSavedConcepts();
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  private call(proc: string, parameters?: Record<string, unknown>): void {
    void this.sp.invoke('ExecuteStoredProcFlat', parameters ? { SpName: proc, Parameters: parameters } : { SpName: proc });
  }

  fetchSavedConcepts(): void {
    // The API fills the caller from the sign-in token and limits the scope to what they may see.
    this.call(SP.READ, { scope: this.savedScope() });
  }

  setSavedScope(scope: ConceptScope): void {
    if (scope === this.savedScope()) return;
    this.savedScope.set(scope);
    this.savedLoaded.set(false);
    this.fetchSavedConcepts();
  }

  // ── Hub results ────────────────────────────────────────────

  private onResult(res: FlatResult): void {
    const proc = procName(res.procedure);
    const error = spError(res);
    const data = spTables(res);
    const row = data['table0']?.[0];
    const ok = !error && (row?.['result_status'] === undefined || row['result_status'] === 'Success');

    switch (proc) {
      case SP.GET_CUSTOMERS:
        this.customers.set((data['table0'] ?? []) as never);
        break;
      case SP.GET_SEASONS:
        this.seasons.set((data['table0'] ?? []) as never);
        break;
      case SP.GET_TAGS:
        this.trendTags.set((data['table0'] ?? []).map((r) => ({ tag_id: r['tag_id'], tag_name: r['tag_name'], sort_order: r['sort_order'] ?? 0 })));
        break;
      case SP.GET_MARKETS:
        this.markets.set(
          (data['table0'] ?? []).map((r) => ({ market_id: r['market_id'], market_name: r['market_name'], sort_order: r['sort_order'] ?? 0 })),
        );
        break;
      case SP.INS_TAG:
      case SP.DEL_TAG:
        if (!ok) toast.error(error ?? String(row?.['result_status'] ?? this.transloco.translate('common.error')));
        this.call(SP.GET_TAGS);
        break;
      case SP.INS_MARKET:
      case SP.DEL_MARKET:
        if (!ok) toast.error(error ?? String(row?.['result_status'] ?? this.transloco.translate('common.error')));
        this.call(SP.GET_MARKETS);
        break;
      case SP.SBU_PRODUCTS:
        this.mergeProducts(false, this.sbuRows(data));
        break;
      case SP.GQ_APPROVED:
        this.mergeProducts(true, this.gqRows(data));
        break;
      case SP.READ:
        this.savedConcepts.set((data['table0'] ?? []) as ConceptStudioResult[]);
        this.savedLoaded.set(true);
        break;
      case SP.INSERT:
      case SP.UPDATE:
        this.onSaved(proc === SP.INSERT, ok, row, error);
        break;
      case SP.DELETE:
        this.onConceptDeleted(ok, error ?? (ok ? null : (row?.['result_status'] as string | undefined) ?? null));
        break;
      case SP.DELETE_COLLECTION_BUILDER: {
        // "No record" = the concept was never used in a collection: still a successful delete.
        const status = String(row?.['result_status'] ?? '');
        this.pendingDeleteRecid = null;
        if (!error && (status === 'Success' || status.toLowerCase().includes('no record'))) {
          toast.success(this.transloco.translate('cs.notifications.deleteSuccess'));
        } else {
          toast.error(this.transloco.translate('cs.notifications.deleteFailed'));
        }
        this.fetchSavedConcepts();
        break;
      }
    }
  }

  private sbuRows(data: SpTables): SbuMatchProduct[] {
    return (data['table0'] ?? []).map((r) => ({
      id: String(r['recid'] ?? r['id'] ?? ''),
      name: r['name'] ?? '',
      styleCode: r['styleCode'] ?? '',
      sbu: r['sbu'] ?? '',
      fobPrice: parseFloat(r['fobPrice']) || 0,
      category: r['category'] ?? '',
      image: resolveImageUrl(r['image']),
    }));
  }

  /** Approved garment quotations: FOB from the quote, or calculated from its BOM + CMT like the GQ screen. */
  private gqRows(data: SpTables): SbuMatchProduct[] {
    const json = <T>(value: unknown, fallback: T): T => {
      try {
        return typeof value === 'string' && value ? (JSON.parse(value) as T) : fallback;
      } catch {
        return fallback;
      }
    };
    return (data['table0'] ?? []).map((r) => {
      const totals = calcTotals(json<GqBomItem[]>(r['bom_json'], []), json<GqCmtItem[]>(r['cmt_json'], []), parseFloat(r['rate_in_usd']) || 6.55);
      const fob = parseFloat(r['fob_price']) || 0;
      return {
        id: `gq_${r['qid']}`,
        name: r['model_name'] ?? r['style_id'] ?? '',
        styleCode: r['style_id'] ?? '',
        sbu: r['factory_name'] ?? '',
        fobPrice: fob > 0 ? fob : +totals.totalUSD.toFixed(2),
        category: r['category'] ?? '',
        image: resolveImageUrl(r['style_image']),
      };
    });
  }

  private mergeProducts(gq: boolean, rows: SbuMatchProduct[]): void {
    this.sbuProducts.update((current) => [...current.filter((p) => p.id.startsWith('gq_') !== gq), ...rows]);
    this.sbuPending.update((n) => Math.max(0, n - 1));
  }

  // ── Form helpers ───────────────────────────────────────────

  onCustomerInput(value: string): void {
    this.customer.set(value);
    this.customerId.set(this.customers().find((c) => c.Full_Name === value)?.Partner_Id ?? '');
  }

  onSeasonInput(value: string): void {
    this.season.set(value);
    this.seasonId.set(this.seasons().find((s) => s.Description === value)?.Season_id ?? '');
  }

  toggleTag(name: string): void {
    this.activeTags.update((tags) => (tags.includes(name) ? tags.filter((t) => t !== name) : [...tags, name]));
  }

  fobTone(price: number): Tone {
    const target = this.targetFob();
    if (target <= 0) return 'neutral';
    const diff = Math.abs(price - target) / target;
    return diff <= 0.1 ? 'green' : diff <= 0.25 ? 'amber' : 'neutral';
  }

  protected readonly toneBadge = toneBadge;

  // ── Tag / market maintenance (shared lists, as in TMS) ─────

  addTrendTag(): void {
    const name = this.newTagName().trim();
    if (!name) return;
    if (this.trendTags().some((t) => t.tag_name.toLowerCase() === name.toLowerCase())) {
      this.newTagName.set('');
      return;
    }
    this.newTagName.set('');
    this.call(SP.INS_TAG, { tag_name: name, sort_order: this.trendTags().length + 1 });
  }

  deleteTrendTag(tag: TrendTag): void {
    this.trendTags.update((tags) => tags.filter((t) => t.tag_id !== tag.tag_id));
    this.activeTags.update((active) => active.filter((a) => a !== tag.tag_name));
    this.call(SP.DEL_TAG, { tag_id: tag.tag_id });
  }

  addTargetMarket(): void {
    const name = this.newMarketName().trim();
    if (!name) return;
    if (this.markets().some((m) => m.market_name.toLowerCase() === name.toLowerCase())) {
      this.newMarketName.set('');
      return;
    }
    this.newMarketName.set('');
    this.call(SP.INS_MARKET, { market_name: name, sort_order: this.markets().length + 1 });
  }

  deleteTargetMarket(market: TargetMarket): void {
    this.markets.update((list) => list.filter((m) => m.market_id !== market.market_id));
    this.call(SP.DEL_MARKET, { market_id: market.market_id });
  }

  // ── AI brief ───────────────────────────────────────────────

  generate(): void {
    if (!this.canGenerate()) return;
    const request: ConceptDraftRequest = {
      name: this.conceptName().trim(),
      client: this.customer().trim(),
      season: this.season().trim(),
      targetMarket: this.market().trim(),
      targetPrice: this.fobPrice().trim(),
      trends: this.activeTags(),
    };
    this.isDrafting.set(true);
    this.http.post<ConceptDraft>(`${this.api}/draft`, request).subscribe({
      next: (draft) => {
        this.brief.set(draft.summary);
        this.suggestedProducts.set(draft.products);
        this.fabricDirection.set(draft.fabrics);
        this.sustainabilityNotes.set(draft.notes);
        this.isDrafting.set(false);
      },
      error: (e) => {
        this.isDrafting.set(false);
        toast.error(this.transloco.translate('cs.notifications.aiGenerateFailed', { reason: problemMessage(e) }));
      },
    });
  }

  // ── Inspiration images ─────────────────────────────────────

  openImagePicker(): void {
    const input = this.fileInput()?.nativeElement;
    if (!input) return;
    input.value = '';
    input.click();
  }

  onImageFilesSelected(event: Event): void {
    const files = (event.target as HTMLInputElement).files;
    if (files?.length) Array.from(files).forEach((f) => void this.uploadImage(f));
  }

  /** Removes the image from this concept only; the file stays (a saved version of the concept may still use it). */
  removeImage(url: string): void {
    this.images.update((list) => list.filter((i) => i !== url));
  }

  private async uploadImage(file: File): Promise<void> {
    this.uploadsInFlight.update((n) => n + 1);
    let preview: string | null = null;
    try {
      const blob = await compressImage(file, MAX_DIMENSION);
      preview = URL.createObjectURL(blob);
      this.images.update((list) => [...list, preview!]);
      const form = new FormData();
      form.append('file', blob, 'inspiration.jpg');
      const res = await new Promise<{ imageUrl: string }>((resolve, reject) =>
        this.http.post<{ imageUrl: string }>(`${this.api}/images`, form).subscribe({ next: resolve, error: reject }),
      );
      const uploaded = preview;
      this.images.update((list) => list.map((i) => (i === uploaded ? res.imageUrl : i)));
    } catch (e) {
      if (preview) this.images.update((list) => list.filter((i) => i !== preview));
      toast.error(this.transloco.translate('cs.uploadError'), { description: problemMessage(e, file.name) });
    } finally {
      if (preview) setTimeout(() => URL.revokeObjectURL(preview!), 60_000);
      this.uploadsInFlight.update((n) => n - 1);
    }
  }

  // ── Save / load / new ──────────────────────────────────────

  save(): void {
    if (!this.canSave()) return;
    const recid = this.editingRecid();
    // username, location and user_group are filled by the API from the sign-in token.
    const params: Record<string, unknown> = {
      concept_name: this.conceptName().trim(),
      customer: this.customer(),
      customer_id: this.customerId(),
      season: this.season(),
      season_id: this.seasonId(),
      target_market: this.market(),
      fob_price: this.fobPrice(),
      active_tags: JSON.stringify(this.activeTags()),
      concept_brief: this.brief(),
      suggested_products: JSON.stringify(this.suggestedProducts()),
      fabric_direction: JSON.stringify(this.fabricDirection()),
      sustainability_notes: JSON.stringify(this.sustainabilityNotes()),
      inspiration_images: JSON.stringify(this.images().filter((u) => !u.startsWith('blob:'))),
    };
    if (recid !== null) params['recid'] = recid;
    this.isSaving.set(true);
    this.call(recid !== null ? SP.UPDATE : SP.INSERT, params);
  }

  private onSaved(inserted: boolean, ok: boolean, row: Record<string, any> | undefined, error: string | null): void {
    this.isSaving.set(false);
    if (!ok) {
      const reason = error ?? (row?.['result_status'] ? String(row['result_status']) : null);
      toast.error(
        inserted || reason
          ? this.transloco.translate('cs.notifications.saveFailed', { reason: reason ?? 'unknown error' })
          : this.transloco.translate('cs.notifications.updateFailed'),
      );
      return;
    }
    if (inserted) {
      const id = Number(row?.['new_recid']);
      if (id > 0) this.editingRecid.set(id);
    }
    toast.success(this.transloco.translate(inserted ? 'cs.notifications.saveSuccess' : 'cs.notifications.updateSuccess'));
    this.fetchSavedConcepts();
  }

  async startNew(): Promise<void> {
    if (this.hasWork()) {
      const confirmed = await this.confirmDlg.confirm({
        title: this.transloco.translate('cs.newConceptTitle'),
        message: this.transloco.translate('cs.newConceptMessage'),
        confirmLabel: this.transloco.translate('cs.newConceptConfirm'),
        cancelLabel: this.transloco.translate('actions.cancel'),
        variant: 'warning',
      });
      if (!confirmed) return;
    }
    this.clearForm();
    this.activeTab.set('new');
  }

  private clearForm(): void {
    for (const s of [this.conceptName, this.customer, this.customerId, this.season, this.seasonId, this.market, this.fobPrice, this.brief]) s.set('');
    for (const s of [this.activeTags, this.suggestedProducts, this.fabricDirection, this.sustainabilityNotes, this.images]) s.set([]);
    this.editingRecid.set(null);
  }

  loadConcept(c: ConceptStudioResult): void {
    this.conceptName.set(c.concept_name ?? '');
    this.customer.set(c.customer ?? '');
    this.customerId.set(c.customer_id ?? '');
    this.season.set(c.season ?? '');
    this.seasonId.set(c.season_id ?? '');
    this.market.set(c.target_market ?? '');
    this.fobPrice.set(c.fob_price ?? '');
    this.activeTags.set(parseList(c.active_tags));
    this.brief.set(c.concept_brief ?? '');
    this.suggestedProducts.set(parseList(c.suggested_products));
    this.fabricDirection.set(parseList(c.fabric_direction));
    this.sustainabilityNotes.set(parseList(c.sustainability_notes));
    this.images.set(parseList(c.inspiration_images).map(resolveImageUrl).filter(Boolean));
    // A teammate's concept loads as a copy: saving it creates a new concept owned by the signed-in user.
    const own = c.can_edit !== false;
    this.editingRecid.set(own ? c.recid : null);
    this.activeTab.set('new');
    toast.success(this.transloco.translate(own ? 'cs.notifications.loadedConcept' : 'cs.notifications.loadedCopy', { name: c.concept_name }));
  }

  deleteConcept(recid: number): void {
    this.pendingDeleteRecid = recid;
    if (this.editingRecid() === recid) this.editingRecid.set(null);
    this.call(SP.DELETE, { recid });
  }

  /** After the concept is deleted, soft-delete the collection built from it (as TMS did). */
  private onConceptDeleted(ok: boolean, reason: string | null): void {
    if (!ok || this.pendingDeleteRecid === null) {
      this.pendingDeleteRecid = null;
      if (!ok) toast.error(reason ?? this.transloco.translate('cs.notifications.deleteFailed'));
      this.fetchSavedConcepts();
      return;
    }
    this.call(SP.DELETE_COLLECTION_BUILDER, { concept_recid: this.pendingDeleteRecid, status: 'Deleted' });
  }
}
