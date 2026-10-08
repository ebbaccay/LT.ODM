import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { Modal } from '@tms/shared/ui/modal';
import { ImageField } from './image-field';
import { GENDERS, SaveStyleRequest, StyleHeader, StyleLibraryService, StyleLookups, styleError, toNumber } from './style-library.service';

type Form = Omit<SaveStyleRequest, 'rowVer' | 'garmentLeadTimeDays'> & { garmentLeadTimeDays: string };

const EMPTY: Form = {
  customerCode: '', seasonCode: '', styleNo: '', description: null, modelCode: null, modelName: null, weaveTypeCode: null,
  productTypeCode: null, gender: null, garmentLeadTimeDays: '', businessUnitCode: null, sketchUrl: null, imageUrl: null, isActive: true,
};

/**
 * Create (style null) or edit a style's header, sketch and photo. Customer, season, business unit and product type
 * offer the known codes but accept new ones (the API adds them). Emits the style id after saving.
 */
@Component({
  selector: 'app-style-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, ImageField, HlmButtonImports, HlmCheckboxImports, HlmInputImports, HlmLabelImports, HlmNativeSelectImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" [heading]="style() ? ('styles.form.editTitle' | transloco: { styleNo: style()!.styleNo }) : ('styles.form.newTitle' | transloco)" size="lg" (closed)="closed.emit()">
      @let f = form();
      @let e = errors();
      <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
        <div class="grid gap-4 sm:grid-cols-3">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-customer">{{ 'styles.form.customer' | transloco }}</label>
            <input hlmInput id="sf-customer" list="sf-customers" class="h-11 lg:h-9" maxlength="32" autocomplete="off"
              [value]="f.customerCode" (input)="set('customerCode', $any($event.target).value)" [attr.aria-invalid]="!!e['customerCode']" />
            <datalist id="sf-customers">
              @for (c of lookups()?.customers ?? []; track c.code) { <option [value]="c.code">{{ c.name }}</option> }
            </datalist>
            @if (e['customerCode']) { <p class="text-destructive text-xs">{{ e['customerCode'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-season">{{ 'styles.form.season' | transloco }}</label>
            <input hlmInput id="sf-season" list="sf-seasons" class="h-11 lg:h-9" maxlength="16" placeholder="2027-SS" autocomplete="off"
              [value]="f.seasonCode" (input)="set('seasonCode', $any($event.target).value)" [attr.aria-invalid]="!!e['seasonCode']" />
            <datalist id="sf-seasons">
              @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code"></option> }
            </datalist>
            @if (e['seasonCode']) { <p class="text-destructive text-xs">{{ e['seasonCode'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-styleno">{{ 'styles.form.styleNo' | transloco }}</label>
            <input hlmInput id="sf-styleno" class="h-11 font-mono lg:h-9" maxlength="40" autocomplete="off"
              [value]="f.styleNo" (input)="set('styleNo', $any($event.target).value)" [attr.aria-invalid]="!!e['styleNo']" />
            @if (e['styleNo']) { <p class="text-destructive text-xs">{{ e['styleNo'] }}</p> }
          </div>
        </div>

        <div class="grid gap-4 sm:grid-cols-3">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-model">{{ 'styles.form.modelId' | transloco }}</label>
            <input hlmInput id="sf-model" class="h-11 lg:h-9" maxlength="60" [value]="f.modelCode ?? ''" (input)="set('modelCode', $any($event.target).value)" />
          </div>
          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label hlmLabel for="sf-modelname">{{ 'styles.form.modelName' | transloco }}</label>
            <input hlmInput id="sf-modelname" class="h-11 lg:h-9" maxlength="100" [value]="f.modelName ?? ''" (input)="set('modelName', $any($event.target).value)" />
          </div>
        </div>

        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="sf-desc">{{ 'styles.form.description' | transloco }}</label>
          <textarea hlmTextarea id="sf-desc" rows="2" maxlength="400" [value]="f.description ?? ''" (input)="set('description', $any($event.target).value)"></textarea>
          @if (e['description']) { <p class="text-destructive text-xs">{{ e['description'] }}</p> }
        </div>

        <div class="grid gap-4 sm:grid-cols-3">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-weave">{{ 'styles.form.weave' | transloco }}</label>
            <hlm-native-select selectId="sf-weave" selectClass="h-11 w-full lg:h-9" [value]="f.weaveTypeCode ?? ''" (valueChange)="set('weaveTypeCode', $event ?? '')">
              <option hlmNativeSelectOption value="">—</option>
              @for (w of lookups()?.weaveTypes ?? []; track w.code) { <option hlmNativeSelectOption [value]="w.code">{{ w.name }}</option> }
            </hlm-native-select>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-ptype">{{ 'styles.form.productType' | transloco }}</label>
            <input hlmInput id="sf-ptype" list="sf-ptypes" class="h-11 lg:h-9" maxlength="40" autocomplete="off"
              [value]="f.productTypeCode ?? ''" (input)="set('productTypeCode', $any($event.target).value)" />
            <datalist id="sf-ptypes">
              @for (p of lookups()?.productTypes ?? []; track p.code) { <option [value]="p.code">{{ p.name }}</option> }
            </datalist>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-gender">{{ 'styles.form.gender' | transloco }}</label>
            <hlm-native-select selectId="sf-gender" selectClass="h-11 w-full lg:h-9" [value]="f.gender ?? ''" (valueChange)="set('gender', $event ?? '')">
              <option hlmNativeSelectOption value="">—</option>
              @for (g of genders; track g.code) { <option hlmNativeSelectOption [value]="g.code">{{ 'styles.gender.' + g.code | transloco }}</option> }
            </hlm-native-select>
            @if (e['gender']) { <p class="text-destructive text-xs">{{ e['gender'] }}</p> }
          </div>
        </div>

        <div class="grid gap-4 sm:grid-cols-3">
          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label hlmLabel for="sf-bu">{{ 'styles.form.businessUnit' | transloco }}</label>
            <input hlmInput id="sf-bu" list="sf-bus" class="h-11 lg:h-9" maxlength="16" autocomplete="off"
              [value]="f.businessUnitCode ?? ''" (input)="set('businessUnitCode', $any($event.target).value)" />
            <datalist id="sf-bus">
              @for (b of lookups()?.businessUnits ?? []; track b.code) { <option [value]="b.code">{{ b.name }}</option> }
            </datalist>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="sf-lead">{{ 'styles.form.leadTime' | transloco }}</label>
            <input hlmInput id="sf-lead" type="number" min="0" max="730" inputmode="numeric" class="h-11 lg:h-9"
              [value]="f.garmentLeadTimeDays" (input)="set('garmentLeadTimeDays', $any($event.target).value)" />
            @if (e['garmentLeadTimeDays']) { <p class="text-destructive text-xs">{{ e['garmentLeadTimeDays'] }}</p> }
          </div>
        </div>

        <div class="grid grid-cols-2 gap-4">
          <app-image-field [label]="'styles.form.sketch' | transloco" [url]="f.sketchUrl" [editable]="true" [downloadName]="(f.styleNo || 'style') + '_sketch'" (changed)="set('sketchUrl', $event)" />
          <app-image-field [label]="'styles.form.photo' | transloco" [url]="f.imageUrl" [editable]="true" [downloadName]="(f.styleNo || 'style') + '_photo'" (changed)="set('imageUrl', $event)" />
        </div>
        <label class="flex w-fit cursor-pointer items-start gap-3">
          <hlm-checkbox class="mt-0.5" [checked]="f.isActive" (checkedChange)="set('isActive', $event)" />
          <span class="flex flex-col">
            <span class="text-sm font-medium">{{ 'styles.form.active' | transloco }}</span>
            <span class="text-muted-foreground text-xs">{{ 'styles.form.activeHint' | transloco }}</span>
          </span>
        </label>
        <button type="submit" class="hidden"></button>
      </form>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ (style() ? 'actions.save' : 'styles.form.create') | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class StyleFormDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  /** The style to edit; null for a new style. */
  readonly style = input<StyleHeader | null>(null);
  readonly lookups = input<StyleLookups | null>(null);
  readonly closed = output<void>();
  readonly saved = output<number>();

  readonly genders = GENDERS;
  readonly form = signal<Form>({ ...EMPTY });
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);

  constructor() {
    // Reset the form each time the dialog opens.
    effect(() => {
      if (!this.open()) return;
      const s = this.style();
      this.errors.set({});
      this.form.set(
        s
          ? {
              customerCode: s.customerCode, seasonCode: s.seasonCode, styleNo: s.styleNo, description: s.description, modelCode: s.modelCode,
              modelName: s.modelName, weaveTypeCode: s.weaveTypeCode, productTypeCode: s.productTypeCode, gender: s.gender,
              garmentLeadTimeDays: s.garmentLeadTimeDays?.toString() ?? '', businessUnitCode: s.businessUnitCode, sketchUrl: s.sketchUrl,
              imageUrl: s.imageUrl, isActive: s.isActive,
            }
          : { ...EMPTY },
      );
    });
  }

  set<K extends keyof Form>(key: K, value: Form[K]): void {
    this.form.update((f) => ({ ...f, [key]: value }));
    if (this.errors()[key]) this.errors.update((e) => ({ ...e, [key]: '' }));
  }

  save(): void {
    if (this.saving()) return;
    const f = this.form();
    const blank = (v: string | null) => (v?.trim() ? v.trim() : null);
    const request: SaveStyleRequest = {
      rowVer: this.style()?.rowVer ?? null,
      customerCode: f.customerCode.trim().toUpperCase(),
      seasonCode: f.seasonCode.trim().toUpperCase(),
      styleNo: f.styleNo.trim(),
      description: blank(f.description),
      modelCode: blank(f.modelCode),
      modelName: blank(f.modelName),
      weaveTypeCode: blank(f.weaveTypeCode),
      productTypeCode: blank(f.productTypeCode),
      gender: blank(f.gender),
      garmentLeadTimeDays: toNumber(f.garmentLeadTimeDays),
      businessUnitCode: blank(f.businessUnitCode),
      sketchUrl: f.sketchUrl,
      imageUrl: f.imageUrl,
      isActive: f.isActive,
    };
    const missing: Record<string, string> = {};
    if (!request.customerCode) missing['customerCode'] = this.transloco.translate('styles.form.customerRequired');
    if (!/^[0-9]{4}-[A-Z]{2,4}$/.test(request.seasonCode)) missing['seasonCode'] = this.transloco.translate('styles.form.seasonInvalid', { example: '2027-SS' });
    if (!request.styleNo) missing['styleNo'] = this.transloco.translate('styles.form.styleNoRequired');
    if (Object.keys(missing).length) {
      this.errors.set(missing);
      return;
    }

    this.saving.set(true);
    const s = this.style();
    const call = s ? this.svc.updateStyle(s.styleId, request) : this.svc.createStyle(request);
    call.subscribe({
      next: (id) => {
        this.saving.set(false);
        this.svc.refreshLookups();
        toast.success(this.transloco.translate(s ? 'styles.form.saved' : 'styles.form.created'));
        this.saved.emit(s ? s.styleId : (id as number));
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
