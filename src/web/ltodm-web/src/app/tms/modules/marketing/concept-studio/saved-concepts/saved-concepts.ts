import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideFileInput, lucideLightbulb, lucideSearch, lucideTrash2 } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { ConceptScope, ConceptStudioResult, parseList, resolveImageUrl } from '../concept-studio.model';

/**
 * Saved concepts list with a detail dialog (ported from the TMS SavedConcepts component).
 * Lists the team's concepts by default; delete is offered only where the API says the user may (can_edit).
 */
@Component({
  selector: 'app-saved-concepts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TranslocoPipe, NgIcon, Modal, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideEye, lucideFileInput, lucideLightbulb, lucideSearch, lucideTrash2 })],
  templateUrl: './saved-concepts.html',
})
export class SavedConcepts {
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly concepts = input<ConceptStudioResult[]>([]);
  readonly loading = input(false);
  readonly scope = input<ConceptScope>('team');
  readonly canSeeAll = input(false);
  readonly scopeChange = output<ConceptScope>();
  readonly deleteConcept = output<number>();
  readonly loadConcept = output<ConceptStudioResult>();

  readonly search = signal('');
  readonly selected = signal<ConceptStudioResult | null>(null);
  readonly preview = signal<string | null>(null);

  readonly scopes = computed(() => {
    const list: { id: ConceptScope; label: string }[] = [
      { id: 'mine', label: 'cs.saved.scopeMine' },
      { id: 'team', label: 'cs.saved.scopeTeam' },
    ];
    return this.canSeeAll() ? [...list, { id: 'all' as const, label: 'cs.saved.scopeAll' }] : list;
  });

  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const list = this.concepts();
    return q
      ? list.filter((c) => [c.concept_name, c.customer, c.season, c.target_market, c.createdBy].some((v) => (v ?? '').toLowerCase().includes(q)))
      : list;
  });

  protected readonly parseList = parseList;

  images = (c: ConceptStudioResult) => parseList(c.inspiration_images).map(resolveImageUrl).filter(Boolean);
  summary = (c: ConceptStudioResult) => [c.customer, c.season, c.target_market].filter(Boolean).join(' · ');
  creator = (c: ConceptStudioResult) => c.createdBy || c.username || '—';
  creatorGroup = (c: ConceptStudioResult) => c.userGroup || c.user_group || '';

  load(c: ConceptStudioResult): void {
    this.selected.set(null);
    this.loadConcept.emit(c);
  }

  async confirmDelete(c: ConceptStudioResult): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('cs.saved.deleteTitle'),
      message: this.transloco.translate('cs.saved.deleteMessage', { name: c.concept_name }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    if (this.selected()?.recid === c.recid) this.selected.set(null);
    this.deleteConcept.emit(c.recid);
  }
}
