import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { LcoLine, StyleGapsService } from './style-gaps.service';
import { styleError } from './style-library.service';

/**
 * Fill empty LCO consumption on one style from the suggestions: the brand consumption (optionally plus the brand's
 * allowance), else the median LCO of other styles using the same material. Filled figures are marked as estimates until
 * someone saves another figure on the line. Rules only; nothing is sent anywhere.
 */
@Component({
  selector: 'app-lco-fill-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, Modal, HlmButtonImports, HlmCheckboxImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" size="lg" [heading]="'gaps.lco.dialogTitle' | transloco: { styleNo: styleNo() }" (closed)="closed.emit()">
      <div class="flex flex-col gap-3">
        <p class="text-muted-foreground text-sm">{{ 'gaps.lco.dialogHint' | transloco }}</p>
        <label class="flex min-h-11 items-center gap-2 text-sm lg:min-h-0">
          <hlm-checkbox inputId="lco-allowance" [checked]="withAllowance()" (checkedChange)="toggleAllowance()" />
          {{ 'gaps.lco.withAllowance' | transloco }}
        </label>
        @if (loading()) {
          <hlm-skeleton class="h-40 w-full" />
        } @else if (!lines().length) {
          <p class="text-muted-foreground py-6 text-center text-sm">{{ 'gaps.lco.noneOnStyle' | transloco }}</p>
        } @else {
          <div class="max-h-[50dvh] overflow-auto rounded-md border">
            <table class="w-full text-sm">
              <thead class="text-muted-foreground bg-muted/40 sticky top-0 text-left text-xs">
                <tr class="border-b">
                  <th class="w-10 px-3 py-2">
                    <hlm-checkbox inputId="lco-all" [checked]="allTicked()" (checkedChange)="toggleAll()" [attr.aria-label]="'gaps.lco.tickAll' | transloco" />
                  </th>
                  <th class="px-2 py-2 font-medium">{{ 'gaps.lco.colMaterial' | transloco }}</th>
                  <th class="px-2 py-2 text-right font-medium">{{ 'gaps.lco.colBrand' | transloco }}</th>
                  <th class="px-2 py-2 text-right font-medium">{{ 'gaps.lco.colSuggested' | transloco }}</th>
                  <th class="px-3 py-2 font-medium">{{ 'gaps.lco.colFrom' | transloco }}</th>
                </tr>
              </thead>
              <tbody class="divide-y">
                @for (l of lines(); track l.bomLineId) {
                  <tr [class.opacity-60]="l.suggested === null">
                    <td class="px-3 py-2">
                      <hlm-checkbox [inputId]="'lco-' + l.bomLineId" [checked]="ticked().has(l.bomLineId)" [disabled]="l.suggested === null"
                                    (checkedChange)="toggle(l.bomLineId)" [attr.aria-label]="l.materialCode" />
                    </td>
                    <td class="max-w-0 px-2 py-2">
                      <span class="block truncate font-mono text-xs">{{ l.materialCode }}</span>
                      <span class="text-muted-foreground block truncate text-xs" [title]="l.materialDescription ?? ''">{{ l.contentClassCode }} · {{ l.materialDescription || '—' }}</span>
                    </td>
                    <td class="px-2 py-2 text-right whitespace-nowrap tabular-nums">
                      {{ l.brandConsumption === null ? '—' : (l.brandConsumption | number: '1.0-4') }}
                      @if (l.consumptionAllowance) { <span class="text-muted-foreground block text-xs">+{{ l.consumptionAllowance * 100 | number: '1.0-1' }}%</span> }
                    </td>
                    <td class="px-2 py-2 text-right font-medium whitespace-nowrap tabular-nums">
                      {{ l.suggested === null ? '—' : (l.suggested | number: '1.0-4') }} <span class="text-muted-foreground text-xs font-normal">{{ l.uomCode }}</span>
                    </td>
                    <td class="text-muted-foreground px-3 py-2 text-xs">
                      {{ (l.source ? 'gaps.lco.source.' + l.source : 'gaps.lco.source.none') | transloco: { peers: l.peers } }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving() || !ticked().size" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ 'gaps.lco.fillN' | transloco: { count: ticked().size } }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class LcoFillDialog {
  private readonly svc = inject(StyleGapsService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly styleId = input(0);
  readonly styleNo = input('');
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly lines = signal<LcoLine[]>([]);
  readonly ticked = signal<ReadonlySet<number>>(new Set());
  readonly withAllowance = signal(false);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly allTicked = computed(() => {
    const ready = this.lines().filter((l) => l.suggested !== null);
    return ready.length > 0 && ready.every((l) => this.ticked().has(l.bomLineId));
  });

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.withAllowance.set(false);
      this.load();
    });
  }

  toggleAllowance(): void {
    this.withAllowance.update((v) => !v);
    this.load();
  }

  toggle(id: number): void {
    this.ticked.update((s) => {
      const next = new Set(s);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  toggleAll(): void {
    this.ticked.set(this.allTicked() ? new Set() : new Set(this.lines().filter((l) => l.suggested !== null).map((l) => l.bomLineId)));
  }

  save(): void {
    if (this.saving() || !this.ticked().size) return;
    this.saving.set(true);
    this.svc.applyLco({ bomLineIds: [...this.ticked()], withAllowance: this.withAllowance() }).subscribe({
      next: (r) => {
        this.saving.set(false);
        toast.success(this.transloco.translate('gaps.lco.filled', { count: r.updated }));
        this.saved.emit();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.svc.lcoForStyle(this.styleId(), this.withAllowance()).subscribe({
      next: (lines) => {
        this.lines.set(lines);
        this.ticked.set(new Set(lines.filter((l) => l.suggested !== null).map((l) => l.bomLineId)));
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }
}
