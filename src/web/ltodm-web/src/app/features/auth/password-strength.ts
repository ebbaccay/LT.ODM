import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCircle } from '@ng-icons/lucide';
import { PasswordPolicy } from '../../core/auth/auth.models';
import { evaluatePassword, passwordScore } from '../../core/auth/password-rules';

/** Strength bar plus a live checklist of the password rules. */
@Component({
  selector: 'app-password-strength',
  imports: [NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideCheck, lucideCircle })],
  template: `
    <div class="flex flex-col gap-2">
      <div class="flex items-center gap-3">
        <div class="grid flex-1 grid-cols-4 gap-1.5" aria-hidden="true">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="h-1.5 rounded-full transition-colors duration-300" [class]="i <= score() ? barColor() : 'bg-foreground/10'"></div>
          }
        </div>
        <span class="w-16 text-right text-xs font-medium" [class]="textColor()" aria-live="polite">{{ score() ? ('auth.password.strength.' + score() | transloco) : '' }}</span>
      </div>
      <ul class="grid grid-cols-1 gap-x-4 gap-y-1 text-xs sm:grid-cols-2" [id]="listId()" [attr.aria-label]="'auth.password.requirements' | transloco">
        @for (rule of rules(); track rule.id) {
          <li class="flex items-center gap-1.5 transition-colors" [class]="rule.passed ? 'text-emerald-600 dark:text-emerald-400' : 'text-muted-foreground'">
            <ng-icon [name]="rule.passed ? 'lucideCheck' : 'lucideCircle'" class="shrink-0 text-sm" />
            <span>{{ 'auth.password.rules.' + rule.id | transloco: rule.params }}<span class="sr-only"> {{ (rule.passed ? 'auth.password.met' : 'auth.password.notMet') | transloco }}</span></span>
          </li>
        }
      </ul>
    </div>
  `,
})
export class PasswordStrength {
  readonly password = input('');
  readonly policy = input.required<PasswordPolicy>();
  readonly personalInfo = input<string[]>([]);
  readonly listId = input('password-rules');

  protected readonly rules = computed(() => evaluatePassword(this.password(), this.policy(), this.personalInfo()));
  protected readonly score = computed(() => passwordScore(this.password(), this.rules()));

  protected readonly barColor = computed(() => ['', 'bg-destructive', 'bg-amber-500', 'bg-emerald-500', 'bg-emerald-600'][this.score()]);
  protected readonly textColor = computed(
    () => ['', 'text-destructive', 'text-amber-600 dark:text-amber-400', 'text-emerald-600 dark:text-emerald-400', 'text-emerald-600 dark:text-emerald-400'][this.score()],
  );
}
