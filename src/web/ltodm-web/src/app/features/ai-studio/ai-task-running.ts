import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMinimize2, lucideX } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AiTask, AiTasksSession } from './ai-tasks.session';

/**
 * Shown on a page while its slow AI job runs: minutes so far, and Minimise (leave the page, the job carries on in the
 * corner tray) or Cancel. Nothing when the task is not running.
 */
@Component({
  selector: 'app-ai-task-running',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButtonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideMinimize2, lucideX })],
  template: `
    @if (task()?.status === 'running') {
      <div class="bg-muted/60 flex flex-wrap items-center gap-x-3 gap-y-2 rounded-md border px-3 py-2 text-sm" role="status">
        <hlm-spinner class="size-4 shrink-0" />
        <p class="min-w-0 flex-1">
          <span class="font-medium">{{ 'ai.tasks.working' | transloco: { count: minutes() } }}</span>
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
    }
  `,
})
export class AiTaskRunning {
  readonly tasks = inject(AiTasksSession);
  readonly task = input<AiTask | null>(null);

  private readonly now = signal(Date.now());
  readonly minutes = computed(() => {
    const t = this.task();
    return t ? Math.max(1, Math.round((this.now() - t.startedAt) / 60_000)) : 0;
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 15_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}
