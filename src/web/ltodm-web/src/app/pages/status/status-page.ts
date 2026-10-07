import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideHouse, lucideLock, lucideSearchX } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';

/** Full-screen status page shared by Not Found, Access Denied and Error. title and message are translation keys. */
@Component({
  selector: 'app-status-page',
  imports: [RouterLink, NgIcon, HlmButtonImports, TranslocoPipe],
  providers: [provideIcons({ lucideSearchX, lucideLock, lucideCircleAlert, lucideHouse })],
  template: `
    <div class="flex min-h-dvh items-center justify-center p-4">
      <div class="flex max-w-md flex-col items-center text-center">
        <div class="mb-6 flex size-16 items-center justify-center rounded-full" [class]="toneClass()">
          <ng-icon [name]="icon()" class="text-3xl" />
        </div>
        @if (code()) {
          <span class="text-muted-foreground mb-1 font-mono text-sm">{{ 'status.errorCode' | transloco: { code: code() } }}</span>
        }
        <h1 class="mb-2 text-3xl font-semibold tracking-tight sm:text-4xl">{{ title() | transloco }}</h1>
        <p class="text-muted-foreground mb-8">{{ message() | transloco }}</p>
        <a hlmBtn routerLink="/" class="h-11 lg:h-9"><ng-icon name="lucideHouse" />{{ 'status.goHome' | transloco }}</a>
      </div>
    </div>
  `,
})
export class StatusPage {
  readonly code = input<string>();
  readonly title = input.required<string>();
  readonly message = input.required<string>();
  readonly icon = input('lucideCircleAlert');
  readonly tone = input<'primary' | 'warning' | 'destructive'>('primary');

  protected toneClass(): string {
    switch (this.tone()) {
      case 'warning':
        return 'bg-amber-500/15 text-amber-600 dark:text-amber-400';
      case 'destructive':
        return 'bg-destructive/15 text-destructive';
      default:
        return 'bg-primary/15 text-primary';
    }
  }
}
