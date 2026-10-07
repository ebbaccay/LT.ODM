import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCheck,
  lucideImage,
  lucideMapPin,
  lucidePackageSearch,
  lucidePencil,
  lucidePlus,
  lucideRotateCcw,
  lucideSearch,
  lucideSend,
  lucideTrash2,
  lucideUndo2,
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
import { StoredProcSignalRService } from '@services/spHub.service';
import { UserInfoService } from '@services/user-info.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { mapGqProducts, mapSbuProducts } from '@tms/shared/sbu-products';
import { FlatResult, procName, spError, spTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { ConceptPicker } from '@tms/shared/ui/concept-picker';
import { Modal } from '@tms/shared/ui/modal';
import { NumberInputDirective } from '@tms/shared/ui/number-input';
import { toneBadge } from '@tms/shared/ui/tones';
import { ConceptStudioResult, resolveImageUrl } from '../concept-studio/concept-studio.model';
import {
  CostBreakdownItem,
  OfferProduct,
  SBU_SUBMISSION_SP as SP,
  STATUS_TONE,
  SbuSubmission,
  costItems,
  costOptFob,
  costOptSuggestions,
} from './sbu-submission.model';

type ReviewAction = 'notes' | 'approve' | 'reject' | 'revise' | 'unapprove';

/**
 * SBU Submission (TMS manage-offering): SBUs and factories offer products against a saved concept; merchandisers
 * approve, reject, or send an offer back to the factory for a cost proposal.
 *
 * The API enforces what TMS only did in the browser: factory users only receive their own factory's products,
 * and the submitter, location and user group come from the sign-in token.
 */
@Component({
  selector: 'app-manage-offering',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, TranslocoPipe, NgIcon, Modal, NumberInputDirective, ConceptPicker, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports,
    HlmLabelImports, HlmSkeletonImports, HlmSpinnerImports,
  ],
  providers: [
    provideIcons({
      lucideCheck, lucideImage, lucideMapPin, lucidePackageSearch, lucidePencil, lucidePlus, lucideRotateCcw,
      lucideSearch, lucideSend, lucideTrash2, lucideUndo2, lucideX,
    }),
  ],
  templateUrl: './manage-offering.html',
})
export class ManageOffering implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly zone = inject(NgZone);
  private readonly userInfo = inject(UserInfoService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDlg = inject(ConfirmDialogService);

  protected readonly toneBadge = toneBadge;
  protected readonly statusTone = STATUS_TONE;
  protected readonly costItems = costItems;
  protected readonly costOptSuggestions = costOptSuggestions;
  protected readonly costOptFob = costOptFob;

  private readonly user = this.userInfo.user;
  readonly isFactoryUser = ['FTY', 'FACTORY'].includes((this.user?.userGroup ?? '').toUpperCase());

  // ── Concepts ───────────────────────────────────────────────
  readonly concepts = signal<ConceptStudioResult[]>([]);
  readonly conceptsLoaded = signal(false);
  readonly selectedConceptRecid = signal<number | null>(null);
  readonly selectedConcept = computed(() => this.concepts().find((c) => c.recid === this.selectedConceptRecid()) ?? null);

  // ── Products (picker) ──────────────────────────────────────
  readonly products = signal<OfferProduct[]>([]);
  readonly productsPending = signal(0);

  // ── Submissions ────────────────────────────────────────────
  readonly submissions = signal<SbuSubmission[]>([]);
  readonly loadingSubmissions = signal(false);
  readonly filterMode = signal<'all' | 'mine'>('all');

  private isMine = (s: SbuSubmission) =>
    this.isFactoryUser ? !!this.user?.location && s.location === this.user.location : s.location === (this.user?.location ?? '') || s.username === this.user?.username;

  /** Factory users only ever see their own factory's offers (as in TMS). */
  readonly visibleSubmissions = computed(() =>
    this.isFactoryUser || this.filterMode() === 'mine' ? this.submissions().filter(this.isMine) : this.submissions(),
  );
  readonly myCount = computed(() => this.submissions().filter(this.isMine).length);

  // ── Dialogs ────────────────────────────────────────────────
  readonly pickerOpen = signal(false);
  readonly pickerSearch = signal('');
  readonly pickerProduct = signal<OfferProduct | null>(null);
  readonly pickerNotes = signal('');
  readonly filteredProducts = computed(() => {
    const q = this.pickerSearch().toLowerCase().trim();
    return q
      ? this.products().filter((p) => [p.name, p.styleCode, p.category, p.sbu].some((v) => (v ?? '').toLowerCase().includes(q)))
      : this.products();
  });

  readonly notesFor = signal<SbuSubmission | null>(null);
  readonly notesText = signal('');

  readonly proposalFor = signal<SbuSubmission | null>(null);
  readonly proposalItems = signal<CostBreakdownItem[]>([]);
  readonly proposalNotes = signal('');
  readonly proposalTotal = computed(() => this.proposalItems().reduce((sum, i) => sum + (i.amount || 0), 0));
  readonly proposalValid = computed(() => this.proposalItems().length > 0 && this.proposalItems().every((i) => i.item.trim() !== '' && i.amount >= 0));

  // ── Busy state ─────────────────────────────────────────────
  readonly saving = signal(false);
  /** Submission being changed (approve, reject, submit, ...) */
  readonly busyRecid = signal<number | null>(null);
  private pendingUpdate: ReviewAction | null = null;

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));

    // Factory users and Admins get every concept, office users their team's; the API decides from the sign-in token.
    this.call(SP.CONCEPTS, {});
    // Factory users get only their own factory's products: the API sets the filter from the sign-in token.
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

  // ── Hub results ────────────────────────────────────────────

  private onResult(res: FlatResult): void {
    const proc = procName(res.procedure);
    const error = spError(res);
    const data = spTables(res);
    const row = data['table0']?.[0];
    const status = row?.['result_status'];
    const ok = !error && (status === undefined || status === 'Success');
    const reason = error ?? (status ? String(status) : 'unknown error');

    switch (proc) {
      case SP.CONCEPTS: {
        const rows = (data['table0'] ?? []) as ConceptStudioResult[];
        this.concepts.set(rows);
        this.conceptsLoaded.set(true);
        if (rows.length && this.selectedConceptRecid() === null) this.selectConcept(rows[0].recid);
        break;
      }
      case SP.PRODUCTS:
        this.mergeProducts('manual', mapSbuProducts(data));
        break;
      case SP.GQ_APPROVED:
        this.mergeProducts('gq', mapGqProducts(data));
        break;
      case SP.READ:
        this.submissions.set(
          ((data['table0'] ?? []) as SbuSubmission[]).map((s) => ({
            ...s,
            imageUrl: resolveImageUrl(s.imageUrl),
            fobPrice: Number(s.fobPrice) || 0,
            targetFob: Number(s.targetFob) || 0,
          })),
        );
        this.loadingSubmissions.set(false);
        break;
      case SP.INSERT:
        this.saving.set(false);
        if (ok) {
          toast.success(this.transloco.translate('mo.notifications.saveSuccess'));
          this.pickerOpen.set(false);
          this.fetchSubmissions();
        } else {
          toast.error(this.transloco.translate('mo.notifications.saveFailed', { reason }));
        }
        break;
      case SP.UPDATE:
        this.onUpdated(ok, reason);
        break;
      case SP.DELETE:
        this.done(ok, 'mo.notifications.deleteSuccess', 'mo.notifications.deleteFailed');
        break;
      case SP.SUBMIT:
        this.done(ok, 'mo.notifications.submitSuccess', 'mo.notifications.submitFailed');
        break;
      case SP.FTY_PROPOSAL:
        this.saving.set(false);
        if (ok) {
          toast.success(this.transloco.translate('mo.notifications.ftyProposalSuccess'));
          this.proposalFor.set(null);
          this.fetchSubmissions();
        } else {
          toast.error(this.transloco.translate('mo.notifications.ftyProposalFailed', { reason }));
        }
        break;
    }
  }

  private done(ok: boolean, successKey: string, failKey: string): void {
    this.busyRecid.set(null);
    if (ok) toast.success(this.transloco.translate(successKey));
    else toast.error(this.transloco.translate(failKey));
    this.fetchSubmissions();
  }

  private onUpdated(ok: boolean, reason: string): void {
    const action = this.pendingUpdate;
    this.pendingUpdate = null;
    this.busyRecid.set(null);
    this.saving.set(false);
    if (!ok) {
      toast.error(this.transloco.translate('mo.notifications.updateFailed', { reason }));
      return;
    }
    const key =
      action === 'unapprove'
        ? 'mo.notifications.unapproveSuccess'
        : action === 'revise'
          ? 'mo.notifications.reviseSuccess'
          : action === 'notes'
            ? 'mo.notifications.notesUpdated'
            : 'mo.notifications.reviewUpdated';
    toast.success(this.transloco.translate(key));
    if (action === 'notes') this.notesFor.set(null);
    this.fetchSubmissions();
  }

  private mergeProducts(source: 'manual' | 'gq', rows: OfferProduct[]): void {
    this.products.update((current) => [...current.filter((p) => p.source !== source), ...rows]);
    this.productsPending.update((n) => Math.max(0, n - 1));
  }

  // ── Concepts ───────────────────────────────────────────────

  selectConcept(recid: number): void {
    this.selectedConceptRecid.set(recid);
    this.fetchSubmissions();
  }

  fetchSubmissions(): void {
    const recid = this.selectedConceptRecid();
    if (recid === null) return;
    this.loadingSubmissions.set(true);
    this.call(SP.READ, { concept_recid: recid });
  }

  // ── New offer ──────────────────────────────────────────────

  openPicker(): void {
    this.pickerSearch.set('');
    this.pickerProduct.set(null);
    this.pickerNotes.set('');
    this.pickerOpen.set(true);
  }

  /** Records the product snapshot as a submitted offer (TMS also only offered "Submit", not "Save as draft"). */
  submitOffer(): void {
    const product = this.pickerProduct();
    const concept = this.selectedConcept();
    if (!product || !concept || this.saving()) return;
    const gq = product.source === 'gq';
    this.saving.set(true);
    this.call(SP.INSERT, {
      concept_recid: concept.recid,
      concept_name: concept.concept_name ?? '',
      product_recid: gq ? null : (product.recid ?? null),
      target_fob: parseFloat(concept.fob_price) || null,
      notes: this.pickerNotes().trim() || null,
      status: 'Submitted',
      ...(gq
        ? {
            snap_product_name: product.name,
            snap_style_code: product.styleCode,
            snap_sbu: product.sbu,
            snap_image_url: product.image,
            snap_fob_price: product.fobPrice,
            snap_cost_breakdown: product.costBreakdown,
          }
        : {}),
    });
  }

  // ── Own drafts ─────────────────────────────────────────────

  isOwn = (s: SbuSubmission) => s.username === this.user?.username;

  openNotes(s: SbuSubmission): void {
    this.notesFor.set(s);
    this.notesText.set(s.notes ?? '');
  }

  saveNotes(): void {
    const s = this.notesFor();
    if (!s || this.saving()) return;
    this.saving.set(true);
    this.update(s, 'notes', { notes: this.notesText().trim() || null, new_status: null, status_reason: null });
  }

  quickSubmit(s: SbuSubmission): void {
    if (s.status !== 'Draft') return;
    this.busyRecid.set(s.recid);
    this.call(SP.SUBMIT, { recid: s.recid });
  }

  async remove(s: SbuSubmission): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('mo.deleteConfirmTitle'),
      message: this.transloco.translate('mo.deleteConfirm', { name: s.productName }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.busyRecid.set(s.recid);
    this.call(SP.DELETE, { recid: s.recid });
  }

  // ── Review (merchandisers) ─────────────────────────────────

  canReview = (s: SbuSubmission) => !this.isFactoryUser && (s.status === 'Submitted' || s.status === 'Factory Submitted');

  approve(s: SbuSubmission): void {
    if (this.canReview(s)) this.update(s, 'approve', { new_status: 'Accepted', status_reason: 'Approved for Collection Builder' });
  }

  reject(s: SbuSubmission): void {
    if (this.canReview(s)) this.update(s, 'reject', { new_status: 'Rejected', status_reason: 'Rejected during concept review' });
  }

  revise(s: SbuSubmission): void {
    if (!this.isFactoryUser && s.status === 'Factory Submitted')
      this.update(s, 'revise', { new_status: 'For Review', status_reason: 'Sent back to factory for revision' });
  }

  async unapprove(s: SbuSubmission): Promise<void> {
    if (this.isFactoryUser || s.status !== 'Accepted') return;
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('mo.unapproveConfirmTitle'),
      message: this.transloco.translate('mo.unapproveConfirm', { name: s.productName }),
      confirmLabel: this.transloco.translate('mo.unapprove'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'warning',
    });
    if (ok) this.update(s, 'unapprove', { new_status: 'Submitted', status_reason: null });
  }

  /** web_rd_upd_sbu_submission always writes @notes, so the current notes are passed through. */
  private update(s: SbuSubmission, action: ReviewAction, changes: Record<string, unknown>): void {
    this.pendingUpdate = action;
    this.busyRecid.set(s.recid);
    this.call(SP.UPDATE, { recid: s.recid, notes: s.notes || null, ...changes });
  }

  // ── Factory cost proposal ──────────────────────────────────

  canPropose = (s: SbuSubmission) => s.status === 'For Review' && !!this.user?.location && s.location === this.user.location;

  openProposal(s: SbuSubmission): void {
    this.proposalItems.set(costItems(s.costBreakdown).map((i) => ({ ...i })));
    this.proposalNotes.set(s.notes ?? '');
    this.proposalFor.set(s);
  }

  addProposalItem(): void {
    this.proposalItems.update((list) => [...list, { item: '', amount: 0 }]);
  }

  setProposalItem(index: number, patch: Partial<CostBreakdownItem>): void {
    this.proposalItems.update((list) => list.map((it, i) => (i === index ? { ...it, ...patch } : it)));
  }

  removeProposalItem(index: number): void {
    this.proposalItems.update((list) => list.filter((_, i) => i !== index));
  }

  submitProposal(): void {
    const s = this.proposalFor();
    if (!s || this.saving() || !this.proposalValid()) return;
    this.saving.set(true);
    // @submitted_by is set by the API from the sign-in token.
    this.call(SP.FTY_PROPOSAL, {
      recid: s.recid,
      cost_breakdown: JSON.stringify(this.proposalItems().map((i) => ({ item: i.item.trim(), amount: +i.amount.toFixed(2) }))),
      total_fob: +this.proposalTotal().toFixed(2),
      notes: this.proposalNotes().trim() || null,
    });
  }

  summary = (p: OfferProduct) => [p.sbu, p.country, p.category].filter(Boolean).join(' · ');

  fobTone(s: SbuSubmission): string {
    if (!s.targetFob) return '';
    return s.fobPrice > s.targetFob ? 'text-red-600 dark:text-red-400' : s.fobPrice < s.targetFob ? 'text-emerald-600 dark:text-emerald-400' : '';
  }
}
