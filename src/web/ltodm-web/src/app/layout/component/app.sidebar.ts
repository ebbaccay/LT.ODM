import { Component, computed, inject } from '@angular/core';
import { LayoutService } from '../service/layout.service';
import { AppMenu } from './app.menu';

/** Docked on desktop (static mode); slide-out drawer on phone/tablet and in overlay mode. */
@Component({
  selector: 'app-sidebar',
  imports: [AppMenu],
  template: `
    <aside
      class="bg-sidebar text-sidebar-foreground border-sidebar-border fixed top-14 bottom-0 left-0 z-40 w-64 overflow-y-auto border-r transition-transform duration-200"
      [class.-translate-x-full]="!visible()"
      [attr.aria-hidden]="!visible()"
      [attr.inert]="visible() ? null : ''"
    >
      <app-menu />
    </aside>
  `,
})
export class AppSidebar {
  private readonly layout = inject(LayoutService);
  protected readonly visible = computed(() => this.layout.sidebarDocked() || this.layout.drawerOpen());
}
