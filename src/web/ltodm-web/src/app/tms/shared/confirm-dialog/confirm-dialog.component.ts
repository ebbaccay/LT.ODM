import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideInfo, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ConfirmDialogService } from './confirm-dialog.service';

/** Renders ConfirmDialogService requests (ported from TMS, restyled with the LT ODM theme). Mounted once in App. */
@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButtonImports, TranslocoPipe],
  providers: [provideIcons({ lucideInfo, lucideTriangleAlert })],
  template: `
    @if (dlg.state(); as s) {
      <div class="fixed inset-0 z-[2000] flex items-center justify-center bg-black/45 p-5 backdrop-blur-[2px]" (click)="dlg.respond(false)">
        <div
          #dialog
          class="bg-card text-card-foreground ring-foreground/10 animate-in fade-in-0 zoom-in-95 flex w-full max-w-md flex-col gap-4 rounded-xl p-6 shadow-2xl ring-1"
          role="alertdialog"
          aria-modal="true"
          [attr.aria-labelledby]="'cdlg-title'"
          [attr.aria-describedby]="'cdlg-msg'"
          tabindex="-1"
          (click)="$event.stopPropagation()"
          (keydown.escape)="dlg.respond(false)"
        >
          <div class="flex gap-4">
            <span class="flex size-11 shrink-0 items-center justify-center rounded-full" [class]="iconClass(s.variant)">
              <ng-icon [name]="s.variant === 'danger' || s.variant === 'warning' ? 'lucideTriangleAlert' : 'lucideInfo'" class="text-xl" />
            </span>
            <div class="min-w-0">
              <h2 id="cdlg-title" class="text-base font-semibold">{{ s.title }}</h2>
              <p id="cdlg-msg" class="text-muted-foreground mt-1 text-sm leading-relaxed">{{ s.message }}</p>
            </div>
          </div>
          <div class="flex justify-end gap-2 pt-1">
            <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="dlg.respond(false)">{{ s.cancelLabel ?? ('actions.cancel' | transloco) }}</button>
            <button
              #confirm
              hlmBtn
              type="button"
              class="h-11 lg:h-9"
              [class]="confirmClass(s.variant)"
              (click)="dlg.respond(true)"
            >
              {{ s.confirmLabel ?? ('actions.confirm' | transloco) }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConfirmDialogComponent {
  protected readonly dlg = inject(ConfirmDialogService);
  private readonly confirm = viewChild<ElementRef<HTMLButtonElement>>('confirm');

  constructor() {
    // Focus the confirm button when a dialog opens (keyboard users can answer straight away).
    effect(() => {
      if (this.dlg.state()) queueMicrotask(() => this.confirm()?.nativeElement.focus());
    });
  }

  protected iconClass(variant?: string): string {
    switch (variant) {
      case 'danger':
        return 'bg-destructive/15 text-destructive';
      case 'warning':
        return 'bg-amber-500/15 text-amber-600 dark:text-amber-400';
      default:
        return 'bg-primary/15 text-primary';
    }
  }

  protected confirmClass(variant?: string): string {
    switch (variant) {
      case 'danger':
        return 'bg-destructive text-white hover:bg-destructive/90';
      case 'warning':
        return 'bg-amber-600 text-white hover:bg-amber-600/90';
      default:
        return '';
    }
  }
}
