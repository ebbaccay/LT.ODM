import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
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
import { BomLine, Colorway, StyleLibraryService, StyleLookups, styleError, toNumber } from './style-library.service';

interface LineForm {
  partNo: string;
  materialCode: string;
  materialDescription: string;
  materialTypeCode: string;
  contentClassCode: string;
  nominatedSupplierCode: string;
  nominatedSupplierName: string;
  supplierCode: string;
  supplierName: string;
  lcoConsumption: string;
  brandConsumption: string;
  uomCode: string;
  imageUrl: string | null;
}

interface ColorwayUse {
  colorway: Colorway;
  used: boolean;
  materialColorCode: string;
  materialColorDescription: string;
}

/**
 * Add (line null) or edit a BOM line: material, class, suppliers, consumption, UOM and image, plus which of the
 * style's colorways use it and the material colour in each. New material, supplier, type and UOM codes are added.
 */
@Component({
  selector: 'app-bom-line-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    Modal, ImageField, HlmButtonImports, HlmCheckboxImports, HlmInputImports, HlmLabelImports, HlmNativeSelectImports, HlmSpinnerImports,
    HlmTextareaImports, TranslocoPipe,
  ],
  template: `
    <app-modal [open]="open()" [heading]="line() ? ('styles.line.editTitle' | transloco: { material: line()!.materialCode }) : ('styles.line.addTitle' | transloco)" size="lg" (closed)="closed.emit()">
      @let f = form();
      @let e = errors();
      <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
        <div class="grid gap-4 sm:grid-cols-[6rem_minmax(0,1fr)_minmax(0,10rem)]">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="bl-part">{{ 'styles.line.partNo' | transloco }}</label>
            <input hlmInput id="bl-part" type="number" min="0" inputmode="numeric" class="h-11 lg:h-9" [value]="f.partNo" (input)="set('partNo', $any($event.target).value)" />
            @if (e['partNo']) { <p class="text-destructive text-xs">{{ e['partNo'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="bl-mat">{{ 'styles.line.materialCode' | transloco }}</label>
            <input hlmInput id="bl-mat" class="h-11 font-mono lg:h-9" maxlength="64" autocomplete="off" [value]="f.materialCode" (input)="set('materialCode', $any($event.target).value)" [attr.aria-invalid]="!!e['materialCode']" />
            @if (e['materialCode']) { <p class="text-destructive text-xs">{{ e['materialCode'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="bl-class">{{ 'styles.line.contentClass' | transloco }}</label>
            <hlm-native-select selectId="bl-class" selectClass="h-11 w-full lg:h-9" [value]="f.contentClassCode" (valueChange)="set('contentClassCode', $event ?? '')">
              <option hlmNativeSelectOption value="">—</option>
              @for (c of lookups()?.contentClasses ?? []; track c.code) { <option hlmNativeSelectOption [value]="c.code">{{ c.name }}</option> }
            </hlm-native-select>
          </div>
        </div>

        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="bl-desc">{{ 'styles.line.materialDescription' | transloco }}</label>
          <textarea hlmTextarea id="bl-desc" rows="2" maxlength="4000" [value]="f.materialDescription" (input)="set('materialDescription', $any($event.target).value)"></textarea>
        </div>

        <div class="grid gap-4 sm:grid-cols-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="bl-type">{{ 'styles.line.materialType' | transloco }}</label>
            <input hlmInput id="bl-type" list="bl-types" class="h-11 lg:h-9" maxlength="16" autocomplete="off" [value]="f.materialTypeCode" (input)="set('materialTypeCode', $any($event.target).value)" />
            <datalist id="bl-types">
              @for (t of lookups()?.materialTypes ?? []; track t.code) { <option [value]="t.code">{{ t.name }}</option> }
            </datalist>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="bl-lco">{{ 'styles.line.lcoConsumption' | transloco }}</label>
            <input hlmInput id="bl-lco" type="number" min="0" step="any" inputmode="decimal" class="h-11 lg:h-9" [value]="f.lcoConsumption" (input)="set('lcoConsumption', $any($event.target).value)" />
            @if (e['lcoConsumption']) { <p class="text-destructive text-xs">{{ e['lcoConsumption'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="bl-ad">{{ 'styles.line.brandConsumption' | transloco }}</label>
            <input hlmInput id="bl-ad" type="number" min="0" step="any" inputmode="decimal" class="h-11 lg:h-9" [value]="f.brandConsumption" (input)="set('brandConsumption', $any($event.target).value)" />
            @if (e['brandConsumption']) { <p class="text-destructive text-xs">{{ e['brandConsumption'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="bl-uom">{{ 'styles.line.uom' | transloco }}</label>
            <input hlmInput id="bl-uom" list="bl-uoms" class="h-11 lg:h-9" maxlength="8" autocomplete="off" [value]="f.uomCode" (input)="set('uomCode', $any($event.target).value)" />
            <datalist id="bl-uoms">
              @for (u of lookups()?.uoms ?? []; track u.code) { <option [value]="u.code"></option> }
            </datalist>
          </div>
        </div>

        <div class="grid gap-4 sm:grid-cols-2">
          <fieldset class="flex flex-col gap-2 rounded-md border p-3">
            <legend class="px-1 text-sm font-medium">{{ 'styles.line.nominatedSupplier' | transloco }}</legend>
            <input hlmInput class="h-11 lg:h-9" maxlength="32" [placeholder]="'styles.line.code' | transloco" [attr.aria-label]="'styles.line.nominatedSupplierCode' | transloco" [value]="f.nominatedSupplierCode" (input)="set('nominatedSupplierCode', $any($event.target).value)" />
            <input hlmInput class="h-11 lg:h-9" maxlength="100" [placeholder]="'styles.line.name' | transloco" [attr.aria-label]="'styles.line.nominatedSupplierName' | transloco" [value]="f.nominatedSupplierName" (input)="set('nominatedSupplierName', $any($event.target).value)" />
          </fieldset>
          <fieldset class="flex flex-col gap-2 rounded-md border p-3">
            <legend class="px-1 text-sm font-medium">{{ 'styles.line.sapSupplier' | transloco }}</legend>
            <input hlmInput list="bl-suppliers" class="h-11 lg:h-9" maxlength="32" [placeholder]="'styles.line.code' | transloco" [attr.aria-label]="'styles.line.sapSupplierCode' | transloco" autocomplete="off" [value]="f.supplierCode" (input)="onSupplierCode($any($event.target).value)" />
            <datalist id="bl-suppliers">
              @for (s of lookups()?.suppliers ?? []; track s.code) { <option [value]="s.code">{{ s.name }}</option> }
            </datalist>
            <input hlmInput class="h-11 lg:h-9" maxlength="150" [placeholder]="'styles.line.name' | transloco" [attr.aria-label]="'styles.line.sapSupplierName' | transloco" [readonly]="knownSupplier()" [value]="f.supplierName" (input)="set('supplierName', $any($event.target).value)" />
          </fieldset>
        </div>

        <!-- Colorways using this line -->
        <div class="flex flex-col gap-2">
          <p class="text-sm font-medium">{{ 'styles.line.colorwaysUsing' | transloco }}</p>
          @if (!uses().length) {
            <p class="text-muted-foreground text-sm">{{ 'styles.line.noColorways' | transloco }}</p>
          } @else {
            <div class="divide-y rounded-md border">
              @for (u of uses(); track u.colorway.colorwayId; let i = $index) {
                <div class="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-3 gap-y-2 p-2 sm:grid-cols-[auto_minmax(0,9rem)_minmax(0,1fr)_minmax(0,1.3fr)]">
                  <hlm-checkbox [inputId]="'use-' + i" [checked]="u.used" (checkedChange)="toggleUse(i, $event)" />
                  <label [for]="'use-' + i" class="min-w-0 text-sm">
                    <span class="block truncate font-mono">{{ u.colorway.colorwayCode }}</span>
                    <span class="text-muted-foreground block truncate text-xs">{{ u.colorway.colorwayName }}</span>
                  </label>
                  <input hlmInput class="col-start-2 h-11 sm:col-start-auto lg:h-8" maxlength="80" [placeholder]="'styles.line.materialColourCode' | transloco" [attr.aria-label]="'styles.line.materialColourCodeFor' | transloco: { code: u.colorway.colorwayCode }"
                    [disabled]="!u.used" [value]="u.materialColorCode" (input)="setUse(i, 'materialColorCode', $any($event.target).value)" />
                  <input hlmInput class="col-start-2 h-11 sm:col-start-auto lg:h-8" maxlength="150" [placeholder]="'styles.line.materialColour' | transloco" [attr.aria-label]="'styles.line.materialColourFor' | transloco: { code: u.colorway.colorwayCode }"
                    [disabled]="!u.used" [value]="u.materialColorDescription" (input)="setUse(i, 'materialColorDescription', $any($event.target).value)" />
                </div>
              }
            </div>
          }
        </div>

        <app-image-field [label]="'styles.line.image' | transloco" sizeClass="w-full sm:w-64" [url]="f.imageUrl" [editable]="true" (changed)="set('imageUrl', $event)" />
        <button type="submit" class="hidden"></button>
      </form>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ (line() ? 'actions.save' : 'styles.line.add') | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class BomLineDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly styleId = input.required<number>();
  readonly line = input<BomLine | null>(null);
  readonly colorways = input<Colorway[]>([]);
  readonly lookups = input<StyleLookups | null>(null);
  /** New lines start in this content class (the section the user added from). */
  readonly defaultContentClass = input<string>('');
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly form = signal<LineForm>(this.empty());
  readonly uses = signal<ColorwayUse[]>([]);
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);
  readonly knownSupplier = computed(() => {
    const code = this.form().supplierCode.trim();
    return !!code && (this.lookups()?.suppliers ?? []).some((s) => s.code === code);
  });

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const l = this.line();
      const text = (v: string | number | null | undefined) => (v === null || v === undefined ? '' : String(v));
      this.form.set(
        l
          ? {
              partNo: text(l.partNo), materialCode: l.materialCode, materialDescription: text(l.materialDescription),
              materialTypeCode: text(l.materialTypeCode), contentClassCode: text(l.contentClassCode),
              nominatedSupplierCode: text(l.nominatedSupplierCode), nominatedSupplierName: text(l.nominatedSupplierName),
              supplierCode: text(l.supplierCode), supplierName: text(l.supplierName), lcoConsumption: text(l.lcoConsumption),
              brandConsumption: text(l.brandConsumption), uomCode: text(l.uomCode), imageUrl: l.imageUrl,
            }
          : { ...this.empty(), contentClassCode: this.defaultContentClass() },
      );
      // A new line is used by every colorway until unticked.
      this.uses.set(
        this.colorways().map((c) => {
          const used = l ? l.colorways.find((x) => x.colorwayId === c.colorwayId) : undefined;
          return {
            colorway: c,
            used: l ? !!used : true,
            materialColorCode: used?.materialColorCode ?? '',
            materialColorDescription: used?.materialColorDescription ?? '',
          };
        }),
      );
      this.errors.set({});
    });
  }

  private empty(): LineForm {
    return {
      partNo: '', materialCode: '', materialDescription: '', materialTypeCode: '', contentClassCode: '', nominatedSupplierCode: '',
      nominatedSupplierName: '', supplierCode: '', supplierName: '', lcoConsumption: '', brandConsumption: '', uomCode: '', imageUrl: null,
    };
  }

  set<K extends keyof LineForm>(key: K, value: LineForm[K]): void {
    this.form.update((f) => ({ ...f, [key]: value }));
    if (this.errors()[key]) this.errors.update((e) => ({ ...e, [key]: '' }));
  }

  /** A known SAP supplier fills in its name. */
  onSupplierCode(code: string): void {
    const known = (this.lookups()?.suppliers ?? []).find((s) => s.code === code.trim());
    this.form.update((f) => ({ ...f, supplierCode: code, supplierName: known ? known.name : f.supplierName }));
  }

  toggleUse(index: number, used: boolean): void {
    this.uses.update((list) => list.map((u, i) => (i === index ? { ...u, used } : u)));
  }

  setUse(index: number, key: 'materialColorCode' | 'materialColorDescription', value: string): void {
    this.uses.update((list) => list.map((u, i) => (i === index ? { ...u, [key]: value } : u)));
  }

  save(): void {
    if (this.saving()) return;
    const f = this.form();
    const blank = (v: string) => v.trim() || null;
    const errors: Record<string, string> = {};
    if (!f.materialCode.trim()) errors['materialCode'] = this.transloco.translate('styles.line.materialRequired');
    for (const key of ['lcoConsumption', 'brandConsumption', 'partNo'] as const) {
      if (f[key].trim() && (toNumber(f[key]) === null || toNumber(f[key])! < 0)) errors[key] = this.transloco.translate('styles.line.numberInvalid');
    }
    this.errors.set(errors);
    if (Object.keys(errors).length) return;

    const l = this.line();
    this.saving.set(true);
    this.svc
      .saveBomLine(this.styleId(), l?.bomLineId ?? null, {
        rowVer: l?.rowVer ?? null,
        partNo: toNumber(f.partNo),
        materialCode: f.materialCode.trim(),
        materialDescription: blank(f.materialDescription),
        materialTypeCode: blank(f.materialTypeCode)?.toUpperCase() ?? null,
        contentClassCode: blank(f.contentClassCode),
        nominatedSupplierCode: blank(f.nominatedSupplierCode),
        nominatedSupplierName: blank(f.nominatedSupplierName),
        supplierCode: blank(f.supplierCode),
        supplierName: blank(f.supplierName),
        lcoConsumption: toNumber(f.lcoConsumption),
        brandConsumption: toNumber(f.brandConsumption),
        uomCode: blank(f.uomCode)?.toLowerCase() ?? null,
        imageUrl: f.imageUrl,
        colorways: this.uses()
          .filter((u) => u.used)
          .map((u) => ({
            colorwayId: u.colorway.colorwayId,
            materialColorCode: u.materialColorCode.trim() || null,
            materialColorDescription: u.materialColorDescription.trim() || null,
          })),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.svc.refreshLookups();
          toast.success(this.transloco.translate(l ? 'styles.line.saved' : 'styles.line.added'));
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
