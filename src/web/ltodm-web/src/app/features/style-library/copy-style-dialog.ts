import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { StyleHeader, StyleLibraryService, StyleLookups, styleError } from './style-library.service';

/** '2028-SS' -> 'SS28' (the suffix customers add when they reuse a style in a season). */
export function seasonSuffix(seasonCode: string): string {
  const m = /^(\d{2})(\d{2})-([A-Z]{2,4})$/.exec(seasonCode.trim().toUpperCase());
  return m ? `${m[3]}${m[2]}` : '';
}

/**
 * Reuse a style in another season (carry-over) or the same season (variant): copies the header, colorways,
 * BOM and images to a new style number and records the history link. Suggests base number + season suffix.
 */
@Component({
  selector: 'app-copy-style-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmSpinnerImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" [heading]="'styles.copy.title' | transloco: { styleNo: style()?.styleNo ?? '' }" (closed)="closed.emit()">
      @let e = errors();
      <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
        <p class="text-muted-foreground text-sm">
          {{ 'styles.copy.hint' | transloco: { styleNo: style()?.styleNo } }}
        </p>
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cp-season">{{ 'styles.copy.season' | transloco }}</label>
            <input hlmInput id="cp-season" list="cp-seasons" class="h-11 lg:h-9" maxlength="16" placeholder="2028-SS" autocomplete="off"
              [value]="season()" (input)="onSeason($any($event.target).value)" [attr.aria-invalid]="!!e['seasonCode']" />
            <datalist id="cp-seasons">
              @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code"></option> }
            </datalist>
            @if (e['seasonCode']) { <p class="text-destructive text-xs">{{ e['seasonCode'] }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cp-styleno">{{ 'styles.copy.newStyleNo' | transloco }}</label>
            <input hlmInput id="cp-styleno" class="h-11 font-mono lg:h-9" maxlength="40" autocomplete="off"
              [value]="styleNo()" (input)="onStyleNo($any($event.target).value)" [attr.aria-invalid]="!!e['styleNo']" />
            @if (e['styleNo']) { <p class="text-destructive text-xs">{{ e['styleNo'] }}</p> }
          </div>
        </div>
        <button type="submit" class="hidden"></button>
      </form>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ 'styles.copy.submit' | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class CopyStyleDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly style = input<StyleHeader | null>(null);
  readonly lookups = input<StyleLookups | null>(null);
  readonly closed = output<void>();
  readonly saved = output<number>();

  readonly season = signal('');
  readonly styleNo = signal('');
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);
  /** Keep suggesting a number until the user types their own. */
  private styleNoTouched = false;

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.season.set('');
      this.styleNo.set('');
      this.errors.set({});
      this.styleNoTouched = false;
    });
  }

  onSeason(value: string): void {
    this.season.set(value);
    const base = this.style()?.baseStyleNo;
    const suffix = seasonSuffix(value);
    if (!this.styleNoTouched && base && suffix) this.styleNo.set(`${base}_${suffix}`);
  }

  onStyleNo(value: string): void {
    this.styleNoTouched = true;
    this.styleNo.set(value);
  }

  save(): void {
    const s = this.style();
    if (!s || this.saving()) return;
    const season = this.season().trim().toUpperCase();
    const styleNo = this.styleNo().trim();
    const errors: Record<string, string> = {};
    if (!/^[0-9]{4}-[A-Z]{2,4}$/.test(season)) errors['seasonCode'] = this.transloco.translate('styles.form.seasonInvalid', { example: '2028-SS' });
    if (!styleNo) errors['styleNo'] = this.transloco.translate('styles.copy.newStyleNoRequired');
    this.errors.set(errors);
    if (Object.keys(errors).length) return;

    this.saving.set(true);
    this.svc.copyStyle(s.styleId, season, styleNo).subscribe({
      next: (id) => {
        this.saving.set(false);
        this.svc.refreshLookups();
        toast.success(this.transloco.translate('styles.copy.created', { newStyleNo: styleNo, styleNo: s.styleNo }));
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
