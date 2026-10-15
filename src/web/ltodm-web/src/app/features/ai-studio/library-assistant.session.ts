import { Injectable, effect, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { Subscription } from 'rxjs';
import { AuthService, problemMessage } from '../../core/auth/auth.service';
import { LibraryAnswer, LibraryAssistantService } from './library-assistant.service';

export interface LibraryTurnState {
  id: number;
  question: string;
  answer: LibraryAnswer | null;
  error: string | null;
  cancelled: boolean;
  /** Rows shown: the first few until "Show all". */
  showAll: boolean;
  /** Thumbs given on this answer (one per answer). */
  feedback: 'up' | 'down' | null;
}

/**
 * The Library assistant conversation for this browser tab, kept outside the page so it survives navigating: away from
 * the page it continues in the chat bubble (library-assistant-bubble.ts), and a running question keeps running.
 * Memory only: a reload or sign-out ends it. Cancel, New conversation or closing the bubble aborts a running question,
 * and the API then stops the AI server's work on it.
 */
@Injectable({ providedIn: 'root' })
export class LibraryAssistantSession {
  private readonly svc = inject(LibraryAssistantService);
  private readonly auth = inject(AuthService);
  private readonly transloco = inject(TranslocoService);

  readonly turns = signal<LibraryTurnState[]>([]);
  readonly draft = signal('');
  readonly busy = signal(false);
  /** The assistant page is showing (the bubble hides). */
  readonly onPage = signal(false);
  /** The bubble's chat window is open. */
  readonly panelOpen = signal(false);
  /** Answers that arrived while neither the page nor the chat window was open. */
  readonly unread = signal(0);

  private nextId = 1;
  private running: { id: number; sub: Subscription } | null = null;

  constructor() {
    // Signing out (or another user signing in) ends the conversation.
    let userName: string | null = null;
    effect(() => {
      const name = this.auth.user()?.userName ?? null;
      if (name !== userName) {
        userName = name;
        this.end();
      }
    });
  }

  ask(question: string): void {
    const q = question.trim();
    if (q.length < 3 || this.busy()) return;
    // The last few answered turns go with the question, so "and for women?" keeps the previous search.
    const history = this.turns()
      .filter((t) => t.answer)
      .slice(-4)
      .map((t) => ({ question: t.question, tool: t.answer!.tool, arguments: t.answer!.arguments }));
    const id = this.nextId++;
    this.turns.update((list) => [...list, { id, question: q, answer: null, error: null, cancelled: false, showAll: false, feedback: null }]);
    this.draft.set('');
    this.busy.set(true);
    const sub = this.svc.ask(q, history).subscribe({
      next: (answer) => this.finish(id, { answer }),
      error: (e) => this.finish(id, { error: problemMessage(e) || this.transloco.translate('ai.failed') }),
    });
    this.running = { id, sub };
  }

  /** Stops the running question: the browser drops the request, the API cancels the AI call, Ollama stops generating. */
  cancel(): void {
    if (!this.running) return;
    const { id, sub } = this.running;
    sub.unsubscribe();
    this.finish(id, { cancelled: true }, false);
  }

  /** New conversation / close the bubble / sign-out: cancels and forgets everything. */
  end(): void {
    this.cancel();
    this.turns.set([]);
    this.draft.set('');
    this.panelOpen.set(false);
    this.unread.set(0);
  }

  update(id: number, change: Partial<LibraryTurnState>): void {
    this.turns.update((list) => list.map((t) => (t.id === id ? { ...t, ...change } : t)));
  }

  /** Thumbs up: saved, answered at once next time. Thumbs down: removed, the AI is asked next time. */
  feedback(t: LibraryTurnState, helpful: boolean): void {
    if (!t.answer) return;
    this.update(t.id, { feedback: helpful ? 'up' : 'down' });
    this.svc.feedback(t.answer, helpful).subscribe({
      error: (e) => {
        this.update(t.id, { feedback: null });
        toast.error(problemMessage(e));
      },
    });
  }

  /** The conversation is being looked at (page or chat window): nothing unread. */
  seen(): void {
    this.unread.set(0);
  }

  private finish(id: number, change: Partial<LibraryTurnState>, notify = true): void {
    if (this.running?.id === id) this.running = null;
    this.update(id, change);
    this.busy.set(false);
    if (notify && !this.onPage() && !this.panelOpen()) this.unread.update((n) => n + 1);
  }
}
