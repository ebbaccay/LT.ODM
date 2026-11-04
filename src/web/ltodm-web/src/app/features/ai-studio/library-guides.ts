import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideLink, lucidePencil, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { problemMessage } from '../../core/auth/auth.service';
import { Guide, GuideStep, LibraryAssistantService } from './library-assistant.service';

/** The form while editing: steps and questions as text, one per line ("text | /address" links a step). */
interface GuideDraft {
  code: string;
  title: string;
  who: string;
  intro: string;
  steps: string;
  questions: string;
  isActive: boolean;
}

const EMPTY: GuideDraft = { code: '', title: '', who: '', intro: '', steps: '', questions: '', isActive: true };

/** "Open Packages | /packages?new=1" -> text and link; a line without " | /" has no link. */
export function parseSteps(text: string): GuideStep[] {
  return text
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
    .map((l) => {
      const at = l.lastIndexOf(' | /');
      return at < 0 ? { text: l, url: null } : { text: l.slice(0, at).trim(), url: l.slice(at + 3).trim() };
    });
}

export function formatSteps(steps: GuideStep[]): string {
  return steps.map((s) => (s.url ? `${s.text} | ${s.url}` : s.text)).join('\n');
}

/**
 * Library assistant > How-to guides (Admin): fixed processes written as numbered steps with links to the screens, and the
 * questions that open each one at once (no AI). {customer} and {season} in a link are filled from the question.
 */
