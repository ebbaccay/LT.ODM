import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGitCompareArrows, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { problemMessage } from '../../core/auth/auth.service';
import { StyleDiff } from '../style-library/style-diff';
import { StyleListItem } from '../style-library/style-library.service';
import { AiProviderBanner } from './ai-provider-banner';
import { AiStudioService, StyleCompare } from './ai-studio.service';
import { CompareSummaryCard } from './compare-summary-card';
import { StylePicker } from './style-picker';

/**
 * AI Studio > Change summary: what changed between a style and the one it was reused from (or any style picked).
 * The differences are worked out by rules and shown in full (StyleDiff, also on the Style Library compare page); the AI
 * only writes the summary, from the differences, when an AI text service is set up.
 * Address: /ai/compare?styleId=..&fromStyleId=.. (fromStyleId optional).
 */
@Component({
  selector: 'app-change-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, AiProviderBanner, StylePicker, StyleDiff, CompareSummaryCard, HlmButtonImports, HlmCardImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideGitCompareArrows, lucideX })],
  templateUrl: './change-summary.html',
})
export class ChangeSummary implements OnInit {
  private readonly svc = inject(AiStudioService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly styleId = signal<number | null>(null);
  readonly fromStyleId = signal<number | null>(null);
  readonly diff = signal<StyleCompare | null>(null);
  readonly loading = signal(false);
  /** Set when the style was not reused from another and none was picked (or the style was not found). */
  readonly problem = signal<string | null>(null);

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
    this.problem.set(null);
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
}
