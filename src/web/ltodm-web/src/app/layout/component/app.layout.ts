import { Component, inject } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideWifiOff } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { filter } from 'rxjs';
import { PwaService } from '../../core/pwa/pwa.service';
import { LayoutService } from '../service/layout.service';
import { AppFooter } from './app.footer';
import { AppSidebar } from './app.sidebar';
import { AppTopbar } from './app.topbar';

@Component({
  selector: 'app-layout',
  imports: [RouterOutlet, NgIcon, TranslocoPipe, HlmAlertImports, HlmButtonImports, AppTopbar, AppSidebar, AppFooter],
  providers: [provideIcons({ lucideRefreshCw, lucideWifiOff })],
  template: `
    <!-- Glass theme: a still, soft glow of the accent and its companion colours behind every page (decorative). -->
    @if (layout.config().glass) {
      <div class="glass-glow pointer-events-none fixed inset-0 -z-10 overflow-hidden" aria-hidden="true">
        <div class="bg-dash-1/25 dark:bg-dash-1/35 absolute -top-40 left-[10%] size-[36rem] rounded-full blur-3xl"></div>
        <div class="bg-dash-2/20 dark:bg-dash-2/25 absolute top-[25%] -right-40 size-[40rem] rounded-full blur-3xl"></div>
        <div class="bg-dash-4/15 dark:bg-dash-4/25 absolute bottom-[-10rem] left-[-8rem] size-[32rem] rounded-full blur-3xl"></div>
        <div class="bg-dash-3/15 dark:bg-dash-3/15 absolute -bottom-48 left-[45%] size-[30rem] rounded-full blur-3xl"></div>
      </div>
    }

    <!-- Screen chrome is not printed (Generate PDF / Export PDF print only the page content). -->
    <app-topbar class="print:hidden" />
    <app-sidebar class="print:hidden" />

    @if (layout.drawerOpen()) {
      <div class="fixed inset-0 top-14 z-30 bg-black/40 backdrop-blur-[1px]" (click)="layout.closeDrawer()"></div>
    }

    <div class="flex min-h-dvh flex-col pt-14 transition-[padding] duration-200 print:!p-0" [class.lg:pl-64]="layout.sidebarDocked()">
      <main class="mx-auto w-full max-w-[1600px] flex-1 p-4 sm:p-6 print:max-w-none print:p-0">
        @if (pwa.updateAvailable()) {
          <div hlmAlert class="mb-4">
            <div hlmAlertTitle>{{ 'layout.newVersion' | transloco }}</div>
            <div hlmAlertDescription>{{ 'layout.newVersionHint' | transloco }}</div>
            <div hlmAlertAction>
              <button hlmBtn size="sm" (click)="pwa.reload()"><ng-icon name="lucideRefreshCw" />{{ 'layout.reload' | transloco }}</button>
            </div>
          </div>
        }
        @if (pwa.showIosHint()) {
          <div hlmAlert class="mb-4">
            <div hlmAlertDescription>{{ 'layout.iosHint' | transloco }}</div>
            <div hlmAlertAction>
              <button hlmBtn variant="ghost" size="sm" (click)="pwa.dismissIosHint()">{{ 'layout.dismiss' | transloco }}</button>
            </div>
          </div>
        }
        <router-outlet />
      </main>
      <app-footer class="print:hidden" />
    </div>

    @if (!pwa.online()) {
      <div class="bg-background fixed inset-0 z-[100] flex flex-col items-center justify-center gap-2 p-6 text-center" role="alert">
        <ng-icon name="lucideWifiOff" class="text-muted-foreground mb-4 text-5xl" />
        <h1 class="text-2xl font-semibold">{{ 'layout.offlineTitle' | transloco }}</h1>
        <p class="text-muted-foreground">{{ 'layout.offlineHint' | transloco }}</p>
      </div>
    }
  `,
})
export class AppLayout {
  protected readonly layout = inject(LayoutService);
  protected readonly pwa = inject(PwaService);

  constructor() {
    // Close the slide-out menu after navigating.
    inject(Router)
      .events.pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.layout.closeDrawer());
  }
}
