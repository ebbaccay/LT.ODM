import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleUser, lucideDownload, lucideKeyRound, lucideLogOut, lucideMenu, lucideMoon, lucidePalette, lucideSun } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { AuthService } from '../../core/auth/auth.service';
import { PwaService } from '../../core/pwa/pwa.service';
import { LayoutService } from '../service/layout.service';
import { AppConfigurator } from './app.configurator';
import { LanguageSwitcher } from './language-switcher';
import { NotificationBell } from './notification-bell';

@Component({
  selector: 'app-topbar',
  imports: [RouterLink, NgIcon, HlmButtonImports, HlmDropdownMenuImports, HlmPopoverImports, HlmTooltipImports, TranslocoPipe, AppConfigurator, LanguageSwitcher, NotificationBell],
  providers: [provideIcons({ lucideMenu, lucideDownload, lucideSun, lucideMoon, lucidePalette, lucideCircleUser, lucideKeyRound, lucideLogOut })],
  template: `
    <header class="bg-background/80 border-border fixed inset-x-0 top-0 z-50 flex h-14 items-center gap-1 border-b px-2 backdrop-blur sm:px-4">
      <button hlmBtn variant="ghost" size="icon" class="size-11 lg:size-9" [attr.aria-label]="'layout.toggleMenu' | transloco" (click)="layout.onMenuToggle()">
        <ng-icon name="lucideMenu" class="text-lg" />
      </button>
      <a routerLink="/" class="mr-auto flex min-w-0 items-center gap-2 rounded-md px-1 font-semibold">
        <img src="icons/icon-72x72.png" alt="" width="28" height="28" class="size-7 shrink-0 rounded-md" />
        <span class="truncate">LT ODM <span class="text-muted-foreground hidden font-normal sm:inline">{{ 'layout.appSubtitle' | transloco }}</span></span>
      </a>

      @if (pwa.canInstall()) {
        <button hlmBtn variant="outline" size="sm" class="h-11 lg:h-8" (click)="pwa.install()">
          <ng-icon name="lucideDownload" />
          <span class="hidden sm:inline">{{ 'layout.installApp' | transloco }}</span>
        </button>
      }

      <app-notification-bell />

      <app-language-switcher />

      <button
        hlmBtn
        variant="ghost"
        size="icon"
        class="size-11 lg:size-9"
        [attr.aria-label]="(layout.isDark() ? 'layout.switchToLight' : 'layout.switchToDark') | transloco"
        [hlmTooltip]="(layout.isDark() ? 'layout.lightMode' : 'layout.darkMode') | transloco"
        (click)="layout.toggleDarkMode()"
      >
        <ng-icon [name]="layout.isDark() ? 'lucideSun' : 'lucideMoon'" class="text-lg" />
      </button>

      <hlm-popover align="end" sideOffset="8">
        <button hlmBtn hlmPopoverTrigger variant="ghost" size="icon" class="size-11 lg:size-9" [attr.aria-label]="'layout.themeSettings' | transloco" [hlmTooltip]="'layout.theme' | transloco">
          <ng-icon name="lucidePalette" class="text-lg" />
        </button>
        <hlm-popover-content *hlmPopoverPortal="let ctx" class="w-80 max-w-[calc(100vw-1rem)]">
          <app-configurator />
        </hlm-popover-content>
      </hlm-popover>

      <button hlmBtn variant="ghost" size="icon" class="size-11 lg:size-9" [attr.aria-label]="'layout.account' | transloco" [hlmDropdownMenuTrigger]="account" align="end">
        <ng-icon name="lucideCircleUser" class="text-lg" />
      </button>
      <ng-template #account>
        <hlm-dropdown-menu class="w-60">
          @if (auth.user(); as user) {
            <hlm-dropdown-menu-label class="flex flex-col gap-0.5">
              <span class="truncate">{{ user.displayName }}</span>
              <span class="text-muted-foreground truncate text-xs font-normal">{{ user.email }}</span>
            </hlm-dropdown-menu-label>
            <hlm-dropdown-menu-separator />
          }
          <button hlmDropdownMenuItem routerLink="/account/password">
            <ng-icon name="lucideKeyRound" />
            {{ 'shell.changePassword' | transloco }}
          </button>
          <button hlmDropdownMenuItem (click)="auth.logout('auth.messages.signedOut')">
            <ng-icon name="lucideLogOut" />
            {{ 'shell.signOut' | transloco }}
          </button>
        </hlm-dropdown-menu>
      </ng-template>
    </header>
  `,
})
export class AppTopbar {
  protected readonly layout = inject(LayoutService);
  protected readonly pwa = inject(PwaService);
  protected readonly auth = inject(AuthService);
}
