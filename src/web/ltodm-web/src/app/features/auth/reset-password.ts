import { Component, OnInit, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCircleAlert, lucideKeyRound, lucideLink2Off } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { toast } from '@spartan-ng/brain/sonner';
import { AuthService, problemFieldErrors, problemMessage } from '../../core/auth/auth.service';
import { passwordsMatchValidator, strongPasswordValidator } from '../../core/auth/password-rules';
import { PasswordInput } from './password-input';
import { PasswordStrength } from './password-strength';

type State = 'checking' | 'invalid' | 'ready';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink, NgIcon, HlmAlertImports, HlmButtonImports, HlmFieldImports, HlmSpinnerImports, PasswordInput, PasswordStrength, TranslocoPipe],
  providers: [provideIcons({ lucideArrowLeft, lucideCircleAlert, lucideKeyRound, lucideLink2Off })],
  template: `
    @switch (state()) {
      @case ('checking') {
        <div class="flex flex-col items-center gap-3 py-10 text-center">
          <hlm-spinner class="text-2xl" />
          <p class="text-muted-foreground text-sm">{{ 'auth.reset.checking' | transloco }}</p>
        </div>
      }
      @case ('invalid') {
        <div class="flex flex-col items-center text-center">
          <span class="bg-destructive/15 text-destructive mb-5 flex size-14 items-center justify-center rounded-2xl">
            <ng-icon name="lucideLink2Off" class="text-2xl" />
          </span>
          <h2 class="text-2xl font-semibold tracking-tight">{{ 'auth.reset.expiredTitle' | transloco }}</h2>
          <p class="text-muted-foreground mt-2 text-sm">
            {{ (welcome ? 'auth.reset.expiredWelcome' : 'auth.reset.expired') | transloco }}
          </p>
          <a hlmBtn routerLink="/forgot-password" class="mt-7 h-11 w-full">{{ 'auth.reset.requestNew' | transloco }}</a>
          <a routerLink="/login" class="text-muted-foreground hover:text-foreground mt-4 flex items-center gap-1.5 text-sm"><ng-icon name="lucideArrowLeft" />{{ 'auth.forgot.backToSignIn' | transloco }}</a>
        </div>
      }
      @case ('ready') {
        <div class="mb-7">
          @if (welcome) {
            <h2 class="text-2xl font-semibold tracking-tight">{{ 'auth.reset.welcomeTitle' | transloco }}</h2>
            <p class="text-muted-foreground mt-1 text-sm">{{ 'auth.reset.welcomeSubtitle' | transloco }}</p>
          } @else {
            <h2 class="text-2xl font-semibold tracking-tight">{{ 'auth.reset.title' | transloco }}</h2>
            <p class="text-muted-foreground mt-1 text-sm">{{ 'auth.reset.subtitle' | transloco }}</p>
          }
        </div>

        @if (error()) {
          <div hlmAlert variant="destructive" class="mb-5">
            <ng-icon name="lucideCircleAlert" />
            <div hlmAlertDescription>
              @for (e of error(); track e) {
                <p>{{ e }}</p>
              }
            </div>
          </div>
        }

        <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-5" novalidate>
          <div hlmField>
            <label hlmFieldLabel for="newPassword">{{ 'auth.password.newPassword' | transloco }}</label>
            <app-password-input inputId="newPassword" formControlName="newPassword" autocomplete="new-password" describedBy="password-rules" autofocus />
            <app-password-strength [password]="newPassword() ?? ''" [policy]="auth.passwordPolicy()" />
          </div>

          <div hlmField>
            <label hlmFieldLabel for="confirm">{{ 'auth.password.confirm' | transloco }}</label>
            <app-password-input inputId="confirm" formControlName="confirm" autocomplete="new-password" />
            <hlm-field-error validator="required">{{ 'auth.password.confirmRequired' | transloco }}</hlm-field-error>
            <hlm-field-error validator="mismatch">{{ 'auth.password.mismatch' | transloco }}</hlm-field-error>
          </div>

          <button hlmBtn type="submit" class="shadow-primary/30 h-11 w-full text-base shadow-lg" [disabled]="busy()">
            @if (busy()) {
              <hlm-spinner />{{ 'common.saving' | transloco }}
            } @else {
              <ng-icon name="lucideKeyRound" />{{ (welcome ? 'auth.reset.setPassword' : 'auth.reset.resetPassword') | transloco }}
            }
          </button>
        </form>
      }
    }
  `,
})
export class ResetPassword implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);
  private token = '';
  /** Link from a new-account email (?welcome=1): same flow, welcome wording. */
  protected welcome = false;

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      newPassword: ['', [Validators.required, strongPasswordValidator(() => this.auth.passwordPolicy())]],
      confirm: ['', Validators.required],
    },
    { validators: passwordsMatchValidator('newPassword', 'confirm') },
  );
  protected readonly newPassword = toSignal(this.form.controls.newPassword.valueChanges);

  protected readonly state = signal<State>('checking');
  protected readonly busy = signal(false);
  protected readonly error = signal<string[] | null>(null);

  async ngOnInit(): Promise<void> {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    this.welcome = this.route.snapshot.queryParamMap.get('welcome') === '1';
    // Remove the token from the address bar and browser history.
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });

    void this.auth.loadPasswordPolicy();
    if (!this.token) {
      this.state.set('invalid');
      return;
    }
    try {
      await this.auth.validateResetToken(this.token);
      this.state.set('ready');
    } catch {
      this.state.set('invalid');
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.resetPassword(this.token, this.form.controls.newPassword.value);
      toast.success(this.transloco.translate(this.welcome ? 'auth.reset.passwordSet' : 'auth.reset.passwordChanged'), {
        description: this.transloco.translate('auth.reset.signInWithNew'),
      });
      await this.router.navigate(['/login'], {
        state: { message: this.welcome ? 'auth.reset.accountReady' : 'auth.reset.resetDone' },
      });
    } catch (e) {
      const fieldErrors = problemFieldErrors(e)['newPassword'];
      if (fieldErrors?.length) {
        this.error.set(fieldErrors);
      } else {
        // Token no longer valid (used / expired meanwhile).
        this.error.set([problemMessage(e)]);
        this.state.set('invalid');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
