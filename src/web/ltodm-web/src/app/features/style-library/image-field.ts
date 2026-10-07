import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideImagePlus, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { compressImage } from '@tms/shared/image-compress';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { StyleLibraryService, styleError } from './style-library.service';

/** Longest side of uploaded sketches and photos, in pixels. */
const MAX_DIMENSION = 1600;

/**
 * An image slot (style sketch / photo, colorway or BOM line image): shows the image, and for editors uploads a new
 * one (resized, JPEG) or clears it. Emits the new URL (or null); the parent saves it with its record.
 */
@Component({
  selector: 'app-image-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, AuthImgDirective, HlmButtonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideImage, lucideImagePlus, lucideTrash2 })],
  template: `
    <div class="flex flex-col gap-2">
      <div class="bg-muted relative flex aspect-[4/3] items-center justify-center overflow-hidden rounded-md border" [class]="sizeClass()">
        @if (url()) {
          <img [appAuthSrc]="url()" [alt]="label()" class="size-full object-contain" (loadError)="broken.set(true)" />
          @if (broken()) {
            <span class="text-muted-foreground absolute inset-0 flex items-center justify-center text-xs">{{ 'styles.image.notFound' | transloco }}</span>
          }
        } @else {
          <span class="text-muted-foreground flex flex-col items-center gap-1 text-xs">
            <ng-icon name="lucideImage" class="text-2xl" />{{ label() }}
          </span>
        }
        @if (uploading()) {
          <span class="bg-background/70 absolute inset-0 flex items-center justify-center"><hlm-spinner /></span>
        }
      </div>
      @if (editable()) {
        <div class="flex gap-2">
          <button hlmBtn variant="outline" size="sm" type="button" class="h-11 flex-1 lg:h-8" [disabled]="uploading()" (click)="fileInput.click()">
            <ng-icon name="lucideImagePlus" />{{ (url() ? 'styles.image.replace' : 'styles.image.upload') | transloco: { label: label().toLowerCase() } }}
          </button>
          <input #fileInput type="file" class="sr-only" tabindex="-1" aria-hidden="true" accept="image/jpeg,image/png,image/gif,image/webp" (change)="onFile($event)" />
          @if (url()) {
            <button hlmBtn variant="ghost" size="sm" type="button" class="text-destructive h-11 lg:h-8" [attr.aria-label]="'styles.image.remove' | transloco: { label: label().toLowerCase() }" (click)="changed.emit(null)">
              <ng-icon name="lucideTrash2" />
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class ImageField {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly url = input<string | null>(null);
  /** Already translated by the parent. */
  readonly label = input('Image');
  readonly editable = input(false);
  readonly sizeClass = input('w-full');
  readonly changed = output<string | null>();

  readonly uploading = signal(false);
  readonly broken = signal(false);

  async onFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      toast.error(this.transloco.translate('styles.image.wrongType'));
      return;
    }
    this.uploading.set(true);
    try {
      const blob = await compressImage(file, MAX_DIMENSION);
      this.svc.uploadImage(blob, 'image.jpg').subscribe({
        next: (url) => {
          this.uploading.set(false);
          this.broken.set(false);
          this.changed.emit(url);
        },
        error: (e) => {
          this.uploading.set(false);
          toast.error(styleError(e).message);
        },
      });
    } catch {
      this.uploading.set(false);
      toast.error(this.transloco.translate('styles.image.unreadable'));
    }
  }
}
