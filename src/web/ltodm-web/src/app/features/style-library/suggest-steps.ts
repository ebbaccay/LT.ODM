import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck } from '@ng-icons/lucide';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';

/**
 * The steps a suggestion request works through, shown while it runs (Suggest BOM, Suggest colours). The request is one
 * call that reports no progress, so the steps advance on a timer set from measured times and the last one stays
 * (spinning) until the answer comes; no percentage is shown. Show it only while loading: it starts when shown.
 */
@Component({
  selector: 'app-suggest-steps',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCheck })],
  template: `
    <ol class="bg-muted/30 flex flex-col gap-2.5 rounded-md border p-4 text-sm" aria-live="polite">
      @for (st of steps(); track st; let i = $index) {
        <li class="flex items-center gap-2.5" [class.text-muted-foreground]="i > current()">
          <span class="flex size-5 shrink-0 items-center justify-center">
            @if (i < current()) {
              <ng-icon name="lucideCheck" class="text-emerald-600 dark:text-emerald-400" />
            } @else if (i === current()) {
              <hlm-spinner class="size-4" />
            } @else {
              <span class="bg-muted-foreground/40 size-1.5 rounded-full"></span>
            }
          </span>
          {{ labelPrefix() + st | transloco }}
        </li>
      }
    </ol>
  `,
})
export class SuggestSteps {
  /** Step keys, in order; each label is labelPrefix + key. */
  readonly steps = input.required<readonly string[]>();
  readonly labelPrefix = input.required<string>();
  /** Time per step (the last one stays). */
  readonly stepMs = input(500);

  readonly current = signal(0);

  constructor() {
    // started on the next tick, when the inputs are set
    let timer: ReturnType<typeof setInterval> | undefined;
    const start = setTimeout(() => {
      timer = setInterval(() => this.current.update((i) => Math.min(i + 1, this.steps().length - 1)), this.stepMs());
    });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(start);
      if (timer !== undefined) clearInterval(timer);
    });
  }
}
