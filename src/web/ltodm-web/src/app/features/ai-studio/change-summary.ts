import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideGitCompareArrows, lucideImage, lucideSparkles, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Tone, toneBadge } from '@tms/shared/ui/tones';
import { problemMessage } from '../../core/auth/auth.service';
import { StyleListItem } from '../style-library/style-library.service';
import { AiProviderBanner } from './ai-provider-banner';
import { AiStudioService, BomLineChange, CompareSummary, StyleCompare } from './ai-studio.service';
import { StylePicker } from './style-picker';

type LineFilter = 'all' | BomLineChange['change'];

const CHANGE_TONE: Record<string, Tone> = { Added: 'green', Removed: 'red', Changed: 'amber', Swapped: 'violet', Recoded: 'primary' };

/**
 * AI Studio > Change summary: what changed between a style and the one it was reused from (or any style picked).
 * The differences are worked out by rules and shown in full; the AI only writes the summary, from the differences.
 * Address: /ai/compare?styleId=..&fromStyleId=.. (fromStyleId optional).
 */
@Component({
  selector: 'app-change-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink, NgIcon, AuthImgDirective, AiProviderBanner, StylePicker, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmSkeletonImports,
    HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideArrowRight, lucideGitCompareArrows, lucideImage, lucideSparkles, lucideX })],
  templateUrl: './change-summary.html',
})
export class ChangeSummary implements OnInit {
  private readonly svc = inject(AiStudioService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly toneBadge = toneBadge;
  readonly changeTone = (change: string) => toneBadge(CHANGE_TONE[change] ?? 'neutral');

  readonly styleId = signal<number | null>(null);
  readonly fromStyleId = signal<number | null>(null);
  readonly diff = signal<StyleCompare | null>(null);
  readonly loading = signal(false);
  /** Set when the style was not reused from another and none was picked (or the style was not found). */
  readonly problem = signal<string | null>(null);
  readonly summary = signal<CompareSummary | null>(null);
  readonly summarizing = signal(false);
  readonly lineFilter = signal<LineFilter>('all');

  readonly lineCounts = computed(() => {
    const counts: Record<string, number> = { all: 0, Swapped: 0, Changed: 0, Added: 0, Removed: 0 };
    for (const l of this.diff()?.bomLines ?? []) {
      counts['all']++;
      counts[l.change]++;
    }
    return counts;
  });

  readonly lines = computed(() => {
    const f = this.lineFilter();
    return (this.diff()?.bomLines ?? []).filter((l) => f === 'all' || l.change === f);
  });

  readonly filters: LineFilter[] = ['all', 'Swapped', 'Changed', 'Added', 'Removed'];

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      const id = Number(p.get('styleId')) || null;
      const from = Number(p.get('fromStyleId')) || null;
      const changed = id !== this.styleId() || from !== this.fromStyleId();
      this.styleId.set(id);
      this.fromStyleId.set(from);
      if (changed) this.load();
    });
  }

  pickStyle(s: StyleListItem): void {
    void this.router.navigate([], { queryParams: { styleId: s.styleId, fromStyleId: null }, queryParamsHandling: 'merge' });
  }

  pickFrom(s: StyleListItem): void {
    void this.router.navigate([], { queryParams: { fromStyleId: s.styleId }, queryParamsHandling: 'merge' });
  }

  clear(): void {
    void this.router.navigate([], { queryParams: {} });
  }

  load(): void {
    const id = this.styleId();
    this.diff.set(null);
    this.summary.set(null);
    this.problem.set(null);
    this.lineFilter.set('all');
    if (!id) return;
    this.loading.set(true);
    this.svc.compare(id, this.fromStyleId()).subscribe({
      next: (d) => {
        this.diff.set(d);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        if (e instanceof HttpErrorResponse && (e.status === 400 || e.status === 404)) this.problem.set(problemMessage(e));
        else toast.error(problemMessage(e));
      },
    });
  }

  summarize(): void {
    const d = this.diff();
    if (!d || this.summarizing()) return;
    this.summarizing.set(true);
    this.svc.summarize(d.to.styleId, d.from.styleId).subscribe({
      next: (s) => {
        this.summary.set(s);
        this.summarizing.set(false);
      },
      error: (e) => {
        this.summarizing.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }

  /** Field name -> label key; unknown fields show as they are. */
  fieldLabel = (field: string) => `ai.compare.field.${field}`;
}
