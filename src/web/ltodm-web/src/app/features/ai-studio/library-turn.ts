import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight, lucideBookmarkCheck, lucideImage, lucideListChecks, lucideScanEye, lucideSparkles, lucideSquare, lucideThumbsDown, lucideThumbsUp, lucideUser,
} from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { LibraryAssistantSession, LibraryTurnState } from './library-assistant.session';

/**
 * One question and its answer: how it was found (source, look-up, filters), the rows, links, thumbs up / down, and Cancel
 * while it runs. Used by the Library assistant page and, compact (fewer rows, smaller), by the chat bubble's window.
 */
@Component({
  selector: 'app-library-turn',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, AuthImgDirective, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmSpinnerImports, TranslocoPipe],
  providers: [
    provideIcons({ lucideArrowRight, lucideBookmarkCheck, lucideImage, lucideListChecks, lucideScanEye, lucideSparkles, lucideSquare, lucideThumbsDown, lucideThumbsUp, lucideUser }),
  ],
  host: { class: 'flex flex-col gap-2' },
  template: `
    @let t = turn();
    <p class="bg-primary text-primary-foreground ml-auto flex max-w-[85%] items-start gap-2 rounded-lg px-3 py-2 text-sm">
      <ng-icon name="lucideUser" class="mt-0.5 shrink-0" /><span>{{ t.question }}</span>
    </p>
    @if (t.answer; as a) {
      <article hlmCard class="max-w-full gap-3" [class]="compact() ? 'p-3' : 'p-4'">
        <p class="text-sm whitespace-pre-line">{{ a.answer }}</p>
        <div class="text-muted-foreground flex flex-wrap items-center gap-1.5 text-xs">
          @if (a.source === 'rules') {
            <span hlmBadge variant="outline" class="text-emerald-700 dark:text-emerald-400"><ng-icon name="lucideListChecks" />{{ 'ai.assistant.source.rules' | transloco }}</span>
          } @else if (a.source === 'look') {
            <span hlmBadge variant="outline" class="text-emerald-700 dark:text-emerald-400"><ng-icon name="lucideScanEye" />{{ 'ai.assistant.source.look' | transloco }}</span>
          } @else if (a.source === 'saved') {
            <span hlmBadge variant="outline" class="text-emerald-700 dark:text-emerald-400"><ng-icon name="lucideBookmarkCheck" />{{ 'ai.assistant.source.saved' | transloco }}</span>
          } @else if (a.source === 'ai') {
            <span hlmBadge variant="outline"><ng-icon name="lucideSparkles" />{{ 'ai.assistant.source.ai' | transloco }}</span>
          }
          @if (a.tool && a.tool !== 'none') {
            <span hlmBadge variant="secondary">{{ 'ai.assistant.tool.' + a.tool | transloco }}</span>
          }
          @for (arg of args(); track arg.key) {
            <span hlmBadge variant="secondary"><span class="opacity-70">{{ 'ai.assistant.arg.' + arg.key | transloco }}:</span>&nbsp;{{ arg.value }}</span>
          }
        </div>
        @if (a.understood) {
          <p class="text-muted-foreground text-xs"><span class="font-medium">{{ 'ai.assistant.understood' | transloco }}</span> {{ a.understood }}</p>
        }
        @if (a.source === 'ai' && a.answerFromFacts && a.table) {
          <p class="text-muted-foreground text-xs">{{ 'ai.assistant.factsUsed' | transloco }}</p>
        }
        @if (a.unmatched.length) {
          <p class="text-xs text-amber-700 dark:text-amber-400">{{ 'ai.assistant.unmatched' | transloco: { words: a.unmatched.join(', ') } }}</p>
        }

        @if (a.table && a.table.rows.length) {
          <div class="overflow-x-auto rounded-md border">
            <table class="w-full text-xs">
              <thead class="bg-muted/50 text-muted-foreground">
                <tr>
                  @if (a.table.rowImages) {
                    <th class="w-0 px-2 py-1.5"><span class="sr-only">{{ 'ai.assistant.col.picture' | transloco }}</span></th>
                  }
                  @for (c of a.table.columns; track $index) {
                    <th class="px-2 py-1.5 text-left font-medium whitespace-nowrap">{{ 'ai.assistant.col.' + c | transloco }}</th>
                  }
                </tr>
              </thead>
              <tbody class="divide-y">
                @for (row of t.showAll ? a.table.rows : a.table.rows.slice(0, firstRows()); track $index; let i = $index) {
                  <tr
                    [class]="a.table.rowLinks[i] ? 'hover:bg-accent/40 cursor-pointer' : ''"
                    [attr.tabindex]="a.table.rowLinks[i] ? 0 : null"
                    (click)="open(a.table.rowLinks[i])"
                    (keydown.enter)="open(a.table.rowLinks[i])"
                  >
                    @if (a.table.rowImages) {
                      <td class="px-2 py-1">
                        <span class="bg-muted flex shrink-0 items-center justify-center overflow-hidden rounded border" [class]="compact() ? 'size-9' : 'size-12'">
                          @if (a.table.rowImages[i]; as img) {
                            <img [appAuthSrc]="img" alt="" class="size-full object-cover" loading="lazy" />
                          } @else {
                            <ng-icon name="lucideImage" class="text-muted-foreground" size="14" />
                          }
                        </span>
                      </td>
                    }
                    @for (cell of row; track $index) {
                      <td class="px-2 py-1.5 align-middle" [class.font-mono]="$first">{{ cell ?? '—' }}</td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="text-muted-foreground flex flex-wrap items-center gap-2 text-xs">
            <span>{{ 'ai.assistant.rows' | transloco: { shown: t.showAll ? a.table.rows.length : min(a.table.rows.length, firstRows()), total: a.table.total } }}</span>
            @if (!t.showAll && a.table.rows.length > firstRows()) {
              <button type="button" class="text-primary underline-offset-2 hover:underline" (click)="session.update(t.id, { showAll: true })">{{ 'ai.assistant.showAll' | transloco }}</button>
            }
          </div>
        }
        @if (a.links.length) {
          <div class="flex flex-wrap gap-2 border-t pt-3">
            @for (l of a.links; track l.url) {
              <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="open(l.url)">
                {{ 'ai.assistant.link.' + l.kind | transloco: { text: l.text } }}<ng-icon name="lucideArrowRight" />
              </button>
            }
          </div>
        }

        @if (a.savedQuestionId) {
          <div class="text-muted-foreground flex flex-wrap items-center gap-2 border-t pt-3 text-xs">
            @if (t.feedback === 'up') {
              <span class="text-emerald-700 dark:text-emerald-400">{{ 'ai.assistant.feedback.savedNote' | transloco }}</span>
            } @else if (t.feedback === 'down') {
              <span>{{ 'ai.assistant.feedback.removedNote' | transloco }}</span>
              @if (a.source === 'saved') {
                <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="session.busy()" (click)="session.ask(t.question)"><ng-icon name="lucideSparkles" />{{ 'ai.assistant.feedback.askAi' | transloco }}</button>
              }
            } @else {
              <span>{{ (a.source === 'saved' ? 'ai.assistant.feedback.questionSaved' : 'ai.assistant.feedback.question') | transloco }}</span>
              @if (a.source !== 'saved') {
                <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="session.feedback(t, true)"><ng-icon name="lucideThumbsUp" />{{ 'ai.assistant.feedback.up' | transloco }}</button>
              }
              <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="session.feedback(t, false)"><ng-icon name="lucideThumbsDown" />{{ 'ai.assistant.feedback.down' | transloco }}</button>
            }
          </div>
        }
      </article>
    } @else if (t.cancelled) {
      <p class="text-muted-foreground text-sm italic">{{ 'ai.assistant.cancelled' | transloco }}</p>
    } @else if (t.error) {
      <p class="rounded-md border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-800 dark:text-red-300">{{ t.error }}</p>
    } @else {
      <div class="text-muted-foreground flex flex-wrap items-center gap-3 text-sm">
        <span class="flex items-center gap-2"><hlm-spinner class="size-4" />{{ 'ai.assistant.thinking' | transloco }}</span>
        <button hlmBtn variant="outline" class="h-11 lg:h-8" (click)="session.cancel()"><ng-icon name="lucideSquare" />{{ 'ai.assistant.cancel' | transloco }}</button>
      </div>
    }
  `,
})
export class LibraryTurn {
  readonly session = inject(LibraryAssistantSession);
  private readonly router = inject(Router);

  readonly turn = input.required<LibraryTurnState>();
  /** In the chat bubble's window: fewer rows; following a link minimises the window. */
  readonly compact = input(false);

  readonly firstRows = computed(() => (this.compact() ? 5 : 10));
  readonly args = computed(() => Object.entries(this.turn().answer?.arguments ?? {}).map(([key, value]) => ({ key, value })));
  readonly min = Math.min;

  open(url: string | null): void {
    if (!url) return;
    if (this.compact()) this.session.panelOpen.set(false);
    void this.router.navigateByUrl(url);
  }
}
