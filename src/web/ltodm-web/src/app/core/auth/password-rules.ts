import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { PasswordPolicy } from './auth.models';

/** Shown as auth.password.rules.<id> (with params) in the UI language. */
export interface PasswordRule {
  id: string;
  params?: Record<string, unknown>;
  passed: boolean;
}

/**
 * Instant feedback while typing. The API (PasswordPolicy.cs) is the source of truth and also rejects
 * common passwords, sequences, repeats and recently used passwords.
 */
export function evaluatePassword(password: string, policy: PasswordPolicy, personalInfo: string[] = []): PasswordRule[] {
  const rules: PasswordRule[] = [
    { id: 'length', params: { min: policy.minLength }, passed: password.length >= policy.minLength && password.length <= policy.maxLength },
  ];
  if (policy.requireUppercase) rules.push({ id: 'upper', passed: /\p{Lu}/u.test(password) });
  if (policy.requireLowercase) rules.push({ id: 'lower', passed: /\p{Ll}/u.test(password) });
  if (policy.requireDigit) rules.push({ id: 'digit', passed: /\d/.test(password) });
  if (policy.requireSymbol) rules.push({ id: 'symbol', passed: /[^\p{L}\p{N}]/u.test(password) });

  const parts = personalInfo
    .filter((v) => !!v)
    .flatMap((v) => v.toLowerCase().split(/[@._\-\s]+/))
    .filter((p) => p.length >= 3);
  if (parts.length > 0) {
    const lower = password.toLowerCase();
    rules.push({ id: 'personal', passed: password.length > 0 && !parts.some((p) => lower.includes(p)) });
  }
  return rules;
}

/** 0 (empty) to 4 (strong): rules met, plus credit for extra length. */
export function passwordScore(password: string, rules: PasswordRule[]): number {
  if (!password) return 0;
  const ratio = rules.filter((r) => r.passed).length / rules.length;
  if (ratio < 0.6) return 1;
  if (ratio < 1) return 2;
  return password.length >= 16 ? 4 : 3;
}

/** Form validator: { passwordRules: [...failed rule ids] } until every rule passes. */
export function strongPasswordValidator(policy: () => PasswordPolicy, personalInfo: () => string[] = () => []): ValidatorFn {
  return (control: AbstractControl<string>): ValidationErrors | null => {
    const failed = evaluatePassword(control.value ?? '', policy(), personalInfo()).filter((r) => !r.passed);
    return failed.length === 0 ? null : { passwordRules: failed.map((r) => r.id) };
  };
}

/** Group validator for "confirm password". */
export function passwordsMatchValidator(passwordKey: string, confirmKey: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const confirm = group.get(confirmKey);
    if (!confirm) return null;
    const mismatch = !!confirm.value && group.get(passwordKey)?.value !== confirm.value;
    const errors = { ...(confirm.errors ?? {}) };
    if (mismatch) errors['mismatch'] = true;
    else delete errors['mismatch'];
    confirm.setErrors(Object.keys(errors).length ? errors : null);
    return null;
  };
}