@Component({
  selector: 'app-library-guides',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgTemplateOutlet, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe],
  providers: [provideIcons({ lucideCheck, lucideLink, lucidePencil, lucidePlus, lucideTrash2 })],
  template: `
    <ng-template #form>
      @let d = draft();
      <div class="flex flex-col gap-3 border-t pt-3">
        @if (editing() === '') {
          <label class="flex flex-col gap-1 text-xs">
            <span class="font-medium">{{ 'ai.assistant.guides.code' | transloco }}</span>
            <input hlmInput class="h-11 font-mono lg:h-9" maxlength="40" placeholder="send_sample" [value]="d.code" (input)="set('code', $any($event.target).value)" />
            <span class="text-muted-foreground">{{ 'ai.assistant.guides.codeHint' | transloco }}</span>
          </label>
        }
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.guides.title' | transloco }}</span>
          <input hlmInput class="h-11 lg:h-9" maxlength="200" [value]="d.title" (input)="set('title', $any($event.target).value)" />
        </label>
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.guides.who' | transloco }}</span>
          <input hlmInput class="h-11 lg:h-9" maxlength="200" [placeholder]="'ai.assistant.guides.whoPlaceholder' | transloco" [value]="d.who" (input)="set('who', $any($event.target).value)" />
        </label>
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.guides.intro' | transloco }}</span>
          <textarea hlmTextarea rows="2" maxlength="1000" class="text-sm" [value]="d.intro" (input)="set('intro', $any($event.target).value)"></textarea>
        </label>
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.guides.steps' | transloco }}</span>
          <textarea hlmTextarea rows="7" class="font-mono text-xs" [value]="d.steps" (input)="set('steps', $any($event.target).value)"></textarea>
          <span class="text-muted-foreground">{{ 'ai.assistant.guides.stepsHint' | transloco }}</span>
        </label>
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.guides.questions' | transloco }}</span>
          <textarea hlmTextarea rows="5" class="text-xs" [value]="d.questions" (input)="set('questions', $any($event.target).value)"></textarea>
          <span class="text-muted-foreground">{{ 'ai.assistant.guides.questionsHint' | transloco }}</span>
        </label>
        <label class="flex items-center gap-2 text-sm">
          <input type="checkbox" class="size-4" [checked]="d.isActive" (change)="set('isActive', $any($event.target).checked)" />
          {{ 'ai.assistant.guides.active' | transloco }}
        </label>
        <div class="flex flex-wrap gap-2">
          <button hlmBtn class="h-11 lg:h-9" [disabled]="busy()" (click)="save()"><ng-icon name="lucideCheck" />{{ 'actions.save' | transloco }}</button>
          <button hlmBtn variant="outline" class="h-11 lg:h-9" (click)="editing.set(null)">{{ 'actions.cancel' | transloco }}</button>
        </div>
      </div>
    </ng-template>

    <section class="flex flex-col gap-4">
      <div class="flex flex-wrap items-start justify-between gap-2">
        <p class="text-muted-foreground max-w-3xl text-sm">{{ 'ai.assistant.guides.hint' | transloco }}</p>
        @if (editing() === null) {
          <button hlmBtn class="h-11 lg:h-9" (click)="startNew()"><ng-icon name="lucidePlus" />{{ 'ai.assistant.guides.new' | transloco }}</button>
        }
      </div>

      @if (editing() === '') {
        <article hlmCard class="gap-2 p-4">
          <h2 class="font-semibold">{{ 'ai.assistant.guides.new' | transloco }}</h2>
          <ng-container [ngTemplateOutlet]="form" />
        </article>
      }

      @if (loading()) {
        <p class="text-muted-foreground flex items-center gap-2 text-sm"><hlm-spinner class="size-4" />{{ 'ai.assistant.thinking' | transloco }}</p>
      } @else if (!guides().length) {
        <p class="text-muted-foreground text-sm">{{ 'ai.assistant.guides.none' | transloco }}</p>
      } @else {
        <ul class="flex flex-col gap-3" role="list">
          @for (g of guides(); track g.code) {
            <li hlmCard class="gap-2 p-4">
              <div class="flex flex-wrap items-start justify-between gap-2">
                <div class="min-w-0 flex-1">
                  <p class="font-medium">{{ g.title }}</p>
                  <p class="text-muted-foreground text-xs"><span class="font-mono">{{ g.code }}</span>@if (g.who) { · {{ g.who }} }</p>
                </div>
                @if (!g.isActive) {
                  <span hlmBadge variant="secondary">{{ 'ai.assistant.guides.inactive' | transloco }}</span>
                }
              </div>
              @if (g.intro) {
                <p class="text-sm">{{ g.intro }}</p>
              }
              <ol class="list-decimal space-y-1 pl-5 text-sm">
                @for (s of g.steps; track $index) {
                  <li>
                    {{ s.text }}
                    @if (s.url) {
                      <span class="text-muted-foreground ml-1 inline-flex items-center gap-1 font-mono text-xs"><ng-icon name="lucideLink" size="12" />{{ s.url }}</span>
                    }
                  </li>
                }
              </ol>
              <div class="flex flex-wrap items-center gap-1.5 text-xs">
                <span class="text-muted-foreground">{{ 'ai.assistant.guides.questions' | transloco }}:</span>
                @for (q of g.questions; track q) {
                  <span hlmBadge variant="outline">“{{ q }}”</span>
                } @empty {
                  <span class="text-muted-foreground italic">{{ 'ai.assistant.guides.noQuestions' | transloco }}</span>
                }
              </div>
              @if (g.changedBy) {
                <p class="text-muted-foreground text-xs">{{ 'ai.assistant.guides.changed.' + (g.changedHow ?? 'Admin') | transloco: { user: g.changedBy, date: (g.changedUtc | date: 'medium') } }}</p>
              }

              @if (editing() === g.code) {
                <ng-container [ngTemplateOutlet]="form" />
              } @else {
                <div class="flex flex-wrap gap-2 border-t pt-3">
                  <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="editing() !== null" (click)="edit(g)"><ng-icon name="lucidePencil" />{{ 'actions.edit' | transloco }}</button>
                  <button hlmBtn variant="ghost" class="h-11 text-red-700 lg:h-8 dark:text-red-400" [disabled]="busy()" (click)="remove(g)"><ng-icon name="lucideTrash2" />{{ 'actions.delete' | transloco }}</button>
                </div>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class LibraryGuides implements OnInit {
  private readonly svc = inject(LibraryAssistantService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly guides = signal<Guide[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  /** The code of the guide being edited; '' = a new guide; null = none. */
  readonly editing = signal<string | null>(null);
  readonly draft = signal<GuideDraft>({ ...EMPTY });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.guides().subscribe({
      next: (g) => {
        this.guides.set(g);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  startNew(): void {
    this.draft.set({ ...EMPTY });
    this.editing.set('');
  }

  edit(g: Guide): void {
    this.draft.set({
      code: g.code, title: g.title, who: g.who ?? '', intro: g.intro ?? '', steps: formatSteps(g.steps), questions: g.questions.join('\n'), isActive: g.isActive,
    });
    this.editing.set(g.code);
  }

  set<K extends keyof GuideDraft>(key: K, value: GuideDraft[K]): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  save(): void {
    const d = this.draft();
    const code = (this.editing() || d.code).trim().toLowerCase();
    this.busy.set(true);
    this.svc
      .saveGuide(code, {
        title: d.title.trim(), who: d.who.trim() || null, intro: d.intro.trim() || null, steps: parseSteps(d.steps), isActive: d.isActive,
        questions: d.questions.split('\n').map((q) => q.trim()).filter((q) => q.length > 0),
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.editing.set(null);
          toast.success(this.transloco.translate('ai.assistant.guides.saved'));
          this.load();
        },
        error: (e) => {
          this.busy.set(false);
          toast.error(problemMessage(e));
        },
      });
  }

  async remove(g: Guide): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('ai.assistant.guides.deleteTitle'),
      message: this.transloco.translate('ai.assistant.guides.deleteMessage', { title: g.title }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.busy.set(true);
    this.svc.deleteGuide(g.code).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        toast.error(problemMessage(e));
      },
    });
  }
}
