import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideKeyRound, lucideShieldAlert } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { AuthService, problemFieldErrors, problemMessage } from '../../core/auth/auth.service';
import { passwordsMatchValidator, strongPasswordValidator } from '../../core/auth/password-rules';
import { PasswordInput } from '../../features/auth/password-input';
import { PasswordStrength } from '../../features/auth/password-strength';

@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, NgIcon, HlmAlertImports, HlmButtonImports, HlmCardImports, HlmFieldImports, HlmSpinnerImports, PasswordInput, PasswordStrength, TranslocoPipe],
  providers: [provideIcons({ lucideCircleAlert, lucideKeyRound, lucideShieldAlert })],
  template: `
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">{{ 'account.changePassword.title' | transloco }}</h1>

    <section hlmCard class="max-w-xl">
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'account.changePassword.cardTitle' | transloco }}</h2>
        <p hlmCardDescription>{{ 'account.changePassword.cardHint' | transloco }}</p>
      </div>
      <div hlmCardContent>
        @if (auth.user()?.mustChangePassword) {
          <div hlmAlert class="mb-5 border-amber-500/40">
            <ng-icon name="lucideShieldAlert" class="text-amber-600" />
            <h3 hlmAlertTitle>{{ 'account.changePassword.mustChangeTitle' | transloco }}</h3>
            <p hlmAlertDescription>{{ 'account.changePassword.mustChangeHint' | transloco }}</p>
          </div>
        }
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
            <label hlmFieldLabel for="currentPassword">{{ 'account.changePassword.current' | transloco }}</label>
            <app-password-input inputId="currentPassword" formControlName="currentPassword" autocomplete="current-password" />
            <hlm-field-error validator="required">{{ 'account.changePassword.currentRequired' | transloco }}</hlm-field-error>
          </div>

          <div hlmField>
            <label hlmFieldLabel for="newPassword">{{ 'auth.password.newPassword' | transloco }}</label>
            <app-password-input inputId="newPassword" formControlName="newPassword" autocomplete="new-password" describedBy="password-rules" />
            <app-password-strength [password]="newPassword() ?? ''" [policy]="auth.passwordPolicy()" [personalInfo]="personalInfo()" />
          </div>

          <div hlmField>
            <label hlmFieldLabel for="confirm">{{ 'auth.password.confirm' | transloco }}</label>
            <app-password-input inputId="confirm" formControlName="confirm" autocomplete="new-password" />
            <hlm-field-error validator="required">{{ 'auth.password.confirmRequired' | transloco }}</hlm-field-error>
            <hlm-field-error validator="mismatch">{{ 'auth.password.mismatch' | transloco }}</hlm-field-error>
          </div>

          <div>
            <button hlmBtn type="submit" class="h-11 lg:h-9" [disabled]="busy()">
              @if (busy()) {
                <hlm-spinner />{{ 'common.saving' | transloco }}
              } @else {
                <ng-icon name="lucideKeyRound" />{{ 'account.changePassword.submit' | transloco }}
              }
            </button>
          </div>
        </form>
      </div>
    </section>
  `,
})
export class ChangePassword implements OnInit {
  protected readonly auth = inject(AuthService);

  protected readonly personalInfo = computed(() => {
    const u = this.auth.user();
    return u ? [u.userName, u.email, u.displayName] : [];
  });

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, strongPasswordValidator(() => this.auth.passwordPolicy(), () => this.personalInfo())]],
      confirm: ['', Validators.required],
    },
    { validators: passwordsMatchValidator('newPassword', 'confirm') },
  );
  protected readonly newPassword = toSignal(this.form.controls.newPassword.valueChanges);

  protected readonly busy = signal(false);
  protected readonly error = signal<string[] | null>(null);

  ngOnInit(): void {
    void this.auth.loadPasswordPolicy();
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const { currentPassword, newPassword } = this.form.getRawValue();
      await this.auth.changePassword(currentPassword, newPassword);
      await this.auth.logout('auth.messages.passwordChanged');
    } catch (e) {
      const fields = problemFieldErrors(e);
      const messages = [...(fields['currentPassword'] ?? []), ...(fields['newPassword'] ?? [])];
      this.error.set(messages.length ? messages : [problemMessage(e)]);
    } finally {
      this.busy.set(false);
    }
  }
}
