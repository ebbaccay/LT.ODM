import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideAward, lucideFactory, lucideImage, lucideInfo, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { StoredProcSignalRService } from '@services/spHub.service';
import { FlatResult, procName, spError, spTables } from '@tms/shared/sp-result';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { toneBadge } from '@tms/shared/ui/tones';
import { resolveImageUrl } from '../../marketing/concept-studio/concept-studio.model';
import { FactoryScore, QuoteStatus, SBU_PERF_SP as SP, STATUS_TONE, SortKey, countrySummaries, scoreRows } from './sbu-performance.model';

/**
 * SBU performance (TMS sbu-performance): factories and SBU offices ranked by price, lead time and quotation
 * approval rate. Office roles only (the API refuses the procedure to others: it shows every factory's prices).
 *
 * The score is a fixed weighted formula (TMS called it "AI score"); TMS's "Key focus" toggles changed nothing
 * and are not ported.
 */
@Component({
  selector: 'app-sbu-performance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, NgTemplateOutlet, TranslocoPipe, NgIcon, Modal, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports,
    HlmNativeSelectImports, HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideAward, lucideFactory, lucideImage, lucideInfo, lucideRefreshCw, lucideSearch })],
  templateUrl: './sbu-performance.html',
})
export class SbuPerformance implements OnInit, OnDestroy {
  private readonly sp = inject(StoredProcSignalRService);
  private readonly zone = inject(NgZone);
  private readonly transloco = inject(TranslocoService);

  protected readonly resolveImageUrl = resolveImageUrl;
  protected readonly sortKeys: SortKey[] = ['overall', 'cost', 'delivery', 'quality', 'products'];
  protected readonly statuses: QuoteStatus[] = ['Approved', 'Pending', 'Draft'];

  readonly rows = signal<FactoryScore[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);

  readonly search = signal('');
  readonly country = signal('');
  readonly status = signal<'' | 'catalog' | QuoteStatus>('');
  readonly sort = signal<SortKey>('overall');
  readonly detail = signal<FactoryScore | null>(null);

  readonly countries = computed(() => [...new Set(this.rows().map((r) => r.country).filter((c) => !!c))].sort());
  readonly filtered = computed(() => {
    const q = this.search().toLowerCase().trim();
    const c = this.country();
    const st = this.status();
    const key = this.sort();
    const value = (r: FactoryScore) => (key === 'overall' ? (r.overall ?? -1) : key === 'products' ? r.products : (r[key] ?? -1));
    return this.rows()
      .filter(
        (r) =>
          (!q || r.name.toLowerCase().includes(q) || r.country.toLowerCase().includes(q)) &&
          (!c || r.country === c) &&
          (!st || (st === 'catalog' ? r.source === 'catalog' : r.status === st)),
      )
      .sort((a, b) => value(b) - value(a) || (b.overall ?? -1) - (a.overall ?? -1) || a.name.localeCompare(b.name));
  });
  readonly top = computed(() => [...this.filtered()].filter((r) => r.overall !== null).sort((a, b) => b.overall! - a.overall!)[0] ?? null);
  readonly summaries = computed(() => countrySummaries(this.filtered()));
  readonly hasFilters = computed(() => !!(this.search() || this.country() || this.status()));

  async ngOnInit(): Promise<void> {
    await this.sp.ensureConnected();
    this.sp.hubConn?.off('StoredProcResultFlat');
    this.sp.hubConn?.on('StoredProcResultFlat', (res: FlatResult) => this.zone.run(() => this.onResult(res)));
    this.refresh();
  }

  ngOnDestroy(): void {
    this.sp.hubConn?.off('StoredProcResultFlat');
  }

  refresh(): void {
    this.loading.set(true);
    this.failed.set(false);
    void this.sp.invoke('ExecuteStoredProcFlat', { SpName: SP.READ });
  }

  private onResult(res: FlatResult): void {
    if (procName(res.procedure) !== SP.READ) return;
    this.loading.set(false);
    const error = spError(res);
    if (error) {
      this.failed.set(true);
      toast.error(this.transloco.translate('sp.loadFailed'), { description: error });
      return;
    }
    this.rows.set(scoreRows(spTables(res)['table0'] ?? []));
  }

  clearFilters(): void {
    this.search.set('');
    this.country.set('');
    this.status.set('');
  }

  statusClass(status: QuoteStatus): string {
    return toneBadge(STATUS_TONE[status]);
  }

  /** Width of a 6–10 score bar (6 = a sliver, 10 = full). */
  barWidth(score: number | null): string {
    return score === null ? '0%' : `${Math.max(4, ((score - 5) / 5) * 100)}%`;
  }
}
