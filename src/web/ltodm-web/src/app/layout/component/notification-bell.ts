import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { GqNotification, GqNotificationService } from '@services/gq-notification.service';
import { toneDot, toneText } from '@tms/shared/ui/tones';

/** Garment-quotation notifications (ported from the TMS shell), shown in the top bar. */
@Component({
  selector: 'app-notification-bell',
  imports: [NgIcon, HlmButtonImports, HlmPopoverImports, TranslocoPipe],
  providers: [provideIcons({ lucideBell })],
  template: `
    <hlm-popover align="end" sideOffset="8" (stateChanged)="$event === 'open' && notif.markAllRead()">
      <button
        hlmBtn
        hlmPopoverTrigger
        variant="ghost"
        size="icon"
        class="relative size-11 lg:size-9"
        [attr.aria-label]="'shell.notificationsAriaLabel' | transloco: { count: notif.unreadCount() }"
      >
        <ng-icon name="lucideBell" class="text-lg" />
        @if (notif.unreadCount() > 0) {
          <span class="bg-destructive absolute top-1 right-1 flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-[10px] font-semibold text-white">
            {{ notif.unreadCount() > 99 ? '99+' : notif.unreadCount() }}
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
        @if (notif.all().length === 0) {
          <p class="text-muted-foreground px-4 py-8 text-center text-sm">{{ 'shell.noNotifications' | transloco }}</p>
        } @else {
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
  private readonly router = inject(Router);
  protected readonly toneDot = toneDot;
  protected readonly toneText = toneText;

  ngOnInit(): void {
    void this.notif.init();
  }

  ngOnDestroy(): void {
    // Shell closes on sign-out: drop notifications and the connection.
    void this.notif.reset();
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
