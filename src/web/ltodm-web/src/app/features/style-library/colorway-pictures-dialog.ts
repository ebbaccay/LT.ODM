import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideSave, lucideTriangleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { CropBoxEditor } from '@tms/modules/settings/style-image-import/crop-box-editor';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { toneBadge } from '@tms/shared/ui/tones';
import { firstValueFrom } from 'rxjs';
import { Colorway, ColorwayPictures, PictureBox, StyleLibraryService, styleError } from './style-library.service';

interface Row {
  colorwayId: number;
  code: string;
  name: string | null;
  imageUrl: string | null;
  nameColour: string | null;
  group: number | null;
  sure: boolean;
  box: PictureBox | null;
  ticked: boolean;
}

/**
 * Style page > Colorway pictures: splits the style's sketch (or picture) into its colourways and suggests, from the
 * colour in each colorway's name, which part belongs to which colorway (GET /api/v1/styles/:id/colorway-pictures;
 * rules on the server, no AI call). The editor checks each box (drag, or pick another colour group) and saves: the
 * server cuts each part, makes it sharper when small and sets it as the colorway's picture.
 */
@Component({
  selector: 'app-colorway-pictures-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, NgIcon, AuthImgDirective, CropBoxEditor, HlmButtonImports, HlmCheckboxImports, HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideImage, lucideSave, lucideTriangleAlert })],
  template: `
    <app-modal [open]="open()" [heading]="'styles.cwPictures.title' | transloco: { styleNo: styleNo() }" size="lg" (closed)="closed.emit()">
      <div class="flex flex-col gap-4">
        <p class="text-muted-foreground text-sm">{{ 'styles.cwPictures.intro' | transloco }}</p>

        @if (loading()) {
          <div hlmSkeleton class="h-40 w-full"></div>
        } @else if (error(); as e) {
          <p class="text-destructive text-sm" role="alert">{{ e }}</p>
        } @else if (data(); as d) {
          @if (sources().length > 1) {
            <div class="bg-muted flex w-fit rounded-lg p-1" role="radiogroup" [attr.aria-label]="'styles.cwPictures.source' | transloco">
              @for (src of sources(); track src) {
                <button type="button" role="radio" class="min-h-9 rounded-md px-3 text-sm font-medium transition-colors"
                        [class]="d.source === src ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'"
                        [attr.aria-checked]="d.source === src" (click)="load(src)">
                  {{ (src === 'Sketch' ? 'styles.detail.sketch' : 'styles.detail.photo') | transloco }}
                </button>
              }
            </div>
          }
          @if (!d.groups.length) {
            <p class="text-sm">{{ 'styles.cwPictures.noGroups' | transloco }}</p>
          }
          <ul class="flex flex-col gap-4" role="list">
            @for (r of rows(); track r.colorwayId) {
              <li class="grid gap-3 rounded-md border p-3 sm:grid-cols-[minmax(0,14rem)_minmax(0,1fr)]">
                @if (sourceUrl(); as src) {
                  <app-crop-box-editor [src]="src" [naturalWidth]="d.width" [naturalHeight]="d.height" [box]="r.box"
                                       (boxChange)="setBox(r, $event)" [label]="r.code" />
                }
                <div class="flex min-w-0 flex-col gap-2">
                  <div class="flex items-start gap-2">
                    <hlm-checkbox [inputId]="'cwp-' + r.colorwayId" [checked]="r.ticked" [disabled]="!r.box || saving()" (checkedChange)="toggle(r)" />
                    <label class="min-w-0 flex-1 cursor-pointer" [for]="'cwp-' + r.colorwayId">
                      <span class="block font-mono text-sm font-medium">{{ r.code }}</span>
                      <span class="text-muted-foreground block text-xs">{{ r.name || '—' }}</span>
                    </label>
                    @if (r.imageUrl) {
                      <span class="size-10 shrink-0 overflow-hidden rounded border bg-white" [title]="'styles.cwPictures.current' | transloco">
                        <img [appAuthSrc]="r.imageUrl" alt="" class="size-full object-contain" />
                      </span>
                    }
                  </div>
                  <div class="flex flex-wrap items-center gap-1.5 text-xs">
                    @if (r.nameColour) {
                      <span class="size-4 rounded-full border" [style.background]="r.nameColour" [title]="'styles.cwPictures.nameColour' | transloco"></span>
                    }
                    @if (!r.nameColour) {
                      <span class="rounded px-1.5 py-0.5" [class]="toneBadge('neutral')">{{ 'styles.cwPictures.noNameColour' | transloco }}</span>
                    } @else if (r.group === null) {
                      <span class="rounded px-1.5 py-0.5" [class]="toneBadge('neutral')">{{ 'styles.cwPictures.noMatch' | transloco }}</span>
                    } @else if (r.sure) {
                      <span class="rounded px-1.5 py-0.5" [class]="toneBadge('green')">{{ 'styles.cwPictures.matched' | transloco }}</span>
                    } @else {
                      <span class="inline-flex items-center gap-1 rounded px-1.5 py-0.5" [class]="toneBadge('amber')"><ng-icon name="lucideTriangleAlert" />{{ 'styles.cwPictures.check' | transloco }}</span>
                    }
                  </div>
                  @if (d.groups.length) {
                    <div class="flex flex-wrap items-center gap-1.5">
                      <span class="text-muted-foreground text-xs">{{ 'styles.cwPictures.pickGroup' | transloco }}</span>
                      @for (g of d.groups; track g.index) {
                        <button type="button" class="size-7 rounded-full border-2 transition-transform hover:scale-110"
                                [class]="r.group === g.index ? 'border-primary' : 'border-border'" [style.background]="g.colour"
                                [attr.aria-label]="('styles.cwPictures.useGroup' | transloco: { n: g.index + 1 })" (click)="useGroup(r, g.index)"></button>
                      }
                    </div>
                  }
                </div>
              </li>
            }
          </ul>
          <label class="flex items-center gap-2 text-sm" for="cwp-enhance">
            <hlm-checkbox inputId="cwp-enhance" [checked]="enhance()" (checkedChange)="enhance.set(!enhance())" />
            {{ 'settings.imageImport.enhance' | transloco }}
          </label>
        }

        <div class="flex flex-wrap justify-end gap-2 border-t pt-4">
          <button hlmBtn variant="ghost" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
          <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="!tickedCount() || saving()" (click)="save()">
            @if (saving()) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideSave" /> }
            {{ 'styles.cwPictures.save' | transloco: { count: tickedCount() } }}
          </button>
        </div>
      </div>
    </app-modal>
  `,
})
export class ColorwayPicturesDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly styleId = input.required<number>();
  readonly styleNo = input('');
  /** The style's colorways as loaded on the page (their RowVer is sent back when saving). */
  readonly colorways = input<Colorway[]>([]);
  /** Which pictures the style has (the source switch shows both when both exist). */
  readonly hasSketch = input(false);
  readonly hasImage = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly toneBadge = toneBadge;
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly data = signal<ColorwayPictures | null>(null);
  readonly rows = signal<Row[]>([]);
  readonly sourceUrl = signal<string | null>(null);
  readonly enhance = signal(true);
  readonly tickedCount = computed(() => this.rows().filter((r) => r.ticked && r.box).length);
  readonly sources = computed(() => [...(this.hasSketch() ? ['Sketch' as const] : []), ...(this.hasImage() ? ['Image' as const] : [])]);

  constructor() {
    effect(() => {
      if (this.open()) untracked(() => this.load());
    });
    inject(DestroyRef).onDestroy(() => this.dropSource());
  }

  load(source?: 'Sketch' | 'Image'): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.colorwayPictures(this.styleId(), source).subscribe({
      next: async (d) => {
        try {
          const blob = await firstValueFrom(this.svc.imageBlob(d.sourceUrl));
          this.dropSource();
          this.sourceUrl.set(URL.createObjectURL(blob));
        } catch {
          this.sourceUrl.set(null);
        }
        this.data.set(d);
        this.rows.set(d.colorways.map((c) => ({
          colorwayId: c.colorwayId, code: c.colorwayCode, name: c.colorwayName, imageUrl: c.imageUrl, nameColour: c.nameColour,
          group: c.group, sure: c.sure, box: c.box, ticked: !!c.box && c.sure,
        })));
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(styleError(e).message);
        this.loading.set(false);
      },
    });
  }

  setBox(r: Row, box: PictureBox | null): void {
    this.patch(r.colorwayId, { box, ticked: !!box });
  }

  useGroup(r: Row, index: number): void {
    const g = this.data()?.groups[index];
    if (g) this.patch(r.colorwayId, { group: index, box: g.box, ticked: true });
  }

  toggle(r: Row): void {
    this.patch(r.colorwayId, { ticked: !r.ticked });
  }

  async save(): Promise<void> {
    const d = this.data();
    if (!d) return;
    const rowVers = new Map(this.colorways().map((c) => [c.colorwayId, c.rowVer]));
    const items = this.rows()
      .filter((r) => r.ticked && r.box && rowVers.has(r.colorwayId))
      .map((r) => ({ colorwayId: r.colorwayId, rowVer: rowVers.get(r.colorwayId)!, ...roundBox(r.box!) }));
    if (!items.length) return;
    this.saving.set(true);
    try {
      const result = await firstValueFrom(this.svc.saveColorwayPictures(this.styleId(), { sourceUrl: d.sourceUrl, enhance: this.enhance(), items }));
      const problems = result.filter((r) => r.problem);
      const done = result.length - problems.length;
      if (problems.length) toast.error(this.transloco.translate('styles.cwPictures.someFailed', { done, failed: problems.length }) + ' ' + problems[0].problem);
      else toast.success(this.transloco.translate('styles.cwPictures.saved', { count: done }));
      this.saved.emit();
    } catch (e) {
      toast.error(styleError(e).message);
    } finally {
      this.saving.set(false);
    }
  }

  private patch(id: number, change: Partial<Row>): void {
    this.rows.update((list) => list.map((r) => (r.colorwayId === id ? { ...r, ...change } : r)));
  }

  private dropSource(): void {
    const url = this.sourceUrl();
    if (url) URL.revokeObjectURL(url);
  }
}

function roundBox(b: PictureBox): PictureBox {
  return { x: Math.round(b.x), y: Math.round(b.y), width: Math.round(b.width), height: Math.round(b.height) };
}
