import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { Modal } from '@tms/shared/ui/modal';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { PACKAGE_STATUSES, PackageHeader, PackageStatus, PackagesService, SavePackage } from './packages.service';

interface PackageForm {
  name: string;
  customerCode: string;
  seasonCode: string;
  description: string;
  status: PackageStatus;
  ownerName: string;
}

/**
 * New package (package null) or its header: name, customer, season, description; on an existing package also status and
 * owner. Customer and season can be typed (new codes are added to their lists, as on the style form).
 * Emits the package id when saved.
 */
@Component({
  selector: 'app-package-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmNativeSelectImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" [heading]="(pkg() ? 'packages.form.editTitle' : 'packages.form.newTitle') | transloco" (closed)="closed.emit()">
      @let e = errors();
      @let f = form();
      <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="pk-name">{{ 'packages.form.name' | transloco }}</label>
          <input hlmInput id="pk-name" class="h-11 lg:h-9" maxlength="150" autocomplete="off" [placeholder]="'packages.form.namePlaceholder' | transloco"
                 [value]="f.name" (input)="set('name', $any($event.target).value)" [attr.aria-invalid]="!!e['name']" />
          @if (e['name']) { <p class="text-destructive text-xs">{{ e['name'] }}</p> }
        </div>
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="pk-customer">{{ 'packages.form.customer' | transloco }}</label>
            <input hlmInput id="pk-customer" list="pk-customers" class="h-11 lg:h-9" maxlength="32" autocomplete="off"
                   [value]="f.customerCode" (input)="set('customerCode', $any($event.target).value)" [attr.aria-invalid]="!!e['customerCode']" />
            <datalist id="pk-customers">
              @for (c of lookups()?.customers ?? []; track c.code) { <option [value]="c.code">{{ c.code === c.name ? '' : c.name }}</option> }
            </datalist>
            @if (e['customerCode']) { <p class="text-destructive text-xs">{{ e['customerCode'] }}</p> }
            @else { <p class="text-muted-foreground text-xs">{{ 'packages.form.customerHint' | transloco }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="pk-season">{{ 'packages.form.season' | transloco }}</label>
            <input hlmInput id="pk-season" list="pk-seasons" class="h-11 lg:h-9" maxlength="16" autocomplete="off" placeholder="2028-SS"
                   [value]="f.seasonCode" (input)="set('seasonCode', $any($event.target).value)" [attr.aria-invalid]="!!e['seasonCode']" />
            <datalist id="pk-seasons">
              @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code">{{ s.code === s.name ? '' : s.name }}</option> }
            </datalist>
            @if (e['seasonCode']) { <p class="text-destructive text-xs">{{ e['seasonCode'] }}</p> }
          </div>
        </div>
        @if (pkg()) {
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="pk-status">{{ 'packages.form.status' | transloco }}</label>
              <hlm-native-select selectId="pk-status" selectClass="h-11 w-full lg:h-9" [value]="f.status" (valueChange)="set('status', $event ?? 'Draft')">
                @for (s of statuses; track s) { <option hlmNativeSelectOption [value]="s">{{ 'packages.status.' + s | transloco }}</option> }
              </hlm-native-select>
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="pk-owner">{{ 'packages.form.owner' | transloco }}</label>
              <input hlmInput id="pk-owner" class="h-11 lg:h-9" maxlength="64" autocomplete="off"
                     [value]="f.ownerName" (input)="set('ownerName', $any($event.target).value)" [attr.aria-invalid]="!!e['ownerName']" />
              @if (e['ownerName']) { <p class="text-destructive text-xs">{{ e['ownerName'] }}</p> }
            </div>
          </div>
        }
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="pk-desc">{{ 'packages.form.description' | transloco }}</label>
          <textarea hlmTextarea id="pk-desc" rows="3" maxlength="2000" [placeholder]="'packages.form.descriptionPlaceholder' | transloco"
                    [value]="f.description" (input)="set('description', $any($event.target).value)"></textarea>
          @if (e['description']) { <p class="text-destructive text-xs">{{ e['description'] }}</p> }
        </div>
        <button type="submit" class="hidden"></button>
      </form>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ (pkg() ? 'actions.save' : 'packages.form.create') | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class PackageFormDialog {
  private readonly svc = inject(PackagesService);
  private readonly styles = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  /** Null = a new package. */
  readonly pkg = input<PackageHeader | null>(null);
  readonly lookups = input<StyleLookups | null>(null);
  /** New package: customer and season to start with (e.g. the styles being added). */
  readonly defaults = input<{ customerCode?: string; seasonCode?: string } | null>(null);
  readonly closed = output<void>();
  readonly saved = output<number>();

  readonly statuses = PACKAGE_STATUSES;
  readonly form = signal<PackageForm>({ name: '', customerCode: '', seasonCode: '', description: '', status: 'Draft', ownerName: '' });
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const p = this.pkg();
      const d = this.defaults();
      this.form.set({
        name: p?.name ?? '', customerCode: p?.customerCode ?? d?.customerCode ?? '', seasonCode: p?.seasonCode ?? d?.seasonCode ?? '',
        description: p?.description ?? '', status: p?.status ?? 'Draft', ownerName: p?.ownerName ?? '',
      });
      this.errors.set({});
    });
  }

  set(key: keyof PackageForm, value: string): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  save(): void {
    if (this.saving()) return;
    const f = this.form();
    const errors: Record<string, string> = {};
    if (!f.name.trim()) errors['name'] = this.transloco.translate('packages.form.nameRequired');
    if (!f.customerCode.trim()) errors['customerCode'] = this.transloco.translate('packages.form.customerRequired');
    this.errors.set(errors);
    if (Object.keys(errors).length) return;

    const p = this.pkg();
    const body: SavePackage = {
      rowVer: p?.rowVer ?? null, name: f.name.trim(), customerCode: f.customerCode.trim().toUpperCase(), seasonCode: f.seasonCode.trim().toUpperCase() || null,
      description: f.description.trim() || null, status: f.status, ownerName: p ? f.ownerName.trim() || null : null,
    };
    this.saving.set(true);
    (p ? this.svc.update(p.packageId, body) : this.svc.create(body)).subscribe({
      next: (id) => {
        this.saving.set(false);
        this.styles.refreshLookups();
        this.saved.emit(id);
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
