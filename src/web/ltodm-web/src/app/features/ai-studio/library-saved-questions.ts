import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucidePencil, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { problemMessage } from '../../core/auth/auth.service';
import { LibraryAssistantService, LibraryTool, SavedQuestion, SavedQuestionStatus } from './library-assistant.service';

const PAGE = 50;

/**
 * Library assistant > Saved questions (Admin): questions the AI answered, with the look-up and filters it chose.
 * Rules are answered at once without AI; Admins make or remove rules, correct filters and delete questions.
 */
@Component({
  selector: 'app-library-saved-questions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCheck, lucidePencil, lucideTrash2, lucideX })],
  template: `
    <section class="flex flex-col gap-4">
      <p class="text-muted-foreground text-sm">{{ 'ai.assistant.saved.hint' | transloco }}</p>
      <div class="flex flex-wrap gap-2">
        <select class="border-input bg-background h-11 rounded-md border px-3 text-sm lg:h-9" [attr.aria-label]="'ai.assistant.saved.status' | transloco" (change)="setStatus($any($event.target).value)">
          <option value="" [selected]="status() === ''">{{ 'ai.assistant.saved.allStatuses' | transloco }}</option>
          @for (s of statuses; track s) {
            <option [value]="s" [selected]="status() === s">{{ 'ai.assistant.saved.statusName.' + s | transloco }}</option>
          }
        </select>
        <input
          hlmInput
          class="h-11 min-w-48 flex-1 lg:h-9"
          [placeholder]="'ai.assistant.saved.search' | transloco"
          [value]="search()"
          (input)="search.set($any($event.target).value)"
          (keydown.enter)="load(0)"
        />
        <button hlmBtn variant="outline" class="h-11 lg:h-9" (click)="load(0)">{{ 'ai.assistant.saved.find' | transloco }}</button>
      </div>

      @if (loading()) {
        <p class="text-muted-foreground flex items-center gap-2 text-sm"><hlm-spinner class="size-4" />{{ 'ai.assistant.thinking' | transloco }}</p>
      } @else if (!rows().length) {
        <p class="text-muted-foreground text-sm">{{ 'ai.assistant.saved.none' | transloco }}</p>
      } @else {
        <ul class="flex flex-col gap-3" role="list">
          @for (q of rows(); track q.savedQuestionId) {
            <li hlmCard class="gap-2 p-4">
              <div class="flex flex-wrap items-start justify-between gap-2">
                <p class="min-w-0 flex-1 font-medium">“{{ q.question }}”</p>
                <span hlmBadge [variant]="q.status === 'Rule' ? 'default' : q.status === 'Removed' ? 'destructive' : 'secondary'">
                  {{ 'ai.assistant.saved.statusName.' + q.status | transloco }}
                </span>
              </div>
              <div class="text-muted-foreground flex flex-wrap items-center gap-1.5 text-xs">
                <span hlmBadge variant="outline">{{ 'ai.assistant.tool.' + q.tool | transloco }}</span>
                @for (a of entries(q.arguments); track a.key) {
                  <span hlmBadge variant="secondary"><span class="opacity-70">{{ 'ai.assistant.arg.' + a.key | transloco }}:</span>&nbsp;{{ a.value }}</span>
                }
              </div>
              <p class="text-muted-foreground text-xs">
                {{ 'ai.assistant.saved.usage' | transloco: { ai: (q.askCount | number), instant: (q.ruleHits | number), last: (q.lastAskedUtc | date: 'medium') } }}
                @if (q.changedBy) {
                  · {{ 'ai.assistant.saved.changed.' + (q.changedHow ?? 'Admin') | transloco: { user: q.changedBy, date: (q.changedUtc | date: 'medium') } }}
                }
              </p>

              @if (editing() === q.savedQuestionId) {
                <div class="grid gap-2 border-t pt-3 sm:grid-cols-2">
                  @for (p of params(q.tool); track p) {
                    <label class="flex flex-col gap-1 text-xs">
                      <span class="font-medium">{{ 'ai.assistant.arg.' + p | transloco }}</span>
                      <input hlmInput class="h-11 lg:h-9" [value]="draft()[p] ?? ''" (input)="setDraft(p, $any($event.target).value)" />
                    </label>
                  }
                </div>
                <p class="text-muted-foreground text-xs">{{ 'ai.assistant.saved.editHint' | transloco }}</p>
                <div class="flex flex-wrap gap-2">
                  <button hlmBtn class="h-11 lg:h-9" [disabled]="busy()" (click)="save(q, q.status, draft())"><ng-icon name="lucideCheck" />{{ 'actions.save' | transloco }}</button>
                  <button hlmBtn variant="outline" class="h-11 lg:h-9" (click)="editing.set(null)">{{ 'actions.cancel' | transloco }}</button>
                </div>
              } @else {
                <div class="flex flex-wrap gap-2 border-t pt-3">
                  @if (q.status === 'Rule') {
                    <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="busy()" (click)="save(q, 'Removed', q.arguments)"><ng-icon name="lucideX" />{{ 'ai.assistant.saved.removeRule' | transloco }}</button>
                  } @else {
                    <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="busy()" (click)="save(q, 'Rule', q.arguments)"><ng-icon name="lucideCheck" />{{ 'ai.assistant.saved.makeRule' | transloco }}</button>
                  }
                  <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="edit(q)"><ng-icon name="lucidePencil" />{{ 'ai.assistant.saved.editFilters' | transloco }}</button>
                  <button hlmBtn variant="ghost" class="h-11 text-red-700 lg:h-8 dark:text-red-400" [disabled]="busy()" (click)="remove(q)"><ng-icon name="lucideTrash2" />{{ 'actions.delete' | transloco }}</button>
                </div>
              }
            </li>
          }
        </ul>
        <div class="text-muted-foreground flex flex-wrap items-center gap-2 text-xs">
          <span>{{ 'ai.assistant.rows' | transloco: { shown: rows().length + skip(), total: total() } }}</span>
          @if (skip() > 0) {
            <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="load(skip() - page)">{{ 'ai.assistant.saved.previous' | transloco }}</button>
          }
          @if (skip() + rows().length < total()) {
            <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="load(skip() + page)">{{ 'ai.assistant.saved.next' | transloco }}</button>
          }
        </div>
      }
    </section>
  `,
})
export class LibrarySavedQuestions implements OnInit {
  private readonly svc = inject(LibraryAssistantService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  /** The assistant's tools: which filters each look-up takes (for editing). */
  readonly tools = input<LibraryTool[]>([]);

  readonly statuses: SavedQuestionStatus[] = ['Rule', 'Recorded', 'Removed'];
  readonly page = PAGE;
  readonly status = signal<SavedQuestionStatus | ''>('');
  readonly search = signal('');
  readonly rows = signal<SavedQuestion[]>([]);
  readonly total = signal(0);
  readonly skip = signal(0);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly editing = signal<number | null>(null);
  readonly draft = signal<Record<string, string>>({});

  ngOnInit(): void {
    this.load(0);
  }

  entries(args: Record<string, string>): { key: string; value: string }[] {
    return Object.entries(args).map(([key, value]) => ({ key, value }));
  }

  params(tool: string): string[] {
    return this.tools().find((t) => t.name === tool)?.parameters ?? [];
  }

  setStatus(value: SavedQuestionStatus | ''): void {
    this.status.set(value);
    this.load(0);
  }

  load(skip: number): void {
    this.loading.set(true);
    this.skip.set(Math.max(0, skip));
    this.svc.saved(this.status(), this.search(), this.skip(), PAGE).subscribe({
      next: (r) => {
        this.rows.set(r.items);
        this.total.set(r.total);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  edit(q: SavedQuestion): void {
    this.draft.set({ ...q.arguments });
    this.editing.set(q.savedQuestionId);
  }

  setDraft(key: string, value: string): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  save(q: SavedQuestion, status: SavedQuestionStatus, args: Record<string, string>): void {
    this.busy.set(true);
    this.svc.saveSaved(q.savedQuestionId, status, args).subscribe({
      next: () => {
        this.busy.set(false);
        this.editing.set(null);
        toast.success(this.transloco.translate('ai.assistant.saved.saved'));
        this.load(this.skip());
      },
      error: (e) => {
        this.busy.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  async remove(q: SavedQuestion): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('ai.assistant.saved.deleteTitle'),
      message: this.transloco.translate('ai.assistant.saved.deleteMessage', { question: q.question }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteSaved(q.savedQuestionId).subscribe({
      next: () => this.load(this.skip()),
      error: (e) => toast.error(problemMessage(e)),
    });
  }
}
