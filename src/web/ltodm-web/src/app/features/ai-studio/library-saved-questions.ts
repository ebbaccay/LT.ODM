import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBookOpen, lucideCheck, lucideEyeOff, lucidePencil, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { problemMessage } from '../../core/auth/auth.service';
import { Guide, LibraryAssistantService, LibraryTool, SavedQuestion, SavedQuestionStatus } from './library-assistant.service';

const PAGE = 50;

/**
 * Library assistant > Saved questions (Admin): questions the AI answered, with the look-up and filters it chose.
 * Rules are answered at once without AI; Admins make or remove rules, correct filters and delete questions.
 */
@Component({
  selector: 'app-library-saved-questions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideBookOpen, lucideCheck, lucideEyeOff, lucidePencil, lucideTrash2, lucideX })],
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
                @if (q.isPattern) {
                  <span hlmBadge variant="outline" [title]="'ai.assistant.saved.patternHint' | transloco">{{ 'ai.assistant.saved.pattern' | transloco }}</span>
                }
                <span
                  hlmBadge
                  [variant]="q.status === 'Rule' ? 'default' : q.status === 'Removed' ? 'destructive' : q.status === 'Unanswered' ? 'outline' : 'secondary'"
                  [class.text-amber-700]="q.status === 'Unanswered'"
                >
                  {{ 'ai.assistant.saved.statusName.' + q.status | transloco }}
                </span>
              </div>
              <div class="text-muted-foreground flex flex-wrap items-center gap-1.5 text-xs">
                @if (q.tool !== 'none') {
                  <span hlmBadge variant="outline">{{ 'ai.assistant.tool.' + q.tool | transloco }}</span>
                }
                @for (a of entries(q.arguments); track a.key) {
                  <span hlmBadge variant="secondary"><span class="opacity-70">{{ 'ai.assistant.arg.' + a.key | transloco }}:</span>&nbsp;{{ a.value }}</span>
                }
              </div>
              <p class="text-muted-foreground text-xs">
                @if (q.tool === 'none') {
                  {{ 'ai.assistant.saved.usageUnanswered' | transloco: { count: q.askCount, last: (q.lastAskedUtc | date: 'medium') } }}
                } @else {
                  {{ 'ai.assistant.saved.usage' | transloco: { ai: (q.askCount | number), instant: (q.ruleHits | number), last: (q.lastAskedUtc | date: 'medium') } }}
                }
                @if (q.changedBy) {
                  · {{ 'ai.assistant.saved.changed.' + (q.changedHow ?? 'Admin') | transloco: { user: q.changedBy, date: (q.changedUtc | date: 'medium') } }}
                }
              </p>

              @if (editing() === q.savedQuestionId) {
                <div class="grid gap-2 border-t pt-3 sm:grid-cols-2">
                  <label class="flex flex-col gap-1 text-xs sm:col-span-2">
                    <span class="font-medium">{{ 'ai.assistant.saved.lookUp' | transloco }}</span>
                    <select class="border-input bg-background h-11 rounded-md border px-3 text-sm lg:h-9" (change)="draftTool.set($any($event.target).value)">
                      @if (draftTool() === 'none') {
                        <option value="none" selected>{{ 'ai.assistant.saved.chooseLookUp' | transloco }}</option>
                      }
                      @for (t of tools(); track t.name) {
                        <option [value]="t.name" [selected]="draftTool() === t.name">{{ 'ai.assistant.tool.' + t.name | transloco }}</option>
                      }
                    </select>
                  </label>
                  @for (p of params(draftTool()); track p) {
                    <label class="flex flex-col gap-1 text-xs">
                      <span class="font-medium">{{ 'ai.assistant.arg.' + p | transloco }}</span>
                      <input hlmInput class="h-11 lg:h-9" [value]="draft()[p] ?? ''" (input)="setDraft(p, $any($event.target).value)" />
                    </label>
                  }
                </div>
                <p class="text-muted-foreground text-xs">{{ 'ai.assistant.saved.editHint' | transloco }}</p>
                <div class="flex flex-wrap gap-2">
                  @if (q.status === 'Unanswered' || q.tool === 'none') {
                    <button hlmBtn class="h-11 lg:h-9" [disabled]="busy() || draftTool() === 'none'" (click)="save(q, 'Rule', draft(), draftTool())">
                      <ng-icon name="lucideCheck" />{{ 'ai.assistant.saved.saveAsRule' | transloco }}
                    </button>
                  } @else {
                    <button hlmBtn class="h-11 lg:h-9" [disabled]="busy()" (click)="save(q, $any(q.status), draft(), draftTool())"><ng-icon name="lucideCheck" />{{ 'actions.save' | transloco }}</button>
                  }
                  <button hlmBtn variant="outline" class="h-11 lg:h-9" (click)="editing.set(null)">{{ 'actions.cancel' | transloco }}</button>
                </div>
              } @else {
                <div class="flex flex-wrap items-center gap-2 border-t pt-3">
                  @if (q.status === 'Rule') {
                    <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="busy()" (click)="save(q, 'Removed', q.arguments)"><ng-icon name="lucideX" />{{ 'ai.assistant.saved.removeRule' | transloco }}</button>
                  } @else if (q.tool !== 'none') {
                    <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="busy()" (click)="save(q, 'Rule', q.arguments)"><ng-icon name="lucideCheck" />{{ 'ai.assistant.saved.makeRule' | transloco }}</button>
                  }
                  <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="edit(q)">
                    <ng-icon name="lucidePencil" />{{ (q.tool === 'none' ? 'ai.assistant.saved.answerWithLookUp' : 'ai.assistant.saved.editFilters') | transloco }}
                  </button>
                  @if (q.status !== 'Rule' && guides().length) {
                    <span class="inline-flex items-center gap-1">
                      <select
                        class="border-input bg-background h-11 max-w-56 rounded-md border px-2 text-xs lg:h-8"
                        [attr.aria-label]="'ai.assistant.saved.guide' | transloco"
                        (change)="guideFor.set({ id: q.savedQuestionId, code: $any($event.target).value })"
                      >
                        <option value="">{{ 'ai.assistant.saved.chooseGuide' | transloco }}</option>
                        @for (g of guides(); track g.code) {
                          <option [value]="g.code">{{ g.title }}</option>
                        }
                      </select>
                      <button
                        hlmBtn
                        variant="outline"
                        class="h-11 lg:h-8"
                        [disabled]="busy() || guideFor()?.id !== q.savedQuestionId || !guideFor()?.code"
                        (click)="toGuide(q)"
                      >
                        <ng-icon name="lucideBookOpen" />{{ 'ai.assistant.saved.addToGuide' | transloco }}
                      </button>
                    </span>
                  }
                  @if (q.status === 'Unanswered') {
                    <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="busy()" (click)="save(q, 'Removed', q.arguments)"><ng-icon name="lucideEyeOff" />{{ 'ai.assistant.saved.ignore' | transloco }}</button>
                  }
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

  readonly statuses: SavedQuestionStatus[] = ['Unanswered', 'Rule', 'Recorded', 'Removed'];
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
  /** The look-up while editing ('none' = an unanswered question, not chosen yet). */
  readonly draftTool = signal('none');
  /** How-to guides, for "Add to guide". */
  readonly guides = signal<Guide[]>([]);
  readonly guideFor = signal<{ id: number; code: string } | null>(null);

  ngOnInit(): void {
    this.load(0);
    this.svc.guides().subscribe({ next: (g) => this.guides.set(g.filter((x) => x.isActive)), error: () => this.guides.set([]) });
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
    this.draftTool.set(q.tool);
    this.editing.set(q.savedQuestionId);
  }

  toGuide(q: SavedQuestion): void {
    const code = this.guideFor()?.code;
    if (!code) return;
    this.busy.set(true);
    this.svc.savedToGuide(q.savedQuestionId, code).subscribe({
      next: () => {
        this.busy.set(false);
        this.guideFor.set(null);
        toast.success(this.transloco.translate('ai.assistant.saved.addedToGuide', { title: this.guides().find((g) => g.code === code)?.title ?? code }));
        this.load(this.skip());
      },
      error: (e) => {
        this.busy.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  setDraft(key: string, value: string): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  save(q: SavedQuestion, status: Exclude<SavedQuestionStatus, 'Unanswered'>, args: Record<string, string>, tool?: string): void {
    // Keys of filters the chosen look-up does not take are left out.
    const allowed = tool && tool !== 'none' ? new Set(this.params(tool)) : null;
    const kept = allowed ? Object.fromEntries(Object.entries(args).filter(([k]) => allowed.has(k))) : args;
    this.busy.set(true);
    this.svc.saveSaved(q.savedQuestionId, status, kept, tool && tool !== 'none' && tool !== q.tool ? tool : undefined).subscribe({
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
