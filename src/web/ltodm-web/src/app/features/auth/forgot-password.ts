import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCircleAlert, lucideMailCheck, lucideSend } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthService, problemMessage } from '../../core/auth/auth.service';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, NgIcon, HlmAlertImports, HlmButtonImports, HlmFieldImports, HlmInputImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideArrowLeft, lucideCircleAlert, lucideMailCheck, lucideSend })],
  template: `
    @if (sentTo(); as email) {
      <div class="flex flex-col items-center text-center">
        <span class="mb-5 flex size-14 items-center justify-center rounded-2xl bg-emerald-500/15 text-emerald-600 dark:text-emerald-400">
          <ng-icon name="lucideMailCheck" class="text-2xl" />
        </span>
        <h2 class="text-2xl font-semibold tracking-tight">{{ 'auth.forgot.checkEmail' | transloco }}</h2>
        <p class="text-muted-foreground mt-2 text-sm">
          {{ 'auth.forgot.sentBefore' | transloco }} <strong class="text-foreground break-all">{{ email }}</strong>{{ 'auth.forgot.sentAfter' | transloco }}
        </p>
        <p class="text-muted-foreground mt-4 text-xs">{{ 'auth.forgot.nothingArrived' | transloco }}</p>
        <a hlmBtn variant="outline" routerLink="/login" class="mt-7 h-11 w-full bg-white/50 dark:bg-white/5"><ng-icon name="lucideArrowLeft" />{{ 'auth.forgot.backToSignIn' | transloco }}</a>
      </div>
    } @else {
      <div class="mb-7">
        <h2 class="text-2xl font-semibold tracking-tight">{{ 'auth.forgot.title' | transloco }}</h2>
        <p class="text-muted-foreground mt-1 text-sm">{{ 'auth.forgot.subtitle' | transloco }}</p>
      </div>

      @if (error()) {
        <div hlmAlert variant="destructive" class="mb-5">
          <ng-icon name="lucideCircleAlert" />
          <p hlmAlertDescription>{{ error() }}</p>
        </div>
      }

      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-5" novalidate>
        <div hlmField>
          <label hlmFieldLabel for="email">{{ 'auth.forgot.email' | transloco }}</label>
          <input hlmInput id="email" type="email" formControlName="email" autocomplete="email" autofocus class="h-11 bg-white/70 lg:h-10 dark:bg-white/5" />
          <hlm-field-error validator="required">{{ 'auth.forgot.emailRequired' | transloco }}</hlm-field-error>
          <hlm-field-error validator="email">{{ 'auth.forgot.emailInvalid' | transloco }}</hlm-field-error>
        </div>

        <button hlmBtn type="submit" class="shadow-primary/30 h-11 w-full text-base shadow-lg" [disabled]="busy()">
          @if (busy()) {
            <hlm-spinner />{{ 'auth.forgot.sending' | transloco }}
          } @else {
            <ng-icon name="lucideSend" />{{ 'auth.forgot.send' | transloco }}
          }
        </button>
      </form>

      <a routerLink="/login" class="text-muted-foreground hover:text-foreground mt-7 flex items-center justify-center gap-1.5 text-sm">
        <ng-icon name="lucideArrowLeft" />{{ 'auth.forgot.backToSignIn' | transloco }}
      </a>
    }
  `,
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly sentTo = signal<string | null>(null);

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const email = this.form.controls.email.value.trim();
      await this.auth.forgotPassword(email);
      this.sentTo.set(email);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
