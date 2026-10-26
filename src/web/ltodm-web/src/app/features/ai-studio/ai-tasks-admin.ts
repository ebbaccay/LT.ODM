import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleStop, lucideRefreshCw } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { interval } from 'rxjs';
import { AiJob, AiJobsService, AiServerLoad, isActive } from '../../core/ai/ai-jobs.service';
import { problemMessage } from '../../core/auth/auth.service';

const PAGE = 25;
/** Seconds between refreshes while the list shows running tasks. */
const REFRESH_MS = 10_000;

/**
 * Settings > AI connections > AI tasks (Admin): every user's AI tasks (ai.Job), what is queued or running on the AI
 * server, and how long finished ones are kept. Admins can stop any queued or running task.
 */
@Component({
  selector: 'app-ai-tasks-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmNativeSelectImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCircleStop, lucideRefreshCw })],
  template: `
    <section hlmCard class="gap-0 py-0">
      <div class="flex flex-wrap items-start justify-between gap-3 border-b px-5 py-3">
        <div class="min-w-0">
          <h2 class="font-semibold">{{ 'aiSettings.tasks.title' | transloco }}</h2>
          <p class="text-muted-foreground text-xs">{{ 'aiSettings.tasks.hint' | transloco }}</p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <select hlmNativeSelect class="h-11 lg:h-9" [attr.aria-label]="'aiSettings.tasks.status' | transloco" (change)="setStatus($any($event.target).value)">
            @for (s of statuses; track s) {
              <option [value]="s" [selected]="status() === s">{{ 'aiSettings.tasks.statusName.' + (s || 'all') | transloco }}</option>
            }
          </select>
          <button hlmBtn variant="outline" size="icon" class="size-11 lg:size-9" [attr.aria-label]="'aiSettings.tasks.refresh' | transloco" (click)="load()">
            <ng-icon name="lucideRefreshCw" />
          </button>
        </div>
      </div>

      @if (servers().length) {
        <ul class="flex flex-wrap gap-2 border-b px-5 py-2 text-xs">
          @for (s of servers(); track s.name) {
            <li class="rounded-md border px-2 py-1">
              <span class="font-medium">{{ s.name }}</span>:
              {{ 'aiSettings.tasks.load' | transloco: { running: s.running, limit: s.limit, waiting: s.waitingInteractive + s.waitingBackground } }}
            </li>
          }
        </ul>
      }

      @if (loading() && !rows().length) {
        <div class="flex justify-center px-5 py-6"><hlm-spinner class="size-5" /></div>
      } @else if (!rows().length) {
        <p class="text-muted-foreground px-5 py-6 text-sm">{{ 'aiSettings.tasks.none' | transloco }}</p>
      } @else {
        <ul class="divide-y">
          @for (j of rows(); track j.jobId) {
            <li class="flex flex-wrap items-start gap-x-4 gap-y-1 px-5 py-3 text-sm">
              <div class="min-w-0 flex-1">
                <p class="font-medium">
                  {{ 'aiSettings.tasks.kind.' + j.kind | transloco }}@if (j.label) {<span class="text-muted-foreground font-normal"> · {{ j.label }}</span>}
                </p>
                <p class="text-muted-foreground text-xs">
                  {{ j.ownerName }} · {{ j.createdUtc | date: 'medium' }}
                  @if (seconds(j); as s) { · {{ 'aiSettings.tasks.took' | transloco: { seconds: s } }} }
                  @if (j.cancelledBy) { · {{ 'aiSettings.tasks.cancelledBy' | transloco: { user: j.cancelledBy } }} }
                </p>
                @if (j.error) {
                  <p class="text-destructive mt-0.5 text-xs">{{ j.error }}</p>
                }
              </div>
              <span hlmBadge [variant]="j.status === 'Done' ? 'secondary' : j.status === 'Failed' || j.status === 'Interrupted' ? 'destructive' : isActive(j.status) ? 'default' : 'outline'">
                {{ 'aiSettings.tasks.statusName.' + j.status | transloco }}
              </span>
              @if (isActive(j.status)) {
                <button hlmBtn variant="outline" class="h-11 lg:h-8" [disabled]="busy() === j.jobId" (click)="cancel(j)">
                  <ng-icon name="lucideCircleStop" />{{ 'aiSettings.tasks.stop' | transloco }}
                </button>
              }
            </li>
          }
        </ul>
        @if (total() > rows().length) {
          <div class="border-t px-5 py-2">
            <button hlmBtn variant="ghost" class="h-11 lg:h-9" [disabled]="loading()" (click)="more()">{{ 'aiSettings.tasks.more' | transloco: { shown: rows().length, total: total() } }}</button>
          </div>
        }
      }

      <div class="flex flex-wrap items-center gap-2 border-t px-5 py-3 text-sm">
        <label for="ai-task-keep-days">{{ 'aiSettings.tasks.keepDays' | transloco }}</label>
        <input id="ai-task-keep-days" hlmInput type="number" min="1" max="365" class="h-11 w-24 lg:h-9" [value]="keepDays()" (input)="keepDraft.set(+$any($event.target).value)" />
        <span class="text-muted-foreground">{{ 'aiSettings.tasks.days' | transloco }}</span>
        <button hlmBtn variant="outline" class="h-11 lg:h-9" [disabled]="savingDays() || !keepDraft() || keepDraft() === keepDays()" (click)="saveKeepDays()">
          {{ 'actions.save' | transloco }}
        </button>
      </div>
    </section>
  `,
})
export class AiTasksAdmin implements OnInit {
  private readonly jobs = inject(AiJobsService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDlg = inject(ConfirmDialogService);

  readonly statuses = ['Active', '', 'Done', 'Failed', 'Cancelled', 'Interrupted'];
  readonly status = signal('Active');
  readonly rows = signal<AiJob[]>([]);
  readonly servers = signal<AiServerLoad[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly busy = signal<number | null>(null);
  readonly keepDays = signal(7);
  readonly keepDraft = signal(0);
  readonly savingDays = signal(false);
  readonly isActive = isActive;

  constructor() {
    // While something is queued or running, refresh now and then (cheap: no answers in the list).
    const tick = interval(REFRESH_MS).subscribe(() => {
      if (!this.loading() && (this.rows().some((j) => isActive(j.status)) || this.servers().some((s) => s.running > 0))) this.load();
    });
    inject(DestroyRef).onDestroy(() => tick.unsubscribe());
  }

  ngOnInit(): void {
    this.load();
  }

  setStatus(value: string): void {
    this.status.set(value);
    this.load();
  }

  load(append = false): void {
    this.loading.set(true);
    this.jobs.list(this.status(), append ? this.rows().length : 0, PAGE).subscribe({
      next: (page) => {
        this.rows.set(append ? [...this.rows(), ...page.items] : page.items);
        this.total.set(page.total);
        this.keepDays.set(page.keepDays);
        this.servers.set(page.servers ?? []);
        if (!this.keepDraft()) this.keepDraft.set(page.keepDays);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  more(): void {
    this.load(true);
  }

  async cancel(j: AiJob): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('aiSettings.tasks.stopTitle'),
      message: this.transloco.translate('aiSettings.tasks.stopMessage', { user: j.ownerName, task: this.transloco.translate('aiSettings.tasks.kind.' + j.kind) }),
      confirmLabel: this.transloco.translate('aiSettings.tasks.stop'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.busy.set(j.jobId);
    this.jobs.cancel(j.jobId).subscribe({
      next: () => {
        this.busy.set(null);
        this.load();
      },
      error: (e) => {
        this.busy.set(null);
        toast.error(problemMessage(e));
        this.load();
      },
    });
  }

  saveKeepDays(): void {
    const days = Math.round(this.keepDraft());
    this.savingDays.set(true);
    this.jobs.saveKeepDays(days).subscribe({
      next: () => {
        this.savingDays.set(false);
        this.keepDays.set(days);
        toast.success(this.transloco.translate('aiSettings.tasks.keepSaved', { days }));
      },
      error: (e) => {
        this.savingDays.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  /** Seconds it ran (or has been running), once started. */
  seconds(j: AiJob): number | null {
    if (!j.startedUtc) return null;
    const end = j.finishedUtc ? Date.parse(j.finishedUtc) : Date.now();
    return Math.max(0, Math.round((end - Date.parse(j.startedUtc)) / 1000));
  }
}
