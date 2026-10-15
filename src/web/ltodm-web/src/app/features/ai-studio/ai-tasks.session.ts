import { Location } from '@angular/common';
import { DestroyRef, Injectable, Signal, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { Observable, Subscription } from 'rxjs';
import { AuthService, problemMessage } from '../../core/auth/auth.service';

export type AiTaskKind = 'render' | 'bomExplain' | 'compareSummary';

export interface AiTask<T = unknown> {
  id: number;
  kind: AiTaskKind;
  /** What the task is for (e.g. "render:123"): one task per key; a page shows the task with its key. */
  key: string;
  /** Short name in the tray and notices, e.g. the style number. */
  label: string;
  /** The page to go back to. */
  url: string;
  status: 'running' | 'done' | 'failed' | 'cancelled';
  result: T | null;
  error: string | null;
  startedAt: number;
  /** The answer has been shown on its page (it then leaves the tray). */
  seen: boolean;
}

/** Answers kept for pages to show again after a visit elsewhere (newest first). */
const KEEP_FINISHED = 10;

/**
 * Slow AI jobs (style renders, BOM check explanations, change summaries) for this browser tab, kept outside their pages
 * like the Library assistant conversation: leaving the page (or Minimise) does not stop or lose them. The tray
 * (ai-tasks-tray.ts) shows what is running and what is ready, and leads back to the page. Memory only: a reload or
 * sign-out ends them. Cancel drops the request; the API then stops waiting for the AI server.
 */
@Injectable({ providedIn: 'root' })
export class AiTasksSession {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly transloco = inject(TranslocoService);

  readonly tasks = signal<AiTask[]>([]);
  /** Keys of the tasks a page is showing now (their answers are seen as they arrive). */
  private readonly viewing = signal<ReadonlySet<string>>(new Set());
  /** In the tray: running, or finished and not yet seen on its page; never the one the page on screen shows. */
  readonly pending = computed(() => this.tasks().filter((t) => !this.viewing().has(t.key) && (t.status === 'running' || !t.seen)));

  private nextId = 1;
  private readonly subs = new Map<number, Subscription>();

  constructor() {
    // Signing out (or another user signing in) ends everything.
    let userName: string | null = null;
    effect(() => {
      const name = this.auth.user()?.userName ?? null;
      if (name !== userName) {
        userName = name;
        this.clear();
      }
    });
  }

  /** Starts the job unless the same key is already running. The answer stays here until the page shows it. */
  run<T>(kind: AiTaskKind, key: string, label: string, request: Observable<T>, url = this.router.url): void {
    if (this.tasks().some((t) => t.key === key && t.status === 'running')) return;
    const id = this.nextId++;
    const task: AiTask = { id, kind, key, label, url, status: 'running', result: null, error: null, startedAt: Date.now(), seen: false };
    this.tasks.update((list) => [task, ...list.filter((t) => t.key !== key)]);
    const sub = request.subscribe({
      next: (result) => this.finish(id, { status: 'done', result }),
      error: (e) => this.finish(id, { status: 'failed', error: problemMessage(e) || this.transloco.translate('ai.failed') }),
    });
    if (this.tasks().some((t) => t.id === id && t.status === 'running')) this.subs.set(id, sub);
  }

  /**
   * For a page: the task for its current key (null when none), and while the page is open its answers count as seen.
   * Call in an injection context (constructor or field initialiser).
   */
  watch<T>(key: Signal<string | null>, destroyRef = inject(DestroyRef)): Signal<AiTask<T> | null> {
    let shown: string | null = null;
    const show = (k: string | null) => {
      this.viewing.update((s) => {
        const next = new Set(s);
        if (shown) next.delete(shown);
        if (k) next.add(k);
        return next;
      });
      shown = k;
    };
    effect(() => show(key()));
    destroyRef.onDestroy(() => show(null));
    const task = computed(() => (this.tasks().find((t) => t.key === key()) as AiTask<T> | undefined) ?? null);
    effect(() => {
      const t = task();
      if (t && t.status !== 'running' && !t.seen) this.patch(t.id, { seen: true });
    });
    return task;
  }

  cancel(id: number): void {
    const sub = this.subs.get(id);
    if (!sub) return;
    sub.unsubscribe();
    this.subs.delete(id);
    this.patch(id, { status: 'cancelled', seen: true });
  }

  /** Out of the tray (and forgotten when finished). */
  dismiss(id: number): void {
    if (this.subs.has(id)) this.cancel(id);
    this.tasks.update((list) => list.filter((t) => t.id !== id));
  }

  open(t: AiTask): void {
    void this.router.navigateByUrl(t.url);
  }

  /** The page's Minimise: back to where the user came from; the task carries on in the tray. */
  minimize(): void {
    if (this.router.lastSuccessfulNavigation()?.previousNavigation) this.location.back();
    else void this.router.navigateByUrl('/');
  }

  private clear(): void {
    for (const sub of this.subs.values()) sub.unsubscribe();
    this.subs.clear();
    this.tasks.set([]);
  }

  private finish(id: number, change: Partial<AiTask>): void {
    this.subs.delete(id);
    const t = this.tasks().find((x) => x.id === id);
    if (!t) return;
    const watched = this.viewing().has(t.key);
    this.patch(id, { ...change, seen: watched });
    // Keep a few finished answers for pages to show again; running ones always stay.
    const finished = this.tasks().filter((x) => x.status !== 'running');
    if (finished.length > KEEP_FINISHED) {
      const drop = new Set(finished.slice(KEEP_FINISHED).map((x) => x.id));
      this.tasks.update((list) => list.filter((x) => !drop.has(x.id)));
    }
    // On the page an answer shows itself and a failure needs saying; away from it, both are said with a way back.
    const name = this.transloco.translate(`ai.tasks.kind.${t.kind}`, { label: t.label });
    const action = watched ? undefined : { label: this.transloco.translate('ai.tasks.open'), onClick: () => this.open(t) };
    if (change.status === 'done') {
      if (!watched) toast.success(this.transloco.translate('ai.tasks.readyToast', { name }), { action });
    } else toast.error(this.transloco.translate('ai.tasks.failedToast', { name }), { description: change.error ?? undefined, action });
  }

  private patch(id: number, change: Partial<AiTask>): void {
    this.tasks.update((list) => list.map((t) => (t.id === id ? { ...t, ...change } : t)));
  }
}
