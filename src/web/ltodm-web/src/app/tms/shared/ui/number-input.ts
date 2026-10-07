import { Directive, ElementRef, effect, inject, input, output } from '@angular/core';

/**
 * Two-way number binding for <input type="number"> that does not fight the user while typing.
 *
 *   <input type="number" [appNumber]="line.amount" (appNumberChange)="setAmount($event)" />
 *
 * With a plain [value] binding, typing "5." reports an empty value, the model becomes 0 and "0" is written back
 * into the field, so decimals cannot be typed. This writes the model into the field only when it differs from
 * what the field holds and the user is not halfway through typing; it emits null for an empty field.
 */
@Directive({ selector: 'input[appNumber]', host: { '(input)': 'onInput()', '(blur)': 'write(appNumber())' } })
export class NumberInputDirective {
  readonly appNumber = input<number | null | undefined>(null);
  readonly appNumberChange = output<number | null>();

  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;

  constructor() {
    effect(() => this.write(this.appNumber()));
  }

  protected onInput(): void {
    if (this.el.validity.badInput) return; // e.g. "5." or "-": wait for the next key
    this.appNumberChange.emit(this.el.value === '' ? null : this.el.valueAsNumber);
  }

  protected write(value: number | null | undefined): void {
    const model = value ?? null;
    const typing = document.activeElement === this.el;
    if (typing && (this.el.value === '' || this.el.validity.badInput)) return;
    const current = this.el.value === '' ? null : this.el.valueAsNumber;
    if (current !== model) this.el.value = model === null ? '' : String(model);
  }
}
