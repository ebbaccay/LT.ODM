import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideInfo, lucideLogIn, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthService, problemMessage } from '../../core/auth/auth.service';
import { PasswordInput } from './password-input';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    HlmAlertImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmFieldImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSpinnerImports,
    PasswordInput,
    TranslocoPipe,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideInfo, lucideLogIn, lucideTriangleAlert })],
  template: `
    <div class="mb-7">
      <h2 class="text-2xl font-semibold tracking-tight">{{ 'auth.login.title' | transloco }}</h2>
      <p class="text-muted-foreground mt-1 text-sm">{{ 'auth.login.subtitle' | transloco }}</p>
    </div>

    @if (notice) {
      <div hlmAlert class="mb-5 border-white/40 bg-white/50 dark:border-white/10 dark:bg-white/5">
        <ng-icon name="lucideInfo" />
        <p hlmAlertDescription>{{ notice | transloco }}</p>
      </div>
    }
    @if (error()) {
      <div hlmAlert variant="destructive" class="mb-5">
        <ng-icon name="lucideCircleAlert" />
        <p hlmAlertDescription>{{ error() }}</p>
      </div>
    }

    <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-5" novalidate>
      <div hlmField>
        <label hlmFieldLabel for="login">{{ 'auth.login.login' | transloco }}</label>
        <input
          hlmInput
          id="login"
          formControlName="login"
          autocomplete="username"
          autocapitalize="off"
          spellcheck="false"
          autofocus
          class="h-11 bg-white/70 lg:h-10 dark:bg-white/5"
        />
        <hlm-field-error validator="required">{{ 'auth.login.loginRequired' | transloco }}</hlm-field-error>
      </div>

      <div hlmField>
        <div class="flex items-center justify-between">
          <label hlmFieldLabel for="password">{{ 'auth.login.password' | transloco }}</label>
          <a routerLink="/forgot-password" class="text-primary text-sm font-medium hover:underline">{{ 'auth.login.forgot' | transloco }}</a>
        </div>
        <app-password-input inputId="password" formControlName="password" autocomplete="current-password" (capsLock)="capsLock.set($event)" />
        @if (capsLock()) {
          <p class="flex items-center gap-1.5 text-xs text-amber-700 dark:text-amber-400" role="status">
            <ng-icon name="lucideTriangleAlert" />{{ 'auth.login.capsLock' | transloco }}
          </p>
        }
        <hlm-field-error validator="required">{{ 'auth.login.passwordRequired' | transloco }}</hlm-field-error>
      </div>

      <div class="flex items-center gap-2.5">
        <hlm-checkbox inputId="remember" formControlName="rememberMe" />
        <label hlmLabel for="remember" class="font-normal">{{ 'auth.login.remember' | transloco }}</label>
      </div>

      <button hlmBtn type="submit" class="shadow-primary/30 h-11 w-full text-base shadow-lg" [disabled]="busy()">
        @if (busy()) {
          <hlm-spinner />{{ 'auth.login.signingIn' | transloco }}
        } @else {
          <ng-icon name="lucideLogIn" />{{ 'auth.login.signIn' | transloco }}
        }
      </button>
    </form>

    <p class="text-muted-foreground mt-7 text-center text-xs">{{ 'auth.login.noAccount' | transloco }}</p>
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = inject(NonNullableFormBuilder).group({
    login: ['', Validators.required],
    password: ['', Validators.required],
    rememberMe: [false],
  });

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly capsLock = signal(false);

  /** Message (translation key) passed by logout / session expiry / password change. */
  protected readonly notice: string | undefined = this.router.currentNavigation()?.extras.state?.['message'] ?? history.state?.message;

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const { login, password, rememberMe } = this.form.getRawValue();
      await this.auth.login(login, password, rememberMe);
      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch (e) {
      this.error.set(problemMessage(e));
      this.form.controls.password.reset();
    } finally {
      this.busy.set(false);
    }
  }

  /** Only same-app paths, never an external URL. */
  private safeReturnUrl(): string {
    const url = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
    return url.startsWith('/') && !url.startsWith('//') ? url : '/';
  }
}
