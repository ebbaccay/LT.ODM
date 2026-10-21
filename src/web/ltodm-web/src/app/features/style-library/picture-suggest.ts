import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideWandSparkles } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { PictureDescription, StyleLibraryService, styleError } from './style-library.service';

/**
 * Suggest from picture (style form): reads the photo, else the sketch, with the image model built into the API and offers
 * the garment's product types, a draft description and, from the closest-looking library styles, knit / woven and the
 * usual fabric. Nothing changes until a suggestion is used. Gender is never suggested: a picture does not show it
 * (55-60% right on the library's sketches, less than always guessing women's).
 */
@Component({
  selector: 'app-picture-suggest',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButtonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideWandSparkles })],
  template: `
    @if (picture(); as pic) {
      <div class="bg-muted/30 flex flex-col gap-3 rounded-md border p-3 text-sm">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <p class="text-muted-foreground text-xs leading-relaxed">{{ 'styles.picture.hint' | transloco }}</p>
          <button hlmBtn variant="outline" type="button" class="h-11 lg:h-8" [disabled]="busy()" (click)="read(pic.url, pic.isSketch)">
            @if (busy()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideWandSparkles" /> }
            {{ (result() ? 'styles.picture.again' : 'styles.picture.button') | transloco }}
          </button>
        </div>
        @if (problem()) {
          <p class="text-xs text-amber-800 dark:text-amber-300">{{ problem() }}</p>
        }
        @if (result(); as r) {
          <dl class="grid gap-3 sm:grid-cols-[9rem_minmax(0,1fr)]">
            <dt class="text-muted-foreground text-xs">{{ 'styles.picture.garment' | transloco }}</dt>
            <dd>
              @if (r.garment; as g) {
                {{ 'styles.picture.garments.' + g.value | transloco }}
                @if (!g.confident) { <span class="text-muted-foreground text-xs">({{ 'styles.picture.unsure' | transloco }})</span> }
              } @else { — }
              @if (r.productTypes.length) {
                <span class="mt-1.5 flex flex-wrap gap-1.5">
                  @for (t of r.productTypes; track t.productTypeCode) {
                    <button hlmBtn variant="outline" size="sm" type="button" class="h-11 text-xs lg:h-7"
                            [class.border-primary]="productTypeCode() === t.productTypeCode" (click)="productType.emit(t.productTypeCode)">
                      {{ t.name }} <span class="text-muted-foreground font-mono">{{ t.productTypeCode }}</span>
                    </button>
                  }
                </span>
                <span class="text-muted-foreground mt-1 block text-xs">{{ 'styles.picture.pickType' | transloco }}</span>
              }
            </dd>

            <dt class="text-muted-foreground text-xs">{{ 'styles.picture.description' | transloco }}</dt>
            <dd class="flex flex-wrap items-center gap-2">
              @if (r.description) {
                <span class="font-mono text-xs">{{ r.description }}</span>
                <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 text-xs lg:h-7" (click)="useDescription(r.description)">
                  {{ (currentDescription()?.trim() ? 'styles.picture.addToDescription' : 'styles.picture.use') | transloco }}
                </button>
              } @else {
                <span class="text-muted-foreground text-xs">{{ 'styles.picture.nothingSure' | transloco }}</span>
              }
            </dd>

            @if (r.weave; as w) {
              <dt class="text-muted-foreground text-xs">{{ 'styles.picture.weave' | transloco }}</dt>
              <dd class="flex flex-wrap items-center gap-2 text-xs">
                <span>{{ 'styles.picture.weaveHint' | transloco: { weave: ('styles.picture.weaves.' + w.value | transloco), votes: w.votes, of: w.of } }}</span>
                <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 text-xs lg:h-7" (click)="weave.emit(w.value)">{{ 'styles.picture.use' | transloco }}</button>
              </dd>
            }
            @if (r.fabric; as fb) {
              <dt class="text-muted-foreground text-xs">{{ 'styles.picture.fabric' | transloco }}</dt>
              <dd class="flex flex-wrap items-center gap-2 text-xs">
                <span>{{ 'styles.picture.fabricHint' | transloco: { fabric: fb.value, votes: fb.votes, of: fb.of } }}</span>
                <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 text-xs lg:h-7" (click)="useDescription(fb.value.toUpperCase())">
                  {{ 'styles.picture.addToDescription' | transloco }}
                </button>
              </dd>
            }

            <dt class="text-muted-foreground text-xs">{{ 'styles.picture.gender' | transloco }}</dt>
            <dd class="text-muted-foreground text-xs">{{ 'styles.picture.genderNote' | transloco }}</dd>

            @if (r.similarStyles.length) {
              <dt class="text-muted-foreground text-xs">{{ 'styles.picture.similar' | transloco }}</dt>
              <dd class="flex flex-wrap gap-x-3 gap-y-1 text-xs">
                @for (s of r.similarStyles; track s.styleId) {
                  <a class="text-primary font-mono underline-offset-2 hover:underline" [href]="'/styles/' + s.styleId" target="_blank" rel="noopener"
                     [title]="s.customerCode + ' · ' + s.seasonCode + ' · ' + (s.productTypeName ?? '')">{{ s.styleNo }}</a>
                }
              </dd>
            }
          </dl>
          <p class="text-muted-foreground text-xs">{{ 'styles.picture.hintsNote' | transloco }}</p>
        }
      </div>
    }
  `,
})
export class PictureSuggest {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly sketchUrl = input<string | null>(null);
  readonly imageUrl = input<string | null>(null);
  readonly customerCode = input<string | null>(null);
  readonly productTypeCode = input<string | null>(null);
  readonly currentDescription = input<string | null>(null);
  /** The product type picked (a code). */
  readonly productType = output<string>();
  /** The new description text (the draft, or the current one with the draft added). */
  readonly description = output<string>();
  /** KNT or WVN. */
  readonly weave = output<string>();

  readonly result = signal<PictureDescription | null>(null);
  readonly busy = signal(false);
  readonly problem = signal<string | null>(null);

  /** Only pictures stored by the API can be read (an import may have left another address). */
  readonly picture = computed(() => {
    const photo = this.imageUrl();
    const sketch = this.sketchUrl();
    if (photo?.startsWith('/api/v1/styles/images/')) return { url: photo, isSketch: false };
    if (sketch?.startsWith('/api/v1/styles/images/')) return { url: sketch, isSketch: true };
    return null;
  });

  constructor() {
    // A new picture: the old reading no longer applies.
    effect(() => {
      this.picture();
      this.result.set(null);
      this.problem.set(null);
    });
  }

  read(url: string, isSketch: boolean): void {
    this.busy.set(true);
    this.problem.set(null);
    this.svc.describePicture(url, isSketch, this.customerCode()?.trim().toUpperCase() || null).subscribe({
      next: (r) => {
        this.result.set(r);
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        this.problem.set(styleError(e).message || this.transloco.translate('styles.picture.failed'));
      },
    });
  }

  useDescription(text: string): void {
    const current = this.currentDescription()?.trim();
    this.description.emit(current ? `${current}; ${text}` : text);
  }
}
