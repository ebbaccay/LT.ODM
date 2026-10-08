import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideArrowLeftRight, lucideGitCompareArrows } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { combineLatest } from 'rxjs';
import { problemMessage } from '../../core/auth/auth.service';
import { AiStudioService, StyleCompare } from '../ai-studio/ai-studio.service';
import { CompareSummaryCard } from '../ai-studio/compare-summary-card';
import { StylePicker } from '../ai-studio/style-picker';
import { StyleDiff } from './style-diff';
import { FamilyMember, StyleLibraryService, StyleListItem } from './style-library.service';

/**
 * Style Library > compare: what changed between a style and an earlier one (by default the style it was reused from),
 * opened from the style's History tab. The differences come from rules on the server, so this works without AI; the
 * AI summary card shows only when an AI text service is set up.
 * Address: /styles/:id/compare?from=.. (from optional: the style it was reused from).
 */
@Component({
  selector: 'app-style-compare',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet, RouterLink, NgIcon, StyleDiff, StylePicker, CompareSummaryCard, HlmButtonImports, HlmCardImports, HlmNativeSelectImports, HlmSkeletonImports,
    TranslocoPipe,
  ],
  providers: [provideIcons({ lucideArrowLeft, lucideArrowLeftRight, lucideGitCompareArrows })],
  templateUrl: './style-compare.html',
})
export class StyleComparePage implements OnInit {
  private readonly ai = inject(AiStudioService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly styleId = signal(0);
  readonly fromStyleId = signal<number | null>(null);
  readonly diff = signal<StyleCompare | null>(null);
  readonly loading = signal(true);
  /** Set when the style was not reused from another and none was picked (or a style was not found). */
  readonly problem = signal<string | null>(null);
  /** The style's family (from its page data), offered as quick picks. */
  readonly family = signal<FamilyMember[]>([]);
  readonly styleNo = signal('');

  /** Family members other than this style, oldest season first. */
  readonly others = computed(() => this.family().filter((m) => m.styleId !== this.styleId()));

  ngOnInit(): void {
    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([p, q]) => {
        const id = Number(p.get('id'));
        const from = Number(q.get('from')) || null;
        const styleChanged = id !== this.styleId();
        if (!styleChanged && from === this.fromStyleId()) return;
        this.styleId.set(id);
        this.fromStyleId.set(from);
        if (styleChanged) this.loadFamily();
        this.load();
      });
  }

  private loadFamily(): void {
    this.family.set([]);
    this.styleNo.set('');
    this.styles.get(this.styleId()).subscribe({
      next: (d) => {
        this.family.set(d.family);
        this.styleNo.set(d.style.styleNo);
      },
      error: () => undefined,
    });
  }

  load(): void {
    this.diff.set(null);
    this.problem.set(null);
    this.loading.set(true);
    this.ai.compare(this.styleId(), this.fromStyleId()).subscribe({
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

  compareWith(fromStyleId: number | null): void {
    if (!fromStyleId || fromStyleId === this.styleId()) return;
    void this.router.navigate([], { relativeTo: this.route, queryParams: { from: fromStyleId } });
  }

  pickOther(s: StyleListItem): void {
    this.compareWith(s.styleId);
  }

  /** Look at the change the other way round (this style becomes the earlier one). */
  swap(): void {
    const d = this.diff();
    if (d) void this.router.navigate(['/styles', d.from.styleId, 'compare'], { queryParams: { from: d.to.styleId } });
  }

  /** "Reused from" tag for the quick picks. */
  isParent = (m: FamilyMember) => this.family().find((f) => f.styleId === this.styleId())?.sourceStyleId === m.styleId;
}
