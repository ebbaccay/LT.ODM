import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell, lucideClock, lucidePackage, lucideThumbsDown, lucideThumbsUp } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { GqNotification, GqNotificationService } from '@services/gq-notification.service';
import { toneDot, toneText } from '@tms/shared/ui/tones';
import { PackageNoticeService } from '../../features/style-packages/package-notice.service';

/** The top bar's bell: Style Packages notices (clients answering through share links), then garment-quotation notifications (ported from the TMS shell). */
@Component({
  selector: 'app-notification-bell',
  imports: [DatePipe, NgIcon, HlmButtonImports, HlmPopoverImports, TranslocoPipe],
  providers: [provideIcons({ lucideBell, lucideClock, lucidePackage, lucideThumbsDown, lucideThumbsUp })],
  template: `
    <hlm-popover align="end" sideOffset="8" (stateChanged)="$event === 'open' && opened()">
      <button
        hlmBtn
        hlmPopoverTrigger
        variant="ghost"
        size="icon"
        class="relative size-11 lg:size-9"
        [attr.aria-label]="'shell.notificationsAriaLabel' | transloco: { count: unread() }"
      >
        <ng-icon name="lucideBell" class="text-lg" />
        @if (unread() > 0) {
          <span class="bg-destructive absolute top-1 right-1 flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-[10px] font-semibold text-white">
            {{ unread() > 99 ? '99+' : unread() }}
          </span>
        }
      </button>
      <hlm-popover-content *hlmPopoverPortal="let ctx" class="w-96 max-w-[calc(100vw-1rem)] gap-0 p-0">
        <div class="border-border flex items-center justify-between border-b px-4 py-3">
          <span class="text-sm font-semibold">{{ 'shell.notifications' | transloco }}</span>
          @if (notif.all().length > 0) {
            <button hlmBtn variant="ghost" size="sm" type="button" (click)="notif.clear()">{{ 'shell.clearAll' | transloco }}</button>
          }
        </div>
        @if (packages.followUps().length) {
          <!-- Follow-up reminders (roadmap 5.24): stay until Done or Later -->
          <p class="text-muted-foreground border-b px-4 pt-2 pb-1 text-[11px] font-semibold tracking-wide uppercase">{{ 'followUps.title' | transloco }}</p>
          <ul class="divide-border max-h-[40vh] divide-y overflow-y-auto border-b">
            @for (f of packages.followUps().slice(0, 10); track f.kind + f.refKey) {
              <li class="flex gap-3 px-4 py-3">
                <ng-icon name="lucideClock" class="mt-0.5 shrink-0 text-amber-600 dark:text-amber-400" />
                <span class="min-w-0 flex-1">
                  <button type="button" class="block w-full text-left outline-none hover:underline" (click)="ctx.close(); packages.openFollowUp(f)">
                    <span class="block truncate text-sm font-medium">{{ f.packageName || f.clientKey }}</span>
                    <span class="block text-xs">{{ 'followUps.' + f.kind | transloco: { days: f.days, label: f.shareLabel, styleNo: f.styleNo, season: f.seasonCode ?? '', date: (f.dueDate | date: 'mediumDate') } }}</span>
                    @if (f.note) { <span class="text-muted-foreground block truncate text-xs">{{ f.note }}</span> }
                  </button>
                  <span class="mt-1 flex gap-2">
                    <button hlmBtn variant="outline" size="sm" type="button" class="h-8" (click)="packages.dismiss(f, null)">{{ 'followUps.done' | transloco }}</button>
                    <button hlmBtn variant="ghost" size="sm" type="button" class="h-8" (click)="packages.dismiss(f, 3)">{{ 'followUps.later' | transloco }}</button>
                  </span>
                </span>
              </li>
            }
          </ul>
        }
        @if (packages.notices().length) {
          <!-- Style Packages: clients answered through share links -->
          <ul class="divide-border max-h-[40vh] divide-y overflow-y-auto border-b">
            @for (p of packages.notices().slice(0, 10); track p.noticeId) {
              <li>
                <button type="button" class="hover:bg-accent focus-visible:bg-accent flex w-full gap-3 px-4 py-3 text-left outline-none" [class.bg-primary/5]="!p.isRead"
                        (click)="ctx.close(); packages.open(p)">
                  <ng-icon name="lucidePackage" class="text-primary mt-0.5 shrink-0" />
                  <span class="min-w-0 flex-1">
                    <span class="flex items-center justify-between gap-2 text-xs">
                      <span class="text-primary font-semibold">{{ 'packages.notice.title' | transloco }}</span>
                      <span class="text-muted-foreground shrink-0">{{ p.lastUtc | date: 'MMM d, HH:mm' }}</span>
                    </span>
                    <span class="block truncate text-sm font-medium">{{ p.packageName }}</span>
                    <span class="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs">
                      <span class="text-muted-foreground">{{ p.clientName || p.shareLabel }}:</span>
                      @if (p.likes) { <span class="flex items-center gap-0.5"><ng-icon name="lucideThumbsUp" />{{ p.likes }}</span> }
                      @if (p.passes) { <span class="flex items-center gap-0.5"><ng-icon name="lucideThumbsDown" />{{ p.passes }}</span> }
                      @if (p.comments) { <span>{{ 'packages.share.comments' | transloco: { count: p.comments } }}</span> }
                      @if (p.samples) { <span class="font-medium text-amber-700 dark:text-amber-400">{{ 'packages.share.samples' | transloco: { count: p.samples } }}</span> }
                    </span>
                  </span>
                </button>
              </li>
            }
          </ul>
        }
        @if (notif.all().length === 0 && !packages.notices().length && !packages.followUps().length) {
          <p class="text-muted-foreground px-4 py-8 text-center text-sm">{{ 'shell.noNotifications' | transloco }}</p>
        } @else if (notif.all().length) {
          <ul class="divide-border max-h-[60vh] divide-y overflow-y-auto">
            @for (n of notif.all(); track n.id) {
              <li>
                <button
                  type="button"
                  class="hover:bg-accent focus-visible:bg-accent flex w-full gap-3 px-4 py-3 text-left outline-none"
                  [class.bg-primary/5]="!n.read"
                  (click)="open(n)"
                >
                  <span class="mt-1.5 size-2 shrink-0 rounded-full" [class]="toneDot(notif.typeTone(n.type))"></span>
                  <span class="min-w-0 flex-1">
                    <span class="flex items-center justify-between gap-2 text-xs">
                      <span class="font-semibold" [class]="toneText(notif.typeTone(n.type))">{{ 'notif.types.' + n.type | transloco }}</span>
                      <span class="text-muted-foreground shrink-0">{{ n.ts }}</span>
                    </span>
                    <span class="block truncate text-sm font-medium">{{ n.styleId }}@if (n.packName) { · {{ n.packName }} }</span>
                    @if (n.factoryName) {
                      <span class="text-muted-foreground block truncate text-xs">{{ n.factoryName }}</span>
                    }
                    <span class="mt-0.5 block text-xs">{{ 'notif.messages.' + n.type | transloco: { styleId: n.styleId, factory: n.factoryName } }}</span>
                    <span class="text-muted-foreground block text-xs">— {{ n.sender }}</span>
                  </span>
                </button>
              </li>
            }
          </ul>
        }
      </hlm-popover-content>
    </hlm-popover>
  `,
})
export class NotificationBell implements OnInit, OnDestroy {
  protected readonly notif = inject(GqNotificationService);
  protected readonly packages = inject(PackageNoticeService);
  /** Unread notices plus open follow-ups (they count until Done or Later). */
  protected readonly unread = computed(() => this.notif.unreadCount() + this.packages.unreadCount() + this.packages.followUps().length);
  private readonly router = inject(Router);
  protected readonly toneDot = toneDot;
  protected readonly toneText = toneText;

  ngOnInit(): void {
    // The garment-quotation service starts the hub; package notices listen on the same connection.
    void this.notif.init().finally(() => this.packages.init());
  }

  protected opened(): void {
    this.markAllRead();
    this.packages.loadFollowUps();
  }

  protected markAllRead(): void {
    this.notif.markAllRead();
    this.packages.markAllRead();
  }

  ngOnDestroy(): void {
    // Shell closes on sign-out: drop notifications and the connection.
    void this.notif.reset();
    this.packages.reset();
  }

  /** Same deep link as TMS: open the style on the costing tab. */
  protected open(n: GqNotification): void {
    this.notif.markRead(n.id);
    if (n.styleId && n.packName) {
      void this.router.navigate(['/garment-quotation'], {
        queryParams: { pkg: n.packName, styleId: n.styleId, factoryId: n.factoryId, tab: 'costing' },
      });
    }
  }
}
