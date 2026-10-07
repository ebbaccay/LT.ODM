import {
  Component, ChangeDetectionStrategy, OnInit, OnDestroy,
  signal, computed, inject, NgZone,
} from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { Tone, toneBadge, toneBorder, toneColor, toneDot, toneText } from '@tms/shared/ui/tones';

import { environment } from '@env/environment';
import { StoredProcSignalRService } from '@services/spHub.service';
import { UserInfoService } from '@services/user-info.service';

import { STATUS_CFG, UserRole, QuotationStatus } from '../marketing/garment-quotation/garment-quotation.model';
import {
  DashStatusCount, DashActionItem, DashKpis, DashActivity,
  ActionGroup, ActionKind, actionGroupsFor,
} from './dashboard.model';

@Component({
  selector: 'app-dashboard',
  imports: [TranslocoPipe, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmNativeSelectImports, HlmSkeletonImports],
  templateUrl: './dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Dashboard implements OnInit, OnDestroy {

  private readonly _sp       = inject(StoredProcSignalRService);
  private readonly _userInfo = inject(UserInfoService);
  private readonly _router   = inject(Router);
  private readonly _zone     = inject(NgZone);

  private readonly _spName = `${environment.appDb}.dbo.web_rd_gq_dashboard_summary`;

  readonly STATUS_CFG = STATUS_CFG;

  // Tailwind colour helpers (tms/shared/ui/tones.ts)
  protected readonly toneBadge = toneBadge;
  protected readonly toneDot = toneDot;
  protected readonly toneText = toneText;
  protected readonly toneBorder = toneBorder;
  /** Action-group tones from dashboard.model -> colour family. */
  protected readonly groupTone: Record<ActionGroup['tone'], Tone> = { info: 'primary', warn: 'amber', success: 'green', danger: 'red' };
  readonly STATUS_ORDER: QuotationStatus[] =
    ['new', 'sent_to_factory', 'factory_submitted', 'for_revision', 'approved', 'rejected'];

  readonly role          = signal<UserRole>('merchandiser');
  readonly factoryUserId = signal<string>('');
  readonly userName      = signal<string>('');

  readonly isLoading        = signal(true);
  readonly statusCounts     = signal<DashStatusCount[]>([]);
  readonly actionItems      = signal<DashActionItem[]>([]);
  readonly kpis             = signal<DashKpis | null>(null);
  readonly recentActivity   = signal<DashActivity[]>([]);
  readonly availableSeasons = signal<string[]>([]);
  readonly selectedSeason   = signal<string>('');

  readonly statusCountMap = computed(() => {
    const m = new Map<QuotationStatus, number>();
    for (const r of this.statusCounts()) m.set(r.status, Number(r.cnt) || 0);
    return m;
  });

  readonly totalQuotations = computed(() =>
    this.statusCounts().reduce((s, r) => s + (Number(r.cnt) || 0), 0)
  );

  readonly donutSegments = computed(() => {
    const total = this.totalQuotations();
    if (!total) return [] as { status: QuotationStatus; pct: number; count: number; cfg: typeof STATUS_CFG[string] }[];
    return this.STATUS_ORDER
      .map(s => ({
        status: s,
        count:  this.statusCountMap().get(s) ?? 0,
        cfg:    STATUS_CFG[s],
      }))
      .filter(seg => seg.count > 0)
      .map(seg => ({ ...seg, pct: (seg.count / total) * 100 }));
  });

  readonly donutGradient = computed(() => {
    const segs = this.donutSegments();
    if (!segs.length) return 'conic-gradient(var(--muted) 0 100%)';
    const stops: string[] = [];
    let acc = 0;
    for (const seg of segs) {
      const start = acc;
      acc += seg.pct;
      stops.push(`${toneColor(seg.cfg.tone)} ${start}% ${acc}%`);
    }
    return `conic-gradient(${stops.join(', ')})`;
  });

  readonly actionGroups = computed<ActionGroup[]>(() => {
    const items = this.actionItems();
    return actionGroupsFor(this.role()).map(g => ({
      ...g,
      items: items.filter(i => i.action_kind === g.key),
    }));
  });

  readonly totalActionsCount = computed(() =>
    this.actionGroups().reduce((s, g) => s + g.items.length, 0)
  );

  readonly winRateLabel = computed(() => {
    const k = this.kpis();
    if (!k || (k.approved_count + k.rejected_count) === 0) return '—';
    return `${(Number(k.win_rate_pct) || 0).toFixed(1)}%`;
  });

  readonly avgSubmitLabel = computed(() => {
    const v = this.kpis()?.avg_submit_days;
    if (v == null) return '—';
    return `${Number(v).toFixed(1)} d`;
  });

  readonly approvedValueLabel = computed(() => {
    const v = this.kpis()?.approved_value_mtd ?? 0;
    return `$${Number(v).toLocaleString('en-US', { maximumFractionDigits: 0 })}`;
  });

  async ngOnInit(): Promise<void> {
    const u = this._userInfo.user;
    this.userName.set(u?.firstName ?? '');
    if (u?.userGroup === 'FTY') {
      this.role.set('factory');
      this.factoryUserId.set(u.location ?? '');
    } else {
      this.role.set('merchandiser');
    }

    await this._sp.ensureConnected();
    this._registerListeners();
    this._fetch();
  }

  ngOnDestroy(): void {
    this._sp.hubConn?.off('StoredProcResultFlat');
  }

  refresh(): void {
    this._fetch();
  }

  onSeasonChange(season: string): void {
    this.selectedSeason.set(season);
    this._fetch();
  }

  goToQuotations(): void {
    this._router.navigate(['/garment-quotation']);
  }

  openItem(item: DashActionItem): void {
    let tab: string;
    if (this.role() === 'factory') {
      tab = 'costing';
    } else if (item.action_kind === 'for_quotation' || item.action_kind === 'tms_approved') {
      tab = 'bom';
    } else if (item.action_kind === 'review') {
      tab = 'costing';
    }
     else {
      tab = 'compare';
    }
    this._router.navigate(['/garment-quotation'], {
      queryParams: {
        pkg:       item.pack_name,
        styleId:   item.style_id,
        factoryId: item.factory_id,
        tab,
      },
    });
  }

  openByStatus(status: QuotationStatus): void {
    this._router.navigate(['/garment-quotation'], { queryParams: { status } });
  }

  fmtAge(days: number): string {
    const d = Number(days) || 0;
    if (d <= 0) return 'today';
    if (d === 1) return '1 day';
    return `${d} days`;
  }

  fmtDate(s: string): string {
    if (!s) return '';
    const d = new Date(s);
    if (isNaN(d.getTime())) return s;
    return d.toLocaleString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  trackKind = (_: number, g: ActionGroup) => g.key;
  trackQid  = (_: number, i: DashActionItem) => i.qid;
  trackHist = (_: number, a: DashActivity) => a.history_id;
  trackStatus = (_: number, s: QuotationStatus) => s;

  // ── Internal ───────────────────────────────────────────────────────────────
  private _fetch(): void {
    this.isLoading.set(true);
    this._sp.invoke('ExecuteStoredProcFlat', {
      SpName: this._spName,
      Parameters: {
        role:       this.role(),
        factory_id: this.factoryUserId(),
        stale_days: 3,
        season_id:  this.selectedSeason() || null,
      },
    });
  }

  private _registerListeners(): void {
    this._sp.hubConn?.off('StoredProcResultFlat');
    this._sp.hubConn?.on('StoredProcResultFlat', (res: any) => {
      if (res?.procedure !== this._spName) return;
      const data = this._mergeToJson(res);
      this._zone.run(() => this._handle(data));
    });
  }

  private _handle(data: Record<string, any[]>): void {
    this.isLoading.set(false);

    this.statusCounts.set((data['table0'] ?? []).map((r: any) => ({
      status: r.status as QuotationStatus,
      cnt:    Number(r.cnt) || 0,
    })));

    this.actionItems.set((data['table1'] ?? []).map((r: any): DashActionItem => ({
      qid:          String(r.qid ?? ''),
      pack_name:    String(r.pack_name ?? ''),
      style_id:     String(r.style_id ?? ''),
      customer_id:  String(r.customer_id ?? ''),
      season_id:    String(r.season_id ?? ''),
      factory_id:   String(r.factory_id ?? ''),
      factory_name: String(r.factory_name ?? ''),
      status:       r.status as QuotationStatus,
      fob_price:    r.fob_price != null ? String(r.fob_price) : null,
      price_type:   r.price_type != null ? String(r.price_type) : null,
      updated_at:   String(r.updated_at ?? ''),
      days_old:     Number(r.days_old) || 0,
      action_kind:  (r.action_kind as ActionKind) || 'other',
    })));

    const kRow = (data['table2'] ?? [])[0];
    this.kpis.set(kRow ? {
      total_active:       Number(kRow.total_active)       || 0,
      approved_count:     Number(kRow.approved_count)     || 0,
      rejected_count:     Number(kRow.rejected_count)     || 0,
      win_rate_pct:       Number(kRow.win_rate_pct)       || 0,
      avg_submit_days:    kRow.avg_submit_days != null ? Number(kRow.avg_submit_days) : null,
      approved_value_mtd: Number(kRow.approved_value_mtd) || 0,
    } : null);

    this.recentActivity.set((data['table3'] ?? []).map((r: any): DashActivity => ({
      history_id:   Number(r.history_id) || 0,
      qid:          String(r.qid ?? ''),
      pack_name:    r.pack_name != null ? String(r.pack_name) : null,
      style_id:     String(r.style_id ?? ''),
      factory_id:   String(r.factory_id ?? ''),
      factory_name: String(r.factory_name ?? ''),
      status:       r.status as QuotationStatus,
      changed_by:   String(r.changed_by ?? ''),
      changed_at:   String(r.changed_at ?? ''),
    })));

    const seasons = (data['table4'] ?? [])
      .map((r: any) => String(r.season_id ?? ''))
      .filter((s: string) => s.length > 0);
    if (seasons.length > 0) this.availableSeasons.set(seasons);
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
