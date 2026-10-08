import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideClipboardCheck, lucideSearch, lucideShieldAlert, lucideSparkles, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { problemMessage } from '../../core/auth/auth.service';
import { StyleLibraryService, StyleListItem, StyleLookups } from '../style-library/style-library.service';
import { AiProviderBanner } from './ai-provider-banner';
import { AiStudioService, BOM_RULES, BomCheck, BomCheckExplanation, BomCheckFinding, BomCheckQuery, BomRule, Severity } from './ai-studio.service';
import { StylePicker } from './style-picker';

type Scope = 'style' | 'group';

const SEVERITY_TONE: Record<Severity, Tone> = { High: 'red', Medium: 'amber', Low: 'neutral' };

/**
 * AI Studio > BOM check: rules flag BOM lines that need a look (missing or outlying consumption, LCO vs brand gaps,
 * changes from the reused style, missing suppliers). The check itself is SQL rules and needs no AI; "Explain with AI"
 * sends the first 80 findings (most severe first) for a plain-language summary and a fix-first list.
 * Address: /ai/bom-check?styleId=.. or ?customer=..&season=.. (both empty = the whole library).
 */
@Component({
  selector: 'app-bom-check',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, NgIcon, AiProviderBanner, StylePicker, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmNativeSelectImports,
    HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideClipboardCheck, lucideSearch, lucideShieldAlert, lucideSparkles, lucideX })],
  templateUrl: './bom-check.html',
})
export class BomCheckPage implements OnInit {
  private readonly svc = inject(AiStudioService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly rules = BOM_RULES;
  readonly severities: Severity[] = ['High', 'Medium', 'Low'];
  readonly severityTone = (s: Severity) => toneBadge(SEVERITY_TONE[s]);

  readonly lookups = signal<StyleLookups | null>(null);
  readonly scope = signal<Scope>('group');
  readonly query = signal<BomCheckQuery>({});
  readonly pickedStyle = signal<string | null>(null);
  readonly result = signal<BomCheck | null>(null);
  readonly loading = signal(false);
  readonly explanation = signal<BomCheckExplanation | null>(null);
  readonly explaining = signal(false);
  readonly ruleFilter = signal<BomRule | ''>('');
  readonly severityFilter = signal<Severity | ''>('');
  /** Search over the findings shown: style, season, customer, material, section and rule. Every word must match. */
  readonly search = signal('');

  /** Lines (and styles) per rule, all severities together. */
  readonly ruleTotals = computed(() => {
    const totals = new Map<BomRule, { lines: number; styles: number; worst: Severity }>();
    for (const r of this.result()?.rules ?? []) {
      const t = totals.get(r.ruleCode);
      if (!t) totals.set(r.ruleCode, { lines: r.lines, styles: r.styles, worst: r.severity });
      else totals.set(r.ruleCode, { lines: t.lines + r.lines, styles: Math.max(t.styles, r.styles), worst: t.worst });
    }
    return BOM_RULES.filter((r) => totals.has(r)).map((r) => ({ rule: r, ...totals.get(r)! }));
  });

  readonly priorityById = computed(() => new Map((this.explanation()?.priorities ?? []).map((p, i) => [p.bomLineId, { ...p, rank: i + 1 }])));

  readonly findings = computed(() => {
    const rule = this.ruleFilter();
    const sev = this.severityFilter();
    const prio = this.priorityById();
    const terms = this.search().trim().toLowerCase().split(/\s+/).filter(Boolean);
    return (this.result()?.findings ?? [])
      .filter((f) => (!rule || f.ruleCode === rule) && (!sev || f.severity === sev))
      .filter((f) => !terms.length || terms.every((t) => this.searchText(f).includes(t)))
      .sort((a, b) => (prio.get(a.bomLineId)?.rank ?? 999) - (prio.get(b.bomLineId)?.rank ?? 999));
  });

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      const styleId = Number(p.get('styleId')) || null;
      const q: BomCheckQuery = styleId ? { styleId } : { customer: p.get('customer'), season: p.get('season') };
      this.scope.set(styleId ? 'style' : 'group');
      this.query.set(q);
      if (p.keys.length) this.run();
      else {
        this.result.set(null);
        this.explanation.set(null);
      }
    });
  }

  setScope(scope: Scope): void {
    this.scope.set(scope);
    this.result.set(null);
    this.explanation.set(null);
  }

  pickStyle(s: StyleListItem): void {
    this.pickedStyle.set(s.styleNo);
    void this.router.navigate([], { queryParams: { styleId: s.styleId } });
  }

  setGroup(key: 'customer' | 'season', value: string): void {
    this.query.update((q) => ({ customer: q.customer ?? null, season: q.season ?? null, [key]: value || null }));
  }

  /** Customer / season scope: the address keeps the choice (an empty "all" flag means the whole library). */
  runGroup(): void {
    const q = this.query();
    void this.router.navigate([], { queryParams: { customer: q.customer || null, season: q.season || null, all: q.customer || q.season ? null : 1 } });
  }

  run(): void {
    this.loading.set(true);
    this.explanation.set(null);
    this.ruleFilter.set('');
    this.severityFilter.set('');
    this.search.set('');
    this.svc.bomCheck(this.query()).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
        if (this.query().styleId && r.findings[0]) this.pickedStyle.set(r.findings[0].styleNo);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  explain(): void {
    if (this.explaining()) return;
    this.explaining.set(true);
    this.svc.explain(this.query()).subscribe({
      next: (x) => {
        this.explanation.set(x);
        this.explaining.set(false);
      },
      error: (e) => {
        this.explaining.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }

  toggleRule(rule: BomRule): void {
    this.ruleFilter.update((r) => (r === rule ? '' : rule));
  }

  noteFor = (rule: BomRule) => this.explanation()?.ruleNotes.find((n) => n.ruleCode === rule)?.note ?? null;

  /** The finding's numbers in words (translation key + parameters). */
  /** Lower-cased text a finding is searched by (the rule by its code and its name in the current language). */
  private searchText(f: BomCheckFinding): string {
    return [
      f.styleNo, f.seasonCode, f.customerCode, f.materialCode, f.materialDescription, f.contentClassCode, f.partNo, f.refStyleNo, f.ruleCode,
      this.transloco.translate('ai.bom.rule.' + f.ruleCode),
    ].filter((v) => v !== null && v !== undefined).join(' ').toLowerCase();
  }

  /** Clears the severity, rule and search filters. */
  clearFilters(): void {
    this.ruleFilter.set('');
    this.severityFilter.set('');
    this.search.set('');
  }

  detail(f: BomCheckFinding): { key: string; params: Record<string, string | number> } {
    const uom = f.uomCode ?? '';
    const n = (v: number | null) => (v === null ? '—' : String(+Number(v).toFixed(4)));
    const pct = (a: number | null, b: number | null) => (a !== null && b ? Math.round(((a - b) / b) * 100) : 0);
    const signed = (p: number) => (p > 0 ? `+${p}` : String(p));
    switch (f.ruleCode) {
      case 'LcoBrandGap':
        return { key: 'ai.bom.detail.LcoBrandGap', params: { lco: n(f.value), brand: n(f.refValue), uom, pct: signed(pct(f.value, f.refValue)) } };
      case 'PeerOutlier':
      case 'MainFabricOutlier':
        return {
          key: `ai.bom.detail.${f.ruleCode}`,
          params: { value: n(f.value), median: n(f.refValue), uom, peers: f.peerCount ?? 0, pct: signed(pct(f.value, f.refValue)) },
        };
      case 'FamilyDrift':
        return { key: 'ai.bom.detail.FamilyDrift', params: { now: n(f.value), before: n(f.refValue), uom, style: f.refStyleNo ?? '', pct: signed(pct(f.value, f.refValue)) } };
      case 'LcoMissing':
        return { key: 'ai.bom.detail.LcoMissing', params: { brand: n(f.refValue), uom } };
      default:
        return { key: `ai.bom.detail.${f.ruleCode}`, params: {} };
    }
  }
}
