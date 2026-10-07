import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX } from '@ng-icons/lucide';

let nextId = 0;

/**
 * Form dialog for ported screens (replaces ui5-dialog). Full screen on phones, centred card from sm up.
 *
 *   <app-modal [open]="dialogOpen()" heading="Edit role" (closed)="dialogOpen.set(false)">
 *     ...fields...
 *     <ng-container footer> <button hlmBtn>Save</button> </ng-container>
 *   </app-modal>
 */
@Component({
  selector: 'app-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideX })],
  template: `
    @if (open()) {
      <div
        class="fixed inset-0 z-[1500] flex items-end justify-center bg-black/45 backdrop-blur-[2px] sm:items-center sm:p-5"
        (click)="closed.emit()"
      >
        <div
          #panel
          class="bg-card text-card-foreground ring-foreground/10 animate-in fade-in-0 zoom-in-95 flex max-h-[100dvh] w-full flex-col rounded-t-xl shadow-2xl ring-1 sm:max-h-[90dvh] sm:rounded-xl"
          [class]="size() === 'lg' ? 'sm:max-w-2xl' : 'sm:max-w-lg'"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="titleId"
          tabindex="-1"
          (click)="$event.stopPropagation()"
          (keydown.escape)="closed.emit()"
        >
          <div class="flex items-center justify-between gap-3 border-b px-5 py-3">
            <h2 [id]="titleId" class="truncate text-base font-semibold">{{ heading() }}</h2>
            <button
              type="button"
              class="text-muted-foreground hover:bg-accent hover:text-foreground -mr-2 flex size-11 shrink-0 items-center justify-center rounded-md lg:size-9"
              [attr.aria-label]="'actions.close' | transloco"
              (click)="closed.emit()"
            >
              <ng-icon name="lucideX" class="text-lg" />
            </button>
          </div>
          <div class="min-h-0 flex-1 overflow-y-auto px-5 py-4">
            <ng-content />
          </div>
          <div class="flex flex-wrap justify-end gap-2 border-t px-5 py-3">
            <ng-content select="[footer]" />
          </div>
        </div>
      </div>
    }
  `,
})
export class Modal {
  readonly open = input(false);
  readonly heading = input('');
  readonly size = input<'md' | 'lg'>('md');
  readonly closed = output<void>();

  protected readonly titleId = `modal-title-${nextId++}`;
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');

  constructor() {
    // Focus the first field when the dialog opens.
    effect(() => {
      const panel = this.panel()?.nativeElement;
      if (!panel) return;
      queueMicrotask(() => {
        const first = panel.querySelector<HTMLElement>('input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button[role=checkbox]:not([disabled])');
        (first ?? panel).focus();
      });
    });
  }
}
