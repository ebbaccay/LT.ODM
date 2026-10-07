import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToasterImports } from '@spartan-ng/helm/sonner';
import { LayoutService } from './layout/service/layout.service';
import { LanguageService } from './tms/services/language.service';
import { ConfirmDialogComponent } from './tms/shared/confirm-dialog/confirm-dialog.component';

@Component({
  imports: [RouterOutlet, HlmToasterImports, ConfirmDialogComponent],
  selector: 'app-root',
  template: `
    <router-outlet />
    <hlm-toaster [theme]="layout.isDark() ? 'dark' : 'light'" richColors />
    <!-- ConfirmDialogService (used by modules ported from TMS) -->
    <app-confirm-dialog />
  `,
})
export class App {
  // Created here so the saved theme is applied on every page, including the status pages.
  protected readonly layout = inject(LayoutService);

  constructor() {
    // Saved (or browser) language, before the first page renders.
    inject(LanguageService).init();
  }
}
