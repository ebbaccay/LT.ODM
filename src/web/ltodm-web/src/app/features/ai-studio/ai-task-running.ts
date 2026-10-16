import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMinimize2, lucideX } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AiTask, AiTasksSession, taskStatus } from './ai-tasks.session';

/**
 * Shown on a page while its slow AI job runs: how far it has got (words written for text, an estimated bar for
 * renders, else the minutes so far), and Minimise (leave the page, the job carries on in the corner tray) or Cancel.
 * Nothing when the task is not running.
 */
@Component({
  selector: 'app-ai-task-running',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButtonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideMinimize2, lucideX })],
  template: `
    @if (status(); as s) {
      <div class="bg-muted/60 flex flex-col gap-2 rounded-md border px-3 py-2 text-sm" role="status">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
          <hlm-spinner class="size-4 shrink-0" />
          <p class="min-w-0 flex-1">
            <span class="font-medium">{{ s.key | transloco: s.params }}</span>
            <span class="text-muted-foreground block text-xs">{{ 'ai.tasks.minimizeHint' | transloco }}</span>
          </p>
          <div class="flex gap-2">
            <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="tasks.minimize()">
              <ng-icon name="lucideMinimize2" />{{ 'ai.tasks.minimize' | transloco }}
            </button>
            <button hlmBtn variant="ghost" type="button" class="h-11 lg:h-9" (click)="tasks.cancel(task()!.id)">
              <ng-icon name="lucideX" />{{ 'ai.tasks.cancel' | transloco }}
            </button>
          </div>
        </div>
        @if (s.percent !== null) {
          <div
            class="bg-background h-2 overflow-hidden rounded-full"
            role="progressbar"
            [attr.aria-valuenow]="s.percent"
            aria-valuemin="0"
            aria-valuemax="100"
            [attr.aria-label]="'ai.tasks.estimate' | transloco"
          >
            <div class="bg-primary h-full rounded-full transition-[width] duration-1000" [style.width.%]="s.percent"></div>
          </div>
          <p class="text-muted-foreground text-xs">{{ 'ai.tasks.estimateHint' | transloco }}</p>
        }
      </div>
    }
  `,
})
export class AiTaskRunning {
  readonly tasks = inject(AiTasksSession);
  readonly task = input<AiTask | null>(null);

  /** Ticks every 5 s so the bar and the minutes move on. */
  private readonly now = signal(Date.now());
  readonly status = computed(() => {
    const t = this.task();
    return t?.status === 'running' ? taskStatus(t, this.now()) : null;
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 5_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}
