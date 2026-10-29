import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideInfo } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { ConceptDetail, ConceptsService } from './concepts.service';

/**
 * Develop as style (roadmap 5.21): an LT original design becomes a new style of the concept's customer (it becomes the brand's
 * design once adopted). The user gives the season and style number, picks the concept picture to use as its sketch, and the
 * inspiration style to start the BOM from (the customer's own brand: the whole BOM; another brand: know-how only). Emits the new
 * style's id.
 */
@Component({
  selector: 'app-develop-style-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, Modal, AuthImgDirective, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmNativeSelectImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCircleAlert, lucideInfo })],
  template: `
    <app-modal [open]="open()" size="lg" [heading]="'concepts.develop.title' | transloco" (closed)="closed.emit()">
      @if (concept(); as d) {
        @let e = errors();
        <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
          @if (!d.concept.customerCode) {
            <p class="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-amber-800 dark:text-amber-300">
              <ng-icon name="lucideCircleAlert" class="mt-0.5 shrink-0" />{{ 'concepts.develop.needCustomer' | transloco }}
            </p>
          } @else {
            <p class="text-muted-foreground text-sm">{{ 'concepts.develop.intro' | transloco: { customer: d.concept.customerName || d.concept.customerCode } }}</p>
          }
          <div class="grid gap-4 sm:grid-cols-[10rem_minmax(0,1fr)]">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="dv-season">{{ 'packages.copy.season' | transloco }}</label>
              <input hlmInput id="dv-season" list="dv-seasons" class="h-11 lg:h-9" maxlength="16" autocomplete="off" placeholder="2028-SS"
                     [value]="season()" (input)="season.set($any($event.target).value)" [attr.aria-invalid]="!!e['seasonCode']" />
              <datalist id="dv-seasons">
                @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code">{{ s.code === s.name ? '' : s.name }}</option> }
              </datalist>
              @if (e['seasonCode']) { <p class="text-destructive text-xs">{{ e['seasonCode'] }}</p> }
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="dv-no">{{ 'packages.copy.styleNo' | transloco }}</label>
              <input hlmInput id="dv-no" class="h-11 font-mono lg:h-9" maxlength="100" autocomplete="off"
                     [value]="styleNo()" (input)="styleNo.set($any($event.target).value)" [attr.aria-invalid]="!!e['styleNo']" />
              @if (e['styleNo']) { <p class="text-destructive text-xs">{{ e['styleNo'] }}</p> }
            </div>
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="dv-base">{{ 'concepts.develop.base' | transloco }}</label>
            <hlm-native-select selectId="dv-base" selectClass="h-11 w-full lg:h-9" [value]="base()" (valueChange)="base.set($event ?? '')">
              <option hlmNativeSelectOption value="">{{ 'concepts.develop.noBase' | transloco }}</option>
              @for (s of d.styles; track s.styleId) {
                <option hlmNativeSelectOption [value]="'' + s.styleId">
                  {{ s.styleNo }} · {{ s.customerCode }} {{ s.seasonCode }} · {{ (s.sameBrand === false ? 'concepts.develop.knowHow' : 'concepts.develop.wholeBom') | transloco }}
                </option>
              }
            </hlm-native-select>
            <p class="text-muted-foreground text-xs">{{ 'concepts.develop.baseHint' | transloco }}</p>
          </div>

          <fieldset class="flex flex-col gap-1.5">
            <legend class="mb-1.5 text-sm font-medium">{{ 'concepts.develop.picture' | transloco }}</legend>
            <div class="flex flex-wrap gap-2" role="radiogroup">
              <button type="button" role="radio" class="text-muted-foreground flex size-20 items-center justify-center rounded-md border text-xs"
                      [class]="picture() === null ? 'border-primary ring-primary/30 ring-2' : ''" [attr.aria-checked]="picture() === null" (click)="picture.set(null)">
                {{ 'concepts.develop.noPicture' | transloco }}
              </button>
              @for (img of d.images; track img.conceptImageId) {
                <button type="button" role="radio" class="bg-muted size-20 overflow-hidden rounded-md border"
                        [class]="picture() === img.conceptImageId ? 'border-primary ring-primary/30 ring-2' : ''" [attr.aria-checked]="picture() === img.conceptImageId"
                        [attr.aria-label]="img.caption || ('concepts.develop.picture' | transloco)" (click)="picture.set(img.conceptImageId)">
                  <img [appAuthSrc]="img.imageUrl + '?w=160'" alt="" class="size-full object-contain" />
                </button>
              }
            </div>
            <p class="text-muted-foreground text-xs">{{ 'concepts.develop.pictureHint' | transloco }}</p>
          </fieldset>
          <p class="text-muted-foreground flex items-start gap-1.5 text-xs"><ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />{{ 'concepts.develop.what' | transloco }}</p>
          <button type="submit" class="hidden"></button>
        </form>
      }
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving() || !concept()?.concept?.customerCode" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ 'concepts.develop.submit' | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class DevelopStyleDialog {
  private readonly svc = inject(ConceptsService);
  private readonly styles = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly concept = input<ConceptDetail | null>(null);
  readonly lookups = input<StyleLookups | null>(null);
  readonly closed = output<void>();
  readonly developed = output<number>();

  readonly season = signal('');
  readonly styleNo = signal('');
  readonly base = signal('');
  readonly picture = signal<number | null>(null);
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const d = this.concept();
      this.season.set(d?.concept.seasonCode ?? '');
      this.styleNo.set('');
      // the customer's own inspiration style first, else none; the first picture
      this.base.set(String(d?.styles.find((s) => s.sameBrand)?.styleId ?? ''));
      this.picture.set(d?.images[0]?.conceptImageId ?? null);
      this.errors.set({});
    });
  }

  save(): void {
    const d = this.concept();
    if (!d || this.saving()) return;
    const errors: Record<string, string> = {};
    if (!this.season().trim()) errors['seasonCode'] = this.transloco.translate('packages.copy.required');
    if (!this.styleNo().trim()) errors['styleNo'] = this.transloco.translate('packages.copy.required');
    this.errors.set(errors);
    if (Object.keys(errors).length) return;
    this.saving.set(true);
    this.svc
      .develop(d.concept.conceptId, {
        seasonCode: this.season().trim().toUpperCase(), styleNo: this.styleNo().trim(), baseStyleId: this.base() ? Number(this.base()) : null,
        conceptImageId: this.picture(),
      })
      .subscribe({
        next: (r) => {
          this.saving.set(false);
          this.styles.refreshLookups();
          this.developed.emit(r.styleId);
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
