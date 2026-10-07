import { Component, booleanAttribute, inject, input, output, signal } from '@angular/core';
import { ControlValueAccessor, NgControl } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff } from '@ng-icons/lucide';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';

/** Password field with show/hide toggle and Caps Lock detection. Works with formControlName. */
@Component({
  selector: 'app-password-input',
  imports: [NgIcon, HlmInputGroupImports, TranslocoPipe],
  providers: [provideIcons({ lucideEye, lucideEyeOff })],
  template: `
    <div hlmInputGroup class="h-11 bg-white/70 dark:bg-white/5 lg:h-10">
      <input
        hlmInputGroupInput
        [id]="inputId()"
        [type]="visible() ? 'text' : 'password'"
        [attr.autocomplete]="autocomplete()"
        [attr.aria-invalid]="invalid() || null"
        [attr.data-matches-spartan-invalid]="invalid() || null"
        [attr.aria-describedby]="describedBy() || null"
        [value]="value()"
        [disabled]="disabled()"
        [autofocus]="autofocus()"
        autocapitalize="off"
        spellcheck="false"
        (input)="onInput($event)"
        (blur)="onTouched()"
        (keydown)="checkCapsLock($event)"
        (keyup)="checkCapsLock($event)"
      />
      <div hlmInputGroupAddon align="inline-end">
        <button
          hlmInputGroupButton
          type="button"
          size="icon-sm"
          [attr.aria-label]="(visible() ? 'auth.password.hide' : 'auth.password.show') | transloco"
          [attr.aria-pressed]="visible()"
          (click)="visible.set(!visible())"
        >
          <ng-icon [name]="visible() ? 'lucideEyeOff' : 'lucideEye'" />
        </button>
      </div>
    </div>
  `,
})
export class PasswordInput implements ControlValueAccessor {
  private readonly ngControl = inject(NgControl, { self: true, optional: true });

  readonly inputId = input.required<string>();
  readonly autocomplete = input<'current-password' | 'new-password'>('current-password');
  readonly describedBy = input<string>();
  readonly autofocus = input(false, { transform: booleanAttribute });
  readonly capsLock = output<boolean>();

  protected readonly value = signal('');
  protected readonly disabled = signal(false);
  protected readonly visible = signal(false);

  private onChange: (value: string) => void = () => {};
  protected onTouched: () => void = () => {};

  constructor() {
    if (this.ngControl) this.ngControl.valueAccessor = this;
  }

  protected invalid(): boolean {
    const c = this.ngControl;
    return !!c && !!c.invalid && (!!c.touched || !!c.dirty);
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.value.set(value);
    this.onChange(value);
  }

  protected checkCapsLock(event: KeyboardEvent): void {
    if (typeof event.getModifierState === 'function') this.capsLock.emit(event.getModifierState('CapsLock'));
  }

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }
}
