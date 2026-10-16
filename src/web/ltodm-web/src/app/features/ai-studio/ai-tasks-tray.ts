import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideCircleCheck, lucideX } from '@ng-icons/lucide';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AiTask, AiTasksSession, taskStatus } from './ai-tasks.session';
import { LibraryAssistantSession } from './library-assistant.session';

/**
 * Slow AI jobs away from their pages (renders, BOM check explanations, change summaries): one chip each in the corner,
 * next to the Library assistant bubble, with a spinner and the minutes so far while it runs, or ready / failed.
 * Clicking a chip opens its page with the answer; X cancels a running job or clears a finished one. Mounted in the layout.
 */
@Component({
  selector: 'app-ai-tasks-tray',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCircleAlert, lucideCircleCheck, lucideX })],
  host: { class: 'print:hidden' },
  template: `
    @if (tasks.pending().length) {
      <ul
        class="fixed z-40 flex max-w-[calc(100vw-2rem)] flex-col items-end gap-2"
        [class]="bubbleShown() ? 'right-4 bottom-20 sm:right-20 sm:bottom-4' : 'right-4 bottom-4'"
        role="list"
        aria-live="polite"
        [attr.aria-label]="'ai.tasks.title' | transloco"
      >
        @for (t of tasks.pending(); track t.id) {
          <li class="bg-background flex max-w-80 items-center gap-1 rounded-full border py-1 pr-1 pl-3 text-sm shadow-lg">
            <button type="button" class="flex min-h-9 min-w-0 flex-1 items-center gap-2 text-left focus-visible:outline-none focus-visible:underline" [title]="'ai.tasks.open' | transloco" (click)="tasks.open(t)">
              @switch (t.status) {
                @case ('running') {
                  <hlm-spinner class="size-4 shrink-0" />
                }
                @case ('done') {
                  <ng-icon name="lucideCircleCheck" class="shrink-0 text-green-600" />
                }
                @default {
                  <ng-icon name="lucideCircleAlert" class="text-destructive shrink-0" />
                }
              }
              <span class="min-w-0 truncate">{{ 'ai.tasks.kind.' + t.kind | transloco: { label: t.label } }}</span>
              <span class="text-muted-foreground shrink-0 text-xs">
                @switch (t.status) {
                  @case ('running') {
                    {{ short(t) }}
                  }
                  @case ('done') {
                    {{ 'ai.tasks.ready' | transloco }}
                  }
                  @default {
                    {{ 'ai.tasks.failed' | transloco }}
                  }
                }
              </span>
            </button>
            <button
              type="button"
              class="hover:bg-muted flex size-9 shrink-0 items-center justify-center rounded-full focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
              [title]="(t.status === 'running' ? 'ai.tasks.cancel' : 'ai.tasks.dismiss') | transloco"
              [attr.aria-label]="(t.status === 'running' ? 'ai.tasks.cancel' : 'ai.tasks.dismiss') | transloco"
              (click)="tasks.dismiss(t.id)"
            >
              <ng-icon name="lucideX" />
            </button>
          </li>
        }
      </ul>
    }
  `,
})
export class AiTasksTray {
  readonly tasks = inject(AiTasksSession);
  private readonly assistant = inject(LibraryAssistantSession);
  private readonly transloco = inject(TranslocoService);

  /** The assistant's bubble takes the corner: the chips sit beside it (above it on phones). */
  readonly bubbleShown = computed(() => !this.assistant.onPage() && this.assistant.turns().length > 0);

  /** Ticks every 5 s so the figures shown move on. */
  private readonly now = signal(Date.now());

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 5_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  /** Chip text while running: the estimated %, the words written, or the minutes so far. */
  short = (t: AiTask): string => {
    const s = taskStatus(t, this.now());
    if (s.percent !== null) return `${s.percent}%`;
    if (t.progress?.phase === 'writing') return this.transloco.translate('ai.tasks.words', { count: t.progress.words });
    return this.transloco.translate('ai.tasks.minutes', { count: Math.max(1, Math.round((this.now() - t.startedAt) / 60_000)) });
  };
}
