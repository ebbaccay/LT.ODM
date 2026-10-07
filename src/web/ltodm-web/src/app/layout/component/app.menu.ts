import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw } from '@ng-icons/lucide';
import { NavConfigService } from '@services/nav-config.service';
import { MENU_ICONS, menuIcon } from '@tms/shared/ui/menu-icons';

interface MenuItem {
  label: string;
  icon: string;
  route: string;
  /** Label is a translation key (nav.garmentQuotation), not plain text. */
  translate: boolean;
}

interface MenuGroup {
  key: string;
  label: string;
  translate: boolean;
  items: MenuItem[];
}

/** Menu text in nav.* is either plain text ("Style Library") or a translation key ("nav.settings"). */
const isTranslationKey = (text: string) => /^[A-Za-z]+(\.[A-Za-z0-9]+)+$/.test(text);

/** Shown when the menu cannot be loaded, so the user can still get home. */
const FALLBACK: MenuGroup[] = [
  { key: 'home', label: 'layout.home', translate: true, items: [{ label: 'layout.dashboard', icon: 'lucideLayoutDashboard', route: '/', translate: true }] },
];

@Component({
  selector: 'app-menu',
  imports: [NgTemplateOutlet, RouterLink, RouterLinkActive, NgIcon, TranslocoPipe],
  providers: [provideIcons({ ...MENU_ICONS, lucideRefreshCw })],
  template: `
    <nav class="flex min-h-full flex-col gap-4 p-3" aria-label="Main">
      @if (nav.loading()) {
        @for (g of [1, 2, 3]; track g) {
          <div class="flex flex-col gap-2 px-3" aria-hidden="true">
            <div class="bg-sidebar-accent h-3 w-24 animate-pulse rounded"></div>
            @for (i of [1, 2, 3]; track i) {
              <div class="bg-sidebar-accent/70 h-7 animate-pulse rounded-md"></div>
            }
          </div>
        }
      } @else {
        @for (group of mainGroups(); track group.key) {
          <ng-container *ngTemplateOutlet="groupTpl; context: { $implicit: group }" />
        }
        @if (nav.failed()) {
          <button
            type="button"
            class="text-muted-foreground hover:text-foreground flex min-h-11 items-center gap-2 px-3 text-left text-xs lg:min-h-9"
            (click)="nav.reload()"
          >
            <ng-icon name="lucideRefreshCw" class="shrink-0" />
            {{ 'layout.menuLoadFailed' | transloco }}
          </button>
        }
        @if (bottomGroups().length) {
          <div class="border-sidebar-border mt-auto flex flex-col gap-4 border-t pt-4">
            @for (group of bottomGroups(); track group.key) {
              <ng-container *ngTemplateOutlet="groupTpl; context: { $implicit: group }" />
            }
          </div>
        }
      }
    </nav>

    <ng-template #groupTpl let-group>
      <div>
        <div class="text-muted-foreground flex items-center gap-2 px-3 pb-1 text-xs font-medium tracking-wide uppercase">
          {{ group.translate ? (group.label | transloco) : group.label }}
        </div>
        <ul class="flex flex-col gap-0.5">
          @for (item of group.items; track item.route) {
            <li>
              <a
                [routerLink]="item.route"
                routerLinkActive="bg-sidebar-accent text-sidebar-accent-foreground font-medium"
                [routerLinkActiveOptions]="{ exact: item.route === '/' }"
                class="hover:bg-sidebar-accent hover:text-sidebar-accent-foreground flex min-h-11 items-center gap-3 rounded-md px-3 text-sm transition-colors lg:min-h-9"
              >
                <ng-icon [name]="item.icon" class="shrink-0 text-base" />
                <span class="truncate">{{ item.translate ? (item.label | transloco) : item.label }}</span>
              </a>
            </li>
          }
        </ul>
      </div>
    </ng-template>
  `,
})
export class AppMenu {
  protected readonly nav = inject(NavConfigService);

  /** Groups and items from nav.* for this user (Settings > Menu). */
  protected readonly mainGroups = computed<MenuGroup[]>(() =>
    this.nav.failed() ? FALLBACK : this.nav.mainGroups().map((g) => this.toMenuGroup(g)),
  );

  protected readonly bottomGroups = computed<MenuGroup[]>(() => this.nav.bottomGroups().map((g) => this.toMenuGroup(g)));

  private toMenuGroup(g: ReturnType<NavConfigService['groups']>[number]): MenuGroup {
    return {
      key: `g${g.groupId}`,
      label: g.text,
      translate: isTranslationKey(g.text),
      items: g.items.map((i) => ({
        label: i.text,
        icon: menuIcon(i.icon),
        route: `/${i.route.replace(/^\/+/, '')}`,
        translate: isTranslationKey(i.text),
      })),
    };
  }
}
