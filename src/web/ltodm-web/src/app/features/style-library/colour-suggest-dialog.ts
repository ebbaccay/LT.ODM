import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { SuggestSteps } from './suggest-steps';
import { BomLine, Colorway, ColourSuggestion, StyleLibraryService, SuggestedColour, styleError } from './style-library.service';

const key = (c: { bomLineId: number; colorwayId: number }) => `${c.bomLineId}|${c.colorwayId}`;

/**
 * Suggest colours: material colours for the style's empty BOM line x colorway cells, from what other styles used for the
 * same material in colorways of the same name (this customer first), or the one colour a material always comes in.
 * Confidence 80+ starts ticked (99% right in the back-test); 40-79 is shown unticked. Only empty cells are filled.
 */
@Component({
  selector: 'app-colour-suggest-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, SuggestSteps, HlmButtonImports, HlmCheckboxImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" size="xl" [heading]="'styles.colours.title' | transloco: { styleNo: styleNo() }" (closed)="closed.emit()">
      <div class="flex flex-col gap-3">
        <p class="text-muted-foreground text-sm leading-relaxed">{{ 'styles.colours.hint' | transloco }}</p>
        @if (!colorways().length) {
          <!-- Colours are per colorway: nothing to suggest yet -->
          <div class="flex flex-col items-center gap-3 py-8 text-center">
            <p class="text-sm">{{ 'styles.colours.noColorways' | transloco }}</p>
            <button hlmBtn type="button" class="h-11 lg:h-9" (click)="addColorways.emit()">{{ 'styles.colours.addColorways' | transloco }}</button>
          </div>
        } @else if (loading()) {
          <!-- Progress: the steps the server works through (about 0.9 s in all) -->
          <app-suggest-steps [steps]="steps" labelPrefix="styles.colours.step." [stepMs]="300" />
          <div hlmSkeleton class="h-40 w-full"></div>
        } @else if (result(); as r) {
          <p class="text-sm">{{ 'styles.colours.summary' | transloco: { empty: r.empty, cells: r.cells, found: r.suggestions.length } }}</p>
          @if (!rows().length) {
            <p class="text-muted-foreground py-6 text-center text-sm">{{ (r.empty ? 'styles.colours.nothing' : 'styles.colours.allFilled') | transloco }}</p>
          } @else {
            <label class="flex min-h-11 w-fit items-center gap-2 text-sm lg:min-h-0" for="col-all">
              <hlm-checkbox inputId="col-all" [checked]="allTicked()" (checkedChange)="toggleAll()" />
              {{ 'styles.colours.tickAll' | transloco }}
            </label>
            <div class="max-h-[55dvh] overflow-auto rounded-md border">
              <table class="w-full min-w-max text-xs">
                <thead class="bg-muted/60 sticky top-0 z-10 text-left">
                  <tr class="border-b">
                    <th class="bg-muted/60 sticky left-0 min-w-56 px-3 py-2 font-medium">{{ 'styles.colours.material' | transloco }}</th>
                    @for (c of colorways(); track c.colorwayId) {
                      <th class="min-w-40 px-3 py-2 font-medium">
                        <span class="block font-mono">{{ c.colorwayCode }}</span>
                        <span class="text-muted-foreground block font-normal">{{ c.colorwayName || '—' }}</span>
                      </th>
                    }
                  </tr>
                </thead>
                <tbody class="divide-y">
                  @for (row of rows(); track row.line.bomLineId) {
                    <tr>
                      <td class="bg-card sticky left-0 max-w-56 px-3 py-2 align-top">
                        <span class="block truncate font-mono font-medium">{{ row.line.materialCode }}</span>
                        <span class="text-muted-foreground block truncate" [title]="row.line.materialDescription ?? ''">{{ row.line.materialDescription }}</span>
                      </td>
                      @for (c of colorways(); track c.colorwayId) {
                        @let s = cell(row.line.bomLineId, c.colorwayId);
                        <td class="px-3 py-2 align-top">
                          @if (s) {
                            <label class="flex min-h-11 cursor-pointer items-start gap-2 lg:min-h-0" [for]="'col-' + s.bomLineId + '-' + s.colorwayId">
                              <hlm-checkbox class="mt-0.5" [inputId]="'col-' + s.bomLineId + '-' + s.colorwayId" [checked]="ticked().has(keyOf(s))" (checkedChange)="toggle(s)" />
                              <span class="min-w-0">
                                <span class="block font-mono">{{ s.materialColorCode || s.materialColorDescription }}</span>
                                @if (s.materialColorCode && s.materialColorDescription) {
                                  <span class="text-muted-foreground block">{{ s.materialColorDescription }}</span>
                                }
                                <span class="block" [class]="s.confidence >= 80 ? 'text-emerald-700 dark:text-emerald-400' : 'text-amber-700 dark:text-amber-400'"
                                      [title]="(s.basis === 'colorway' ? 'styles.colours.basisColorway' : 'styles.colours.basisMaterial') | transloco: { lines: s.lines, of: s.of }">
                                  {{ s.confidence }}% · {{ s.lines }}/{{ s.of }}
                                </span>
                              </span>
                            </label>
                          } @else if (filled(row.line.bomLineId, c.colorwayId)) {
                            <span class="text-muted-foreground">{{ filled(row.line.bomLineId, c.colorwayId) }}</span>
                          } @else {
                            <span class="text-muted-foreground">—</span>
                          }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p class="text-muted-foreground text-xs">{{ 'styles.colours.legend' | transloco }}</p>
          }
        }
      </div>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving() || !ticked().size" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ 'styles.colours.fillN' | transloco: { count: ticked().size } }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class ColourSuggestDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly styleId = input(0);
  readonly styleNo = input('');
  readonly lines = input<BomLine[]>([]);
  readonly colorways = input<Colorway[]>([]);
  readonly closed = output<void>();
  readonly saved = output<number>();
  /** The style has no colorways yet: go and add them. */
  readonly addColorways = output<void>();

  readonly result = signal<ColourSuggestion | null>(null);
  readonly loading = signal(false);
  readonly steps = ['cells', 'history', 'pick'] as const;
  readonly saving = signal(false);
  readonly ticked = signal<ReadonlySet<string>>(new Set());

  readonly keyOf = key;
  private readonly byCell = computed(() => new Map((this.result()?.suggestions ?? []).map((s) => [key(s), s])));
  /** Lines with at least one suggestion, in BOM order. */
  readonly rows = computed(() => {
    const withSuggestion = new Set((this.result()?.suggestions ?? []).map((s) => s.bomLineId));
    return this.lines().filter((l) => withSuggestion.has(l.bomLineId)).map((line) => ({ line }));
  });
  readonly allTicked = computed(() => {
    const all = this.result()?.suggestions ?? [];
    return all.length > 0 && all.every((s) => this.ticked().has(key(s)));
  });

  constructor() {
    effect(() => {
      if (this.open() && this.colorways().length) this.load();
    });
  }

  cell = (bomLineId: number, colorwayId: number) => this.byCell().get(`${bomLineId}|${colorwayId}`) ?? null;

  /** The colour already on the cell (shown, never changed). */
  filled(bomLineId: number, colorwayId: number): string | null {
    const c = this.lines().find((l) => l.bomLineId === bomLineId)?.colorways.find((x) => x.colorwayId === colorwayId);
    return c?.materialColorCode || c?.materialColorDescription || null;
  }

  toggle(s: SuggestedColour): void {
    this.ticked.update((t) => {
      const next = new Set(t);
      if (!next.delete(key(s))) next.add(key(s));
      return next;
    });
  }

  toggleAll(): void {
    this.ticked.set(this.allTicked() ? new Set() : new Set((this.result()?.suggestions ?? []).map(key)));
  }

  save(): void {
    if (this.saving() || !this.ticked().size) return;
    const cells = (this.result()?.suggestions ?? [])
      .filter((s) => this.ticked().has(key(s)))
      .map(({ bomLineId, colorwayId, materialColorCode, materialColorDescription }) => ({ bomLineId, colorwayId, materialColorCode, materialColorDescription }));
    this.saving.set(true);
    this.svc.applyColours(this.styleId(), cells).subscribe({
      next: (r) => {
        this.saving.set(false);
        toast.success(this.transloco.translate('styles.colours.filled', { count: r.filled }));
        this.saved.emit(r.filled);
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  private load(): void {
    this.result.set(null);
    this.ticked.set(new Set());
    this.loading.set(true);
    this.svc.suggestColours(this.styleId()).subscribe({
      next: (r) => {
        this.result.set(r);
        this.ticked.set(new Set(r.suggestions.filter((s) => s.recommended).map(key)));
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }
}
