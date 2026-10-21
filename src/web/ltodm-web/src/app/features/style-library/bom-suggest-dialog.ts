import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { BomOption, BomSuggestion, SaveBomLineRequest, StyleLibraryService, SuggestedBomLine, styleError } from './style-library.service';

interface Section {
  code: string;
  name: string;
  lines: SuggestedBomLine[];
}

/**
 * Suggest BOM: lines for the style built from similar library styles (family first, then product type, weave, gender,
 * customer, the fabric its description names, and the look of its picture), each with alternatives. Lines with
 * confidence 60+ start ticked; another brand's materials and materials already on the BOM never do. Ticked lines are
 * added as ordinary BOM lines for every colorway. Rules only; nothing is sent to an AI service.
 */
@Component({
  selector: 'app-bom-suggest-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, Modal, HlmButtonImports, HlmCheckboxImports, HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" size="xl" [heading]="'styles.suggest.title' | transloco: { styleNo: styleNo() }" (closed)="closed.emit()">
      <div class="flex flex-col gap-4">
        <p class="text-muted-foreground text-sm leading-relaxed">{{ 'styles.suggest.hint' | transloco }}</p>

        @if (loading()) {
          <div hlmSkeleton class="h-16 w-full"></div>
          <div hlmSkeleton class="h-64 w-full"></div>
        } @else if (suggestion(); as s) {
          <!-- What it was built from -->
          <div class="bg-muted/40 flex flex-col gap-2 rounded-md border p-3 text-sm">
            <p>
              <span class="text-muted-foreground">{{ 'styles.suggest.fabricRead' | transloco }}</span>
              @if (s.fabric; as f) {
                {{ f.fibres || ('styles.suggest.fibresUnknown' | transloco) }}@if (f.weightGsm) { · {{ f.weightGsm | number: '1.0-1' }} g/m² }@if (f.weave) { · {{ 'styles.suggest.weave.' + f.weave | transloco }} }
              } @else {
                <span class="text-muted-foreground">{{ 'styles.suggest.noFabricRead' | transloco }}</span>
              }
            </p>
            <p class="text-muted-foreground text-xs">
              {{ (s.usedPicture ? 'styles.suggest.pictureUsed' : 'styles.suggest.picture.' + (s.pictureNote || 'noPicture')) | transloco }}
            </p>
            @if (s.references.length) {
              <details>
                <summary class="cursor-pointer text-xs font-medium">{{ 'styles.suggest.references' | transloco: { count: s.references.length } }}</summary>
                <ul class="mt-2 flex flex-col gap-1 text-xs">
                  @for (r of s.references; track r.styleId) {
                    <li class="flex flex-wrap items-baseline gap-x-2">
                      <a class="text-primary font-mono underline-offset-2 hover:underline" [href]="'/styles/' + r.styleId" target="_blank" rel="noopener">{{ r.styleNo }}</a>
                      <span class="text-muted-foreground">{{ r.customerCode }} · {{ r.seasonCode }}</span>
                      @if (r.otherBrand) {
                        <span class="rounded bg-amber-500/15 px-1.5 text-amber-800 dark:text-amber-300">{{ 'styles.suggest.otherBrand' | transloco }}</span>
                      }
                      <span class="text-muted-foreground">{{ reasonText(r.reasons) }}</span>
                    </li>
                  }
                </ul>
              </details>
            }
            @for (n of s.notes; track n) {
              <p class="text-xs text-amber-800 dark:text-amber-300">{{ 'styles.suggest.note.' + n | transloco }}</p>
            }
          </div>

          @if (!s.lines.length) {
            <p class="text-muted-foreground py-6 text-center text-sm">{{ 'styles.suggest.nothing' | transloco }}</p>
          } @else {
            <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
              <label class="flex min-h-11 items-center gap-2 lg:min-h-0" for="suggest-all">
                <hlm-checkbox inputId="suggest-all" [checked]="allTicked()" (checkedChange)="toggleAll()" />
                {{ 'styles.suggest.tickAll' | transloco }}
              </label>
              <span class="text-muted-foreground text-xs">{{ 'styles.suggest.tickedHint' | transloco }}</span>
            </div>
            <div class="flex max-h-[55dvh] flex-col overflow-auto rounded-md border">
              @for (sec of sections(); track sec.code) {
                <div class="bg-muted/60 sticky top-0 z-10 border-b px-3 py-1.5 text-xs font-semibold">{{ sec.name }} · {{ sec.lines.length }}</div>
                @for (l of sec.lines; track l.key) {
                  @let o = chosen(l);
                  <div class="grid grid-cols-[2rem_minmax(0,1fr)] gap-x-2 gap-y-1 border-b px-3 py-2.5 text-sm lg:grid-cols-[2rem_minmax(0,2.4fr)_3rem_minmax(0,1.2fr)_6.5rem_minmax(0,1.3fr)] lg:items-start"
                       [class.opacity-60]="l.alreadyOnBom">
                    <hlm-checkbox class="mt-0.5" [inputId]="'sug-' + l.key" [checked]="ticked().has(l.key)" [disabled]="l.alreadyOnBom"
                                  (checkedChange)="toggle(l.key)" [attr.aria-label]="o.materialCode" />
                    <div class="min-w-0">
                      @if (l.options.length > 1) {
                        <hlm-native-select [selectId]="'opt-' + l.key" selectClass="h-11 w-full font-mono text-xs lg:h-8" [attr.aria-label]="'styles.suggest.pickMaterial' | transloco"
                                           [value]="choice(l).toString()" (valueChange)="choose(l, +($event ?? 0))">
                          @for (opt of l.options; track opt.materialId; let i = $index) {
                            <option hlmNativeSelectOption [value]="i.toString()">{{ opt.materialCode }}{{ i === 0 ? '' : ' · ' + ('styles.suggest.alternative' | transloco) }}</option>
                          }
                        </hlm-native-select>
                      } @else {
                        <span class="block truncate font-mono text-xs font-medium">{{ o.materialCode }}</span>
                      }
                      <span class="text-muted-foreground mt-0.5 line-clamp-2 block text-xs" [title]="o.description ?? ''">
                        {{ l.materialTypeName || l.materialTypeCode }}@if (l.materialTypeName || l.materialTypeCode) { · }{{ o.description || '—' }}
                      </span>
                      @if (l.alreadyOnBom) {
                        <span class="text-muted-foreground text-xs">{{ 'styles.suggest.onBom' | transloco }}</span>
                      } @else if (o.otherBrand) {
                        <span class="text-xs text-amber-800 dark:text-amber-300">{{ 'styles.suggest.otherBrandMaterial' | transloco }}</span>
                      }
                    </div>
                    <span class="text-muted-foreground col-start-2 text-xs lg:col-start-auto lg:text-sm lg:tabular-nums">
                      <span class="lg:hidden">{{ 'styles.suggest.part' | transloco }} </span>{{ o.partNo ?? '—' }}
                    </span>
                    <span class="text-muted-foreground col-start-2 min-w-0 text-xs lg:col-start-auto">
                      <span class="block truncate">{{ o.nominatedSupplierName || o.nominatedSupplierCode || '—' }}</span>
                      @if (o.supplierName || o.supplierCode) {
                        <span class="block truncate">SAP: {{ o.supplierName || o.supplierCode }}</span>
                      }
                    </span>
                    <span class="col-start-2 text-xs tabular-nums lg:col-start-auto lg:text-right lg:text-sm">
                      {{ o.lcoConsumption ?? o.brandConsumption ?? null | number: '1.0-4' }}{{ o.lcoConsumption ?? o.brandConsumption ? '' : '—' }}
                      <span class="text-muted-foreground text-xs">{{ o.uomCode }}</span>
                    </span>
                    <div class="col-start-2 flex flex-col gap-1 text-xs lg:col-start-auto">
                      <div class="flex items-center gap-2">
                        <span class="bg-muted h-1.5 w-16 overflow-hidden rounded-full" aria-hidden="true">
                          <span class="block h-full rounded-full" [class]="l.confidence >= 60 ? 'bg-emerald-500' : l.confidence >= 35 ? 'bg-amber-500' : 'bg-muted-foreground/50'"
                                [style.width.%]="l.confidence"></span>
                        </span>
                        <span class="tabular-nums">{{ l.confidence }}%</span>
                      </div>
                      <span class="text-muted-foreground">
                        @if (o.usedBy > 0) {
                          {{ 'styles.suggest.usedBy' | transloco: { used: o.usedBy, of: o.ofReferences } }}
                        }
                        @if (o.fabricMatch !== null) {
                          @if (o.usedBy > 0) { · }{{ 'styles.suggest.fabricMatch' | transloco: { match: o.fabricMatch } }}
                        }
                      </span>
                    </div>
                  </div>
                }
              }
            </div>
          }
        }
      </div>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving() || !ticked().size" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ 'styles.suggest.addN' | transloco: { count: ticked().size } }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class BomSuggestDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly styleId = input(0);
  readonly styleNo = input('');
  /** The style's colorways: added lines are used by all of them (colours left empty). */
  readonly colorwayIds = input<number[]>([]);
  readonly closed = output<void>();
  readonly saved = output<number>();

  readonly suggestion = signal<BomSuggestion | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly ticked = signal<ReadonlySet<string>>(new Set());
  /** Line key -> index of the option picked (0 = the suggestion). */
  readonly choices = signal<ReadonlyMap<string, number>>(new Map());

  readonly sections = computed<Section[]>(() => {
    const map = new Map<string, Section>();
    for (const l of this.suggestion()?.lines ?? []) {
      const code = l.contentClassCode ?? '';
      if (!map.has(code)) map.set(code, { code, name: l.contentClassName || this.transloco.translate('styles.suggest.unclassified'), lines: [] });
      map.get(code)!.lines.push(l);
    }
    return [...map.values()];
  });

  private readonly tickable = computed(() => (this.suggestion()?.lines ?? []).filter((l) => !l.alreadyOnBom));
  readonly allTicked = computed(() => this.tickable().length > 0 && this.tickable().every((l) => this.ticked().has(l.key)));

  constructor() {
    effect(() => {
      if (this.open()) this.load();
    });
  }

  choice = (l: SuggestedBomLine) => this.choices().get(l.key) ?? 0;
  chosen = (l: SuggestedBomLine): BomOption => l.options[this.choice(l)] ?? l.options[0];

  choose(l: SuggestedBomLine, index: number): void {
    this.choices.update((m) => new Map(m).set(l.key, index));
  }

  toggle(key: string): void {
    this.ticked.update((s) => {
      const next = new Set(s);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }

  toggleAll(): void {
    this.ticked.set(this.allTicked() ? new Set() : new Set(this.tickable().map((l) => l.key)));
  }

  reasonText(reasons: string[]): string {
    return reasons.map((r) => this.transloco.translate('styles.suggest.reason.' + r)).join(', ');
  }

  save(): void {
    const s = this.suggestion();
    if (!s || this.saving() || !this.ticked().size) return;
    const colorways = this.colorwayIds().map((colorwayId) => ({ colorwayId, materialColorCode: null, materialColorDescription: null }));
    const lines: SaveBomLineRequest[] = s.lines
      .filter((l) => this.ticked().has(l.key))
      .map((l) => {
        const o = this.chosen(l);
        return {
          rowVer: null,
          partNo: o.partNo,
          materialCode: o.materialCode,
          materialDescription: o.description,
          materialTypeCode: l.materialTypeCode,
          contentClassCode: l.contentClassCode,
          nominatedSupplierCode: o.nominatedSupplierCode,
          nominatedSupplierName: o.nominatedSupplierName,
          supplierCode: o.supplierCode,
          supplierName: o.supplierName,
          lcoConsumption: o.lcoConsumption,
          brandConsumption: o.brandConsumption,
          uomCode: o.uomCode,
          imageUrl: null,
          colorways,
        };
      });
    this.saving.set(true);
    this.svc.applyBomSuggestion(this.styleId(), lines).subscribe({
      next: (r) => {
        this.saving.set(false);
        toast.success(this.transloco.translate('styles.suggest.added', { count: r.added }));
        this.saved.emit(r.added);
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  private load(): void {
    this.suggestion.set(null);
    this.choices.set(new Map());
    this.ticked.set(new Set());
    this.loading.set(true);
    this.svc.suggestBom(this.styleId()).subscribe({
      next: (s) => {
        this.suggestion.set(s);
        this.ticked.set(new Set(s.lines.filter((l) => l.recommended).map((l) => l.key)));
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }
}
