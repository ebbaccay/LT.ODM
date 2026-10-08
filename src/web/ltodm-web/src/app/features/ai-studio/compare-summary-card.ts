import { ChangeDetectionStrategy, Component, inject, input, linkedSignal, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSparkles } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { catchError, map, of } from 'rxjs';
import { problemMessage } from '../../core/auth/auth.service';
import { AiStudioService, CompareSummary, StyleCompare } from './ai-studio.service';

/**
 * The AI's written summary of a style comparison, asked for on demand. Shows nothing at all when no AI text
 * service is set up, so the rule-based comparison around it reads as complete without AI.
 */
@Component({
  selector: 'app-compare-summary-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButtonImports, HlmCardImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideSparkles })],
  host: { class: 'contents' },
  template: `
    @if (textReady()) {
      <section hlmCard class="gap-3 p-5">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideSparkles" />{{ 'ai.compare.summaryTitle' | transloco }}</h2>
          <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="summarizing()" (click)="summarize()">
            @if (summarizing()) {
              <hlm-spinner class="size-4" />
            }
            {{ (summary() ? 'ai.compare.summarizeAgain' : 'ai.compare.summarize') | transloco }}
          </button>
        </div>
        @if (summary(); as s) {
          <p class="font-medium">{{ s.headline }}</p>
          <p class="text-sm">{{ s.summary }}</p>
          @if (s.highlights.length) {
            <ul class="flex flex-col gap-1.5 text-sm">
              @for (h of s.highlights; track $index) {
                <li class="flex items-start gap-2">
                  <span class="bg-muted text-muted-foreground shrink-0 rounded px-1.5 py-0.5 text-[11px] uppercase">{{ 'ai.compare.area.' + h.area | transloco }}</span>
                  <span>{{ h.text }}</span>
                </li>
              }
            </ul>
          }
          @if (s.checks.length) {
            <div class="rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-sm">
              <p class="mb-1 font-medium">{{ 'ai.compare.checks' | transloco }}</p>
              <ul class="list-disc pl-5">
                @for (c of s.checks; track $index) {
                  <li>{{ c }}</li>
                }
              </ul>
            </div>
          }
          <p class="text-muted-foreground text-xs">{{ 'ai.compare.aiNote' | transloco }}</p>
        } @else {
          <p class="text-muted-foreground text-sm">{{ 'ai.compare.summaryHint' | transloco }}</p>
        }
      </section>
    }
  `,
})
export class CompareSummaryCard {
  private readonly svc = inject(AiStudioService);
  private readonly transloco = inject(TranslocoService);

  readonly diff = input.required<StyleCompare>();

  /** True once the server says an AI text service is set up (hidden while unknown or on error). */
  readonly textReady = toSignal(this.svc.status().pipe(map((s) => s.text.isConfigured), catchError(() => of(false))), { initialValue: false });

  /** Cleared whenever a new pair of styles comes in. */
  readonly summary = linkedSignal<StyleCompare, CompareSummary | null>({ source: this.diff, computation: () => null });
  readonly summarizing = signal(false);

  summarize(): void {
    const d = this.diff();
    if (this.summarizing()) return;
    this.summarizing.set(true);
    this.svc.summarize(d.to.styleId, d.from.styleId).subscribe({
      next: (s) => {
        // Ignore an answer for a pair that is no longer shown.
        if (this.diff() === d) this.summary.set(s);
        this.summarizing.set(false);
      },
      error: (e) => {
        this.summarizing.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }
}
