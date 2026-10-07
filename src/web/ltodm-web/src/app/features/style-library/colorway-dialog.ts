import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { ImageField } from './image-field';
import { Colorway, ColorwayStatus, StyleLibraryService, styleError, toNumber } from './style-library.service';

/** Add (colorway null) or edit a colorway: code (the customer's article number), name, status, order and image. */
@Component({
  selector: 'app-colorway-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, ImageField, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmNativeSelectImports, HlmSpinnerImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" [heading]="colorway() ? ('styles.colorway.editTitle' | transloco: { code: colorway()!.colorwayCode }) : ('styles.colorway.addTitle' | transloco)" (closed)="closed.emit()">
      @let e = errors();
      <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cw-code">{{ 'styles.colorway.code' | transloco }}</label>
            <input hlmInput id="cw-code" class="h-11 font-mono lg:h-9" maxlength="20" autocomplete="off" [value]="code()" (input)="code.set($any($event.target).value)" [attr.aria-invalid]="!!e['colorwayCode']" />
            @if (e['colorwayCode']) { <p class="text-destructive text-xs">{{ e['colorwayCode'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cw-status">{{ 'styles.colorway.status' | transloco }}</label>
            <hlm-native-select selectId="cw-status" selectClass="h-11 w-full lg:h-9" [value]="status()" (valueChange)="status.set($any($event) ?? 'INRANGE')">
              <option hlmNativeSelectOption value="INRANGE">{{ 'styles.detail.inRange' | transloco }}</option>
              <option hlmNativeSelectOption value="DROPPED">{{ 'styles.detail.dropped' | transloco }}</option>
            </hlm-native-select>
          </div>
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="cw-name">{{ 'styles.colorway.name' | transloco }}</label>
          <input hlmInput id="cw-name" class="h-11 lg:h-9" maxlength="150" [placeholder]="'styles.colorway.namePlaceholder' | transloco" [value]="name()" (input)="name.set($any($event.target).value)" />
        </div>
        <div class="grid grid-cols-[minmax(0,1fr)_8rem] items-start gap-4">
          <app-image-field [label]="'styles.colorway.image' | transloco" [url]="imageUrl()" [editable]="true" (changed)="imageUrl.set($event)" />
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cw-order">{{ 'styles.colorway.order' | transloco }}</label>
            <input hlmInput id="cw-order" type="number" min="0" inputmode="numeric" class="h-11 lg:h-9" [placeholder]="'styles.colorway.orderPlaceholder' | transloco" [value]="sortOrder()" (input)="sortOrder.set($any($event.target).value)" />
          </div>
        </div>
        <button type="submit" class="hidden"></button>
      </form>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ (colorway() ? 'actions.save' : 'styles.colorway.add') | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class ColorwayDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly styleId = input.required<number>();
  readonly colorway = input<Colorway | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly code = signal('');
  readonly name = signal('');
  readonly status = signal<ColorwayStatus>('INRANGE');
  readonly sortOrder = signal('');
  readonly imageUrl = signal<string | null>(null);
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const c = this.colorway();
      this.code.set(c?.colorwayCode ?? '');
      this.name.set(c?.colorwayName ?? '');
      this.status.set(c?.status ?? 'INRANGE');
      this.sortOrder.set(c ? String(c.sortOrder) : '');
      this.imageUrl.set(c?.imageUrl ?? null);
      this.errors.set({});
    });
  }

  save(): void {
    if (this.saving()) return;
    if (!this.code().trim()) {
      this.errors.set({ colorwayCode: this.transloco.translate('styles.colorway.codeRequired') });
      return;
    }
    const c = this.colorway();
    this.saving.set(true);
    this.svc
      .saveColorway(this.styleId(), c?.colorwayId ?? null, {
        rowVer: c?.rowVer ?? null,
        colorwayCode: this.code().trim(),
        colorwayName: this.name().trim() || null,
        status: this.status(),
        sortOrder: toNumber(this.sortOrder()),
        imageUrl: this.imageUrl(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          toast.success(this.transloco.translate(c ? 'styles.colorway.saved' : 'styles.colorway.added'));
          this.saved.emit();
        },
        error: (err) => {
          this.saving.set(false);
          const { message, fields } = styleError(err);
          this.errors.set(fields);
          toast.error(message);
        },
      });
  }
}
