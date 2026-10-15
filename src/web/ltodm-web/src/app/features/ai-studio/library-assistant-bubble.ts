import { ChangeDetectionStrategy, Component, ElementRef, afterRenderEffect, inject, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMaximize2, lucideMessagesSquare, lucideMinus, lucideSend, lucideX } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { LibraryAssistantSession } from './library-assistant.session';
import { LibraryTurn } from './library-turn';

/**
 * The Library assistant conversation away from its page, like a messenger chat head: a bubble in the corner (spinner
 * while a question runs, a count of answers that arrived meanwhile) that opens a small chat window over any page.
 * Minimise folds it back into the bubble, Maximise opens the full page, Close ends the conversation (and stops a
 * running question). Shown only while there is a conversation and the assistant page is not open. Mounted in the layout.
 */
@Component({
  selector: 'app-library-assistant-bubble',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, LibraryTurn, HlmButtonImports, HlmInputImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideMaximize2, lucideMessagesSquare, lucideMinus, lucideSend, lucideX })],
  host: { class: 'print:hidden', '(document:keydown.escape)': 'session.panelOpen() && minimize()' },
  template: `
    @if (!session.onPage() && session.turns().length) {
      @if (session.panelOpen()) {
        <section
          class="bg-background fixed inset-x-2 bottom-2 z-40 flex h-[75dvh] flex-col overflow-hidden rounded-xl border shadow-2xl sm:inset-x-auto sm:right-4 sm:bottom-20 sm:h-[min(600px,75dvh)] sm:w-[420px]"
          role="dialog"
          [attr.aria-label]="'ai.assistant.title' | transloco"
        >
          <header class="flex items-center gap-2 border-b px-3 py-2">
            <ng-icon name="lucideMessagesSquare" class="shrink-0" />
            <h2 class="min-w-0 flex-1 truncate text-sm font-semibold">{{ 'ai.assistant.title' | transloco }}</h2>
            <button hlmBtn variant="ghost" size="icon" class="size-11 lg:size-8" [title]="'ai.assistant.bubble.maximize' | transloco" [attr.aria-label]="'ai.assistant.bubble.maximize' | transloco" (click)="maximize()">
              <ng-icon name="lucideMaximize2" />
            </button>
            <button hlmBtn variant="ghost" size="icon" class="size-11 lg:size-8" [title]="'ai.assistant.bubble.minimize' | transloco" [attr.aria-label]="'ai.assistant.bubble.minimize' | transloco" (click)="minimize()">
              <ng-icon name="lucideMinus" />
            </button>
            <button hlmBtn variant="ghost" size="icon" class="size-11 lg:size-8" [title]="'ai.assistant.bubble.close' | transloco" [attr.aria-label]="'ai.assistant.bubble.close' | transloco" (click)="session.end()">
              <ng-icon name="lucideX" />
            </button>
          </header>

          <ol #scroller class="flex flex-1 flex-col gap-3 overflow-y-auto p-3" role="list" aria-live="polite">
            @for (t of session.turns(); track t.id) {
              <li><app-library-turn [turn]="t" [compact]="true" /></li>
            }
          </ol>

          <form class="flex gap-2 border-t p-2" (submit)="submit($event)">
            <input
              #box
              hlmInput
              class="h-11 w-full lg:h-9"
              maxlength="500"
              [attr.aria-label]="'ai.assistant.placeholder' | transloco"
              [placeholder]="'ai.assistant.bubble.placeholder' | transloco"
              [value]="session.draft()"
              (input)="session.draft.set($any($event.target).value)"
            />
            <button hlmBtn type="submit" size="icon" class="size-11 shrink-0 lg:size-9" [disabled]="session.busy() || session.draft().trim().length < 3" [attr.aria-label]="'ai.assistant.ask' | transloco">
              @if (session.busy()) {
                <hlm-spinner class="size-4" />
              } @else {
                <ng-icon name="lucideSend" />
              }
            </button>
          </form>
        </section>
      }

      <button
        type="button"
        class="bg-primary text-primary-foreground fixed right-4 bottom-4 z-40 flex size-14 items-center justify-center rounded-full shadow-lg transition-transform hover:scale-105 focus-visible:ring-4 focus-visible:ring-ring/50 focus-visible:outline-none"
        [class.hidden]="session.panelOpen()"
        [class.sm:flex]="session.panelOpen()"
        [attr.aria-label]="(session.busy() ? 'ai.assistant.bubble.openBusy' : 'ai.assistant.bubble.open') | transloco: { count: session.unread() }"
        [attr.aria-expanded]="session.panelOpen()"
        (click)="toggle()"
      >
        <ng-icon name="lucideMessagesSquare" size="24" />
        @if (session.busy()) {
          <!-- A ring that turns while the question runs. -->
          <span class="border-primary-foreground/80 absolute inset-1 animate-spin rounded-full border-2 border-t-transparent" aria-hidden="true"></span>
        }
        @if (session.unread()) {
          <span class="absolute -top-1 -right-1 flex min-w-6 items-center justify-center rounded-full bg-red-600 px-1.5 text-xs font-semibold text-white ring-2 ring-white dark:ring-neutral-900" aria-hidden="true">
            {{ session.unread() }}
          </span>
        }
      </button>
    }
  `,
})
export class LibraryAssistantBubble {
  readonly session = inject(LibraryAssistantSession);
  private readonly router = inject(Router);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly box = viewChild<ElementRef<HTMLInputElement>>('box');

  constructor() {
    // Keep the newest answer in view while the window is open.
    afterRenderEffect(() => {
      this.session.turns();
      const el = this.scroller()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }

  toggle(): void {
    if (this.session.panelOpen()) {
      this.minimize();
      return;
    }
    this.session.panelOpen.set(true);
    this.session.seen();
    setTimeout(() => this.box()?.nativeElement.focus());
  }

  minimize(): void {
    this.session.panelOpen.set(false);
  }

  maximize(): void {
    this.session.panelOpen.set(false);
    void this.router.navigateByUrl('/ai/assistant');
  }

  submit(event: Event): void {
    event.preventDefault();
    this.session.ask(this.session.draft());
  }
}
