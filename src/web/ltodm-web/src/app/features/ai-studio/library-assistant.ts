import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEraser, lucideMessagesSquare, lucideMinimize2, lucideSend, lucideZap } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { catchError, of } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { AiProviderBanner } from './ai-provider-banner';
import { LibraryAssistantService } from './library-assistant.service';
import { LibraryAssistantSession } from './library-assistant.session';
import { LibrarySavedQuestions } from './library-saved-questions';
import { LibraryTurn } from './library-turn';

/**
 * AI Studio > Library assistant: questions about the library, answered from the database. The phrase list and saved
 * questions answer at once without AI; otherwise the Text job picks one read-only tool and words the answer from its rows.
 * The conversation lives in LibraryAssistantSession: leaving the page (or Minimize) keeps it, and a running question,
 * in the chat bubble on every page.
 */
@Component({
  selector: 'app-library-assistant',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, AiProviderBanner, LibrarySavedQuestions, LibraryTurn, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideEraser, lucideMessagesSquare, lucideMinimize2, lucideSend, lucideZap })],
  template: `
    <section class="flex flex-col gap-5">
      <header class="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideMessagesSquare" />{{ 'ai.assistant.title' | transloco }}</h1>
          <p class="text-muted-foreground mt-1 text-sm">{{ 'ai.assistant.subtitle' | transloco }}</p>
        </div>
        @if (view() === 'ask' && session.turns().length) {
          <div class="flex flex-wrap gap-2">
            <button hlmBtn variant="outline" class="h-11 lg:h-9" [title]="'ai.assistant.bubble.minimizeHint' | transloco" (click)="minimize()">
              <ng-icon name="lucideMinimize2" />{{ 'ai.assistant.bubble.minimize' | transloco }}
            </button>
            <button hlmBtn variant="outline" class="h-11 lg:h-9" (click)="session.end()"><ng-icon name="lucideEraser" />{{ 'ai.assistant.clear' | transloco }}</button>
          </div>
        }
      </header>

      @if (isAdmin()) {
        <div class="bg-muted inline-flex w-fit rounded-md p-1 text-sm" role="tablist">
          @for (v of views; track v) {
            <button
              type="button"
              role="tab"
              class="rounded px-3 py-1.5"
              [class]="view() === v ? 'bg-background shadow-sm font-medium' : 'text-muted-foreground'"
              [attr.aria-selected]="view() === v"
              (click)="view.set(v)"
            >
              {{ 'ai.assistant.view.' + v | transloco }}
            </button>
          }
        </div>
      }

      @if (view() === 'saved') {
        <app-library-saved-questions [tools]="tools()" />
      } @else {
        <app-ai-provider-banner uses="text" />

        @if (!session.turns().length) {
          @if (popular().length) {
            <section hlmCard class="gap-3 p-5">
              <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideZap" />{{ 'ai.assistant.popular' | transloco }}</h2>
              <div class="flex flex-wrap gap-2">
                @for (q of popular(); track q) {
                  <button type="button" class="bg-muted hover:bg-accent rounded-full px-3 py-1 text-xs" (click)="session.ask(q)">{{ q }}</button>
                }
              </div>
            </section>
          }
          <section hlmCard class="gap-3 p-5">
            <h2 class="font-semibold">{{ 'ai.assistant.canAsk' | transloco }}</h2>
            <ul class="grid gap-2 sm:grid-cols-2" role="list">
              @for (t of tools(); track t.name) {
                <li>
                  <button type="button" class="hover:bg-accent/50 flex h-full w-full flex-col items-start gap-0.5 rounded-md border p-3 text-left" (click)="session.ask(t.example)">
                    <span class="text-sm font-medium">{{ 'ai.assistant.tool.' + t.name | transloco }}</span>
                    <span class="text-muted-foreground text-xs">“{{ t.example }}”</span>
                  </button>
                </li>
              }
            </ul>
            <p class="text-muted-foreground text-xs">{{ 'ai.assistant.howItWorks' | transloco }}</p>
          </section>
        }

        <ol class="flex flex-col gap-4" role="list" aria-live="polite">
          @for (t of session.turns(); track t.id) {
            <li><app-library-turn [turn]="t" /></li>
          }
        </ol>

        <form class="bg-background sticky bottom-0 flex gap-2 py-2" (submit)="submit($event)">
          <input
            #box
            hlmInput
            class="h-11 w-full lg:h-10"
            maxlength="500"
            [attr.aria-label]="'ai.assistant.placeholder' | transloco"
            [placeholder]="'ai.assistant.placeholder' | transloco"
            [value]="session.draft()"
            (input)="session.draft.set($any($event.target).value)"
          />
          <button hlmBtn type="submit" class="h-11 shrink-0 lg:h-10" [disabled]="session.busy() || session.draft().trim().length < 3">
            @if (session.busy()) {
              <hlm-spinner class="size-4" />
            } @else {
              <ng-icon name="lucideSend" />
            }
            {{ 'ai.assistant.ask' | transloco }}
          </button>
        </form>
      }
    </section>
  `,
})
export class LibraryAssistant {
  readonly session = inject(LibraryAssistantSession);
  private readonly svc = inject(LibraryAssistantService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly box = viewChild<ElementRef<HTMLInputElement>>('box');

  readonly views = ['ask', 'saved'] as const;
  readonly view = signal<'ask' | 'saved'>('ask');
  readonly isAdmin = computed(() => (this.auth.user()?.roles ?? []).some((r) => r.toLowerCase() === 'admin'));
  readonly tools = toSignal(this.svc.tools().pipe(catchError(() => of([]))), { initialValue: [] });
  readonly popular = toSignal(this.svc.popular().pipe(catchError(() => of([]))), { initialValue: [] });

  constructor() {
    // While this page shows, the bubble hides and nothing is unread. Leaving keeps the conversation (it moves to the bubble).
    this.session.onPage.set(true);
    this.session.panelOpen.set(false);
    this.session.seen();
    inject(DestroyRef).onDestroy(() => this.session.onPage.set(false));
  }

  submit(event: Event): void {
    event.preventDefault();
    this.session.ask(this.session.draft());
    this.box()?.nativeElement.focus();
  }

  /** Back to the previous page; the conversation continues in the bubble. */
  minimize(): void {
    if (this.router.lastSuccessfulNavigation()?.previousNavigation) this.location.back();
    else void this.router.navigateByUrl('/');
  }
}
