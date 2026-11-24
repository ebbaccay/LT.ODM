import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { styleError } from '../style-library/style-library.service';
import { CostingService, PlantRate, PlantRates, SavePlantRate } from './costing.service';

/** The row being added (rateId null) or changed. */
interface Draft extends SavePlantRate {
  rateId: number | null;
}

/**
 * Costing > Cost per minute (Admin and Costing): the plants' cost per minute, as in the ERP's Cost Per Minute table. Basic CM = cost per minute
 * x TC SMV. Rates are added and changed in place; Delete marks a rate deleted (kept, never used again).
 */
@Component({
  selector: 'app-plant-rates',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, NgTemplateOutlet, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucidePencil, lucidePlus, lucideTrash2 })],
  template: `
    <section hlmCard class="gap-0 py-0">
      <div class="flex flex-wrap items-start justify-between gap-3 border-b px-5 py-3">
        <div class="min-w-0">
          <h2 class="font-semibold">{{ 'ioPrices.rates.title' | transloco }}</h2>
          <p class="text-muted-foreground max-w-3xl text-xs">{{ 'ioPrices.rates.hint' | transloco }}</p>
        </div>
        <div class="flex flex-wrap items-center gap-3">
          <label class="flex min-h-9 items-center gap-2 text-xs" for="rates-deleted">
            <hlm-checkbox inputId="rates-deleted" [checked]="withDeleted()" (checkedChange)="withDeleted.set(!withDeleted()); load()" />
            {{ 'ioPrices.rates.showDeleted' | transloco }}
          </label>
          <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="!!draft()" (click)="startAdd()"><ng-icon name="lucidePlus" />{{ 'ioPrices.rates.add' | transloco }}</button>
        </div>
      </div>

      @if (loading() && !data()) {
        <div class="p-5"><div hlmSkeleton class="h-24 w-full"></div></div>
      } @else {
        <div class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="text-muted-foreground text-left text-xs tracking-wide uppercase">
              <tr class="border-b">
                <th class="px-5 py-2 font-medium">{{ 'ioPrices.rates.plant' | transloco }}</th>
                <th class="px-3 py-2 font-medium">{{ 'ioPrices.rates.from' | transloco }}</th>
                <th class="px-3 py-2 text-right font-medium">{{ 'ioPrices.rates.minQty' | transloco }}</th>
                <th class="px-3 py-2 font-medium">{{ 'ioPrices.rates.term' | transloco }}</th>
                <th class="px-3 py-2 font-medium">{{ 'ioPrices.rates.currency' | transloco }}</th>
                <th class="px-3 py-2 text-right font-medium">{{ 'ioPrices.rates.cost' | transloco }}</th>
                <th class="px-3 py-2 font-medium">{{ 'ioPrices.rates.changed' | transloco }}</th>
                <th class="px-3 py-2"><span class="sr-only">{{ 'ioPrices.rates.edit' | transloco }}</span></th>
              </tr>
            </thead>
            <tbody class="divide-y">
              @if (draft(); as d) {
                @if (d.rateId === null) { <ng-container [ngTemplateOutlet]="editor" /> }
              }
              @for (r of data()?.rates ?? []; track r.rateId) {
                @if (draft()?.rateId === r.rateId) {
                  <ng-container [ngTemplateOutlet]="editor" />
                } @else {
                  <tr [class.opacity-50]="r.isDeleted">
                    <td class="px-5 py-2">
                      <span class="font-medium">{{ r.plantCode }}</span>
                      <span class="text-muted-foreground block text-xs">{{ r.plantName }}</span>
                    </td>
                    <td class="px-3 py-2 whitespace-nowrap">
                      {{ r.effectiveDate | date: 'mediumDate' }}
                      @if (r.inForce) { <span hlmBadge class="ml-1">{{ 'ioPrices.rates.inForce' | transloco }}</span> }
                      @if (r.isDeleted) { <span hlmBadge variant="outline" class="ml-1">{{ 'ioPrices.rates.deleted' | transloco }}</span> }
                    </td>
                    <td class="px-3 py-2 text-right tabular-nums">{{ r.minOrderQty | number }}</td>
                    <td class="px-3 py-2">{{ r.salesTerm }}</td>
                    <td class="px-3 py-2">{{ r.currencyCode }}</td>
                    <td class="px-3 py-2 text-right font-semibold tabular-nums">{{ r.cost | number: '1.5-5' }}</td>
                    <td class="text-muted-foreground px-3 py-2 text-xs whitespace-nowrap">{{ r.updatedBy ?? r.createdBy }} · {{ (r.updatedUtc ?? r.createdUtc) | date: 'mediumDate' }}</td>
                    <td class="px-3 py-2 text-right whitespace-nowrap">
                      @if (!r.isDeleted) {
                        <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 lg:h-8" [disabled]="!!draft()" (click)="startEdit(r)"><ng-icon name="lucidePencil" />{{ 'ioPrices.rates.edit' | transloco }}</button>
                        <button hlmBtn variant="ghost" size="sm" type="button" class="text-destructive h-11 lg:h-8" [disabled]="!!draft()" (click)="remove(r)"><ng-icon name="lucideTrash2" /><span class="sr-only">{{ 'ioPrices.rates.delete' | transloco }}</span></button>
                      }
                    </td>
                  </tr>
                }
              } @empty {
                @if (!draft()) { <tr><td colspan="8" class="text-muted-foreground px-5 py-8 text-center">{{ 'ioPrices.rates.none' | transloco }}</td></tr> }
              }
            </tbody>
          </table>
        </div>
      }
    </section>

    <ng-template #editor>
      @if (draft(); as d) {
        <tr class="bg-muted/40">
          <td class="px-5 py-2">
            <hlm-native-select selectId="rate-plant" selectClass="h-11 w-full min-w-40 lg:h-9" [attr.aria-label]="'ioPrices.rates.plant' | transloco" [value]="d.plantCode" (valueChange)="patch({ plantCode: $event ?? '' })">
              @for (p of data()?.plants ?? []; track p.code) { <option hlmNativeSelectOption [value]="p.code">{{ p.code }} · {{ p.name }}</option> }
            </hlm-native-select>
          </td>
          <td class="px-3 py-2"><input hlmInput id="rate-from" type="date" class="h-11 lg:h-9" [attr.aria-label]="'ioPrices.rates.from' | transloco" [value]="d.effectiveDate" (change)="patch({ effectiveDate: $any($event.target).value })" /></td>
          <td class="px-3 py-2"><input hlmInput id="rate-qty" type="number" min="0" class="h-11 w-24 text-right lg:h-9" [attr.aria-label]="'ioPrices.rates.minQty' | transloco" [value]="d.minOrderQty" (input)="patch({ minOrderQty: +$any($event.target).value })" /></td>
          <td class="px-3 py-2"><input hlmInput id="rate-term" maxlength="10" class="h-11 w-20 uppercase lg:h-9" [attr.aria-label]="'ioPrices.rates.term' | transloco" [value]="d.salesTerm" (input)="patch({ salesTerm: $any($event.target).value })" /></td>
          <td class="px-3 py-2"><input hlmInput id="rate-currency" maxlength="3" class="h-11 w-16 uppercase lg:h-9" [attr.aria-label]="'ioPrices.rates.currency' | transloco" [value]="d.currency" (input)="patch({ currency: $any($event.target).value })" /></td>
          <td class="px-3 py-2"><input hlmInput id="rate-cost" type="number" min="0.00001" step="0.00001" class="h-11 w-28 text-right lg:h-9" [attr.aria-label]="'ioPrices.rates.cost' | transloco" [value]="d.cost" (input)="patch({ cost: +$any($event.target).value })" /></td>
          <td class="px-3 py-2" colspan="2">
            <span class="flex justify-end gap-1">
              <button hlmBtn size="sm" type="button" class="h-11 lg:h-8" [disabled]="saving()" (click)="save()">{{ 'ioPrices.rates.save' | transloco }}</button>
              <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 lg:h-8" (click)="draft.set(null)">{{ 'ioPrices.rates.cancel' | transloco }}</button>
            </span>
          </td>
        </tr>
      }
    </ng-template>
  `,
})
export class PlantRatesPanel implements OnInit {
  private readonly svc = inject(CostingService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly i18n = inject(TranslocoService);
  private readonly datePipe = new DatePipe('en-US');

  readonly data = signal<PlantRates | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly withDeleted = signal(false);
  readonly draft = signal<Draft | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.rates(this.withDeleted()).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  patch(change: Partial<Draft>): void {
    this.draft.update((d) => (d ? { ...d, ...change } : d));
  }

  startAdd(): void {
    const latest = this.data()?.rates.find((r) => !r.isDeleted);
    this.draft.set({
      rateId: null, plantCode: latest?.plantCode ?? this.data()?.plants[0]?.code ?? '', effectiveDate: new Date().toISOString().slice(0, 10),
      minOrderQty: 1, salesTerm: 'FOB', currency: latest?.currencyCode ?? 'USD', cost: latest?.cost ?? 0,
    });
  }

  startEdit(r: PlantRate): void {
    this.draft.set({
      rateId: r.rateId, plantCode: r.plantCode, effectiveDate: r.effectiveDate.slice(0, 10), minOrderQty: r.minOrderQty, salesTerm: r.salesTerm, currency: r.currencyCode, cost: r.cost,
    });
  }

  save(): void {
    const d = this.draft();
    if (!d) return;
    this.saving.set(true);
    const { rateId, ...rate } = d;
    this.svc.saveRate(rateId, rate).subscribe({
      next: () => {
        this.saving.set(false);
        this.draft.set(null);
        toast.success(this.i18n.translate('ioPrices.rates.saved'));
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  async remove(r: PlantRate): Promise<void> {
    const ok = await this.confirm.confirm({
      title: this.i18n.translate('ioPrices.rates.deleteTitle'),
      message: this.i18n.translate('ioPrices.rates.deleteMessage', { plant: r.plantCode, date: this.datePipe.transform(r.effectiveDate, 'mediumDate') }),
      confirmLabel: this.i18n.translate('ioPrices.rates.delete'),
      cancelLabel: this.i18n.translate('ioPrices.rates.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteRate(r.rateId).subscribe({
      next: () => {
        toast.success(this.i18n.translate('ioPrices.rates.removed'));
        this.load();
      },
      error: (e) => toast.error(styleError(e).message),
    });
  }
}
