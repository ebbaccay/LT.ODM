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
import { GENDERS, StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { CONCEPT_STATUSES, ConceptDetail, ConceptLookups, ConceptStatus, ConceptsService, SaveConcept } from './concepts.service';

interface ConceptForm {
  name: string;
  customerCode: string;
  seasonCode: string;
  targetMarketCode: string;
  productCategoryCode: string;
  gender: string;
  targetPrice: string;
  description: string;
  clientStory: string;
  status: ConceptStatus;
  ownerName: string;
  trendTags: string[];
}

/**
 * New concept (concept null) or its details: name, customer (optional: an idea need not have a client yet), season, target
 * market, product category, gender, target price, the idea, trend tags; on an existing concept also status and owner. The pick
 * lists come from Settings > Reference lists; customer and season can be typed (new codes are added, as on the package form).
 * Emits the concept id when saved.
 */
@Component({
  selector: 'app-concept-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmNativeSelectImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" size="lg" [heading]="(concept() ? 'concepts.form.editTitle' : 'concepts.form.newTitle') | transloco" (closed)="closed.emit()">
      @let e = errors();
      @let f = form();
      @let l = conceptLookups();
      <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="cf-name">{{ 'concepts.form.name' | transloco }}</label>
          <input hlmInput id="cf-name" class="h-11 lg:h-9" maxlength="150" autocomplete="off" [placeholder]="'concepts.form.namePlaceholder' | transloco"
                 [value]="f.name" (input)="set('name', $any($event.target).value)" [attr.aria-invalid]="!!e['name']" />
          @if (e['name']) { <p class="text-destructive text-xs">{{ e['name'] }}</p> }
        </div>
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cf-customer">{{ 'concepts.form.customer' | transloco }}</label>
            <input hlmInput id="cf-customer" list="cf-customers" class="h-11 lg:h-9" maxlength="32" autocomplete="off"
                   [value]="f.customerCode" (input)="set('customerCode', $any($event.target).value)" [attr.aria-invalid]="!!e['customerCode']" />
            <datalist id="cf-customers">
              @for (c of lookups()?.customers ?? []; track c.code) { <option [value]="c.code">{{ c.code === c.name ? '' : c.name }}</option> }
            </datalist>
            @if (e['customerCode']) { <p class="text-destructive text-xs">{{ e['customerCode'] }}</p> }
            @else { <p class="text-muted-foreground text-xs">{{ 'concepts.form.customerHint' | transloco }}</p> }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cf-season">{{ 'concepts.form.season' | transloco }}</label>
            <input hlmInput id="cf-season" list="cf-seasons" class="h-11 lg:h-9" maxlength="16" autocomplete="off" placeholder="2028-SS"
                   [value]="f.seasonCode" (input)="set('seasonCode', $any($event.target).value)" [attr.aria-invalid]="!!e['seasonCode']" />
            <datalist id="cf-seasons">
              @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code">{{ s.code === s.name ? '' : s.name }}</option> }
            </datalist>
            @if (e['seasonCode']) { <p class="text-destructive text-xs">{{ e['seasonCode'] }}</p> }
          </div>
        </div>
        <div class="grid gap-4 sm:grid-cols-3">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cf-category">{{ 'concepts.form.category' | transloco }}</label>
            <hlm-native-select selectId="cf-category" selectClass="h-11 w-full lg:h-9" [value]="f.productCategoryCode" (valueChange)="set('productCategoryCode', $event ?? '')">
              <option hlmNativeSelectOption value="">{{ 'concepts.form.none' | transloco }}</option>
              @for (c of l?.productCategories ?? []; track c.code) { <option hlmNativeSelectOption [value]="c.code">{{ c.name }}</option> }
            </hlm-native-select>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cf-gender">{{ 'concepts.form.gender' | transloco }}</label>
            <hlm-native-select selectId="cf-gender" selectClass="h-11 w-full lg:h-9" [value]="f.gender" (valueChange)="set('gender', $event ?? '')">
              <option hlmNativeSelectOption value="">{{ 'concepts.form.none' | transloco }}</option>
              @for (g of genders; track g.code) { <option hlmNativeSelectOption [value]="g.code">{{ 'styles.gender.' + g.code | transloco }}</option> }
            </hlm-native-select>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cf-market">{{ 'concepts.form.market' | transloco }}</label>
            <hlm-native-select selectId="cf-market" selectClass="h-11 w-full lg:h-9" [value]="f.targetMarketCode" (valueChange)="set('targetMarketCode', $event ?? '')">
              <option hlmNativeSelectOption value="">{{ 'concepts.form.none' | transloco }}</option>
              @for (m of l?.targetMarkets ?? []; track m.code) { <option hlmNativeSelectOption [value]="m.code">{{ m.name }}</option> }
            </hlm-native-select>
          </div>
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="cf-price">{{ 'concepts.form.targetPrice' | transloco }}</label>
          <input hlmInput id="cf-price" class="h-11 lg:h-9 sm:max-w-xs" maxlength="50" autocomplete="off" placeholder="USD 12-14 FOB"
                 [value]="f.targetPrice" (input)="set('targetPrice', $any($event.target).value)" [attr.aria-invalid]="!!e['targetPrice']" />
          @if (e['targetPrice']) { <p class="text-destructive text-xs">{{ e['targetPrice'] }}</p> }
        </div>
        <fieldset class="flex flex-col gap-1.5">
          <legend class="mb-1.5 text-sm font-medium">{{ 'concepts.form.trends' | transloco }}</legend>
          <div class="flex flex-wrap gap-1.5">
            @for (t of l?.trendTags ?? []; track t.code) {
              @let on = f.trendTags.includes(t.code);
              <button type="button" class="min-h-9 rounded-full border px-3 text-xs font-medium transition-colors"
                      [class]="on ? 'border-primary bg-primary/10 text-primary' : 'text-muted-foreground hover:bg-accent/40'"
                      [attr.aria-pressed]="on" (click)="toggleTag(t.code)">{{ t.name }}</button>
            } @empty {
              <p class="text-muted-foreground text-xs">{{ 'concepts.form.noTags' | transloco }}</p>
            }
          </div>
        </fieldset>
        @if (concept()) {
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="cf-status">{{ 'concepts.form.status' | transloco }}</label>
              <hlm-native-select selectId="cf-status" selectClass="h-11 w-full lg:h-9" [value]="f.status" (valueChange)="set('status', $event ?? 'Draft')">
                @for (s of statuses; track s) { <option hlmNativeSelectOption [value]="s">{{ 'concepts.status.' + s | transloco }}</option> }
              </hlm-native-select>
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="cf-owner">{{ 'concepts.form.owner' | transloco }}</label>
              <input hlmInput id="cf-owner" class="h-11 lg:h-9" maxlength="64" autocomplete="off"
                     [value]="f.ownerName" (input)="set('ownerName', $any($event.target).value)" [attr.aria-invalid]="!!e['ownerName']" />
              @if (e['ownerName']) { <p class="text-destructive text-xs">{{ e['ownerName'] }}</p> }
            </div>
          </div>
        }
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="cf-desc">{{ 'concepts.form.description' | transloco }}</label>
          <textarea hlmTextarea id="cf-desc" rows="4" maxlength="2000" [placeholder]="'concepts.form.descriptionPlaceholder' | transloco"
                    [value]="f.description" (input)="set('description', $any($event.target).value)"></textarea>
          @if (e['description']) { <p class="text-destructive text-xs">{{ e['description'] }}</p> }
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="cf-story">{{ 'concepts.form.clientStory' | transloco }}</label>
          <textarea hlmTextarea id="cf-story" rows="4" maxlength="2000" [placeholder]="'concepts.form.clientStoryPlaceholder' | transloco"
                    [value]="f.clientStory" (input)="set('clientStory', $any($event.target).value)"></textarea>
          @if (e['clientStory']) { <p class="text-destructive text-xs">{{ e['clientStory'] }}</p> }
          @else { <p class="text-muted-foreground text-xs">{{ 'concepts.form.clientStoryHint' | transloco }}</p> }
        </div>
        <button type="submit" class="hidden"></button>
      </form>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ (concept() ? 'actions.save' : 'concepts.form.create') | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class ConceptFormDialog {
  private readonly svc = inject(ConceptsService);
  private readonly styles = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  /** Null = a new concept. */
  readonly concept = input<ConceptDetail | null>(null);
  readonly lookups = input<StyleLookups | null>(null);
  readonly conceptLookups = input<ConceptLookups | null>(null);
  readonly closed = output<void>();
  readonly saved = output<number>();

  readonly statuses = CONCEPT_STATUSES;
  readonly genders = GENDERS;
  readonly form = signal<ConceptForm>(this.empty());
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const d = this.concept();
      const c = d?.concept;
      this.form.set(c ? {
        name: c.name, customerCode: c.customerCode ?? '', seasonCode: c.seasonCode ?? '', targetMarketCode: c.targetMarketCode ?? '',
        productCategoryCode: c.productCategoryCode ?? '', gender: c.gender ?? '', targetPrice: c.targetPrice ?? '', description: c.description ?? '',
        status: c.status, ownerName: c.ownerName, trendTags: d!.tags.map((t) => t.code), clientStory: c.clientStory ?? '',
      } : this.empty());
      this.errors.set({});
    });
  }

  private empty(): ConceptForm {
    return {
      name: '', customerCode: '', seasonCode: '', targetMarketCode: '', productCategoryCode: '', gender: '', targetPrice: '', description: '',
      status: 'Draft', ownerName: '', trendTags: [], clientStory: '',
    };
  }

  set(key: Exclude<keyof ConceptForm, 'trendTags'>, value: string): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  toggleTag(code: string): void {
    this.form.update((f) => ({ ...f, trendTags: f.trendTags.includes(code) ? f.trendTags.filter((t) => t !== code) : [...f.trendTags, code] }));
  }

  save(): void {
    if (this.saving()) return;
    const f = this.form();
    if (!f.name.trim()) {
      this.errors.set({ name: this.transloco.translate('concepts.form.nameRequired') });
      return;
    }
    this.errors.set({});
    const c = this.concept()?.concept;
    const body: SaveConcept = {
      rowVer: c?.rowVer ?? null, name: f.name.trim(), customerCode: f.customerCode.trim().toUpperCase() || null,
      seasonCode: f.seasonCode.trim().toUpperCase() || null, targetMarketCode: f.targetMarketCode || null, productCategoryCode: f.productCategoryCode || null,
      gender: f.gender || null, targetPrice: f.targetPrice.trim() || null, description: f.description.trim() || null, status: f.status,
      clientStory: f.clientStory.trim() || null,
      ownerName: c ? f.ownerName.trim() || null : null, trendTags: f.trendTags,
    };
    this.saving.set(true);
    (c ? this.svc.update(c.conceptId, body) : this.svc.create(body)).subscribe({
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
