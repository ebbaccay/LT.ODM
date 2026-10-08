import { ChangeDetectionStrategy, Component, ElementRef, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDownload, lucideImage, lucideImagePlus, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { compressImage } from '@tms/shared/image-compress';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { StyleLibraryService, styleError } from './style-library.service';

/** Longest side of uploaded sketches and photos, in pixels. */
const MAX_DIMENSION = 1600;

/**
 * An image slot (style sketch / photo, colorway or BOM line image): shows the image and lets anyone download it; for
 * editors, uploads a new one (button, or drop a file on the box; resized, JPEG) or clears it. Emits the new URL (or
 * null); the parent saves it with its record.
 */
@Component({
  selector: 'app-image-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:dragover)': 'guardPage($event)', '(document:drop)': 'guardPage($event)' },
  imports: [NgIcon, AuthImgDirective, HlmButtonImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideDownload, lucideImage, lucideImagePlus, lucideTrash2 })],
  template: `
    <div class="flex flex-col gap-2">
      <div
        class="bg-muted relative flex aspect-[4/3] items-center justify-center overflow-hidden rounded-md border transition-colors"
        [class]="sizeClass() + (dragOver() ? ' border-primary ring-primary/40 border-dashed ring-2' : '')"
        (dragenter)="onDragOver($event)"
        (dragover)="onDragOver($event)"
        (dragleave)="onDragLeave($event)"
        (drop)="onDrop($event)"
      >
        @if (url()) {
          <img [appAuthSrc]="url()" [alt]="label()" class="size-full object-contain" (loadError)="broken.set(true)" />
          @if (broken()) {
            <span class="text-muted-foreground absolute inset-0 flex items-center justify-center text-xs">{{ 'styles.image.notFound' | transloco }}</span>
          }
        } @else {
          <span class="text-muted-foreground flex flex-col items-center gap-1 px-2 text-center text-xs">
            <ng-icon name="lucideImage" class="text-2xl" />{{ label() }}
            @if (editable()) {
              <span class="hidden sm:block">{{ 'styles.image.dropHint' | transloco }}</span>
            }
          </span>
        }
        @if (dragOver()) {
          <span class="bg-background/80 text-primary absolute inset-0 flex items-center justify-center text-sm font-medium">{{ 'styles.image.dropHere' | transloco }}</span>
        }
        @if (uploading()) {
          <span class="bg-background/70 absolute inset-0 flex items-center justify-center"><hlm-spinner /></span>
        }
      </div>
      @if (editable() || (url() && !broken())) {
        <div class="flex gap-2">
          @if (editable()) {
            <button hlmBtn variant="outline" size="sm" type="button" class="h-11 flex-1 lg:h-8" [disabled]="uploading()" (click)="fileInput.click()">
              <ng-icon name="lucideImagePlus" />{{ (url() ? 'styles.image.replace' : 'styles.image.upload') | transloco: { label: label().toLowerCase() } }}
            </button>
            <input #fileInput type="file" class="sr-only" tabindex="-1" aria-hidden="true" accept="image/jpeg,image/png,image/gif,image/webp" (change)="onFile($event)" />
          }
          @if (url() && !broken()) {
            <button hlmBtn variant="outline" size="sm" type="button" class="h-11 lg:h-8" [class.flex-1]="!editable()" [disabled]="downloading()"
              [attr.aria-label]="'styles.image.download' | transloco: { label: label().toLowerCase() }" [title]="'styles.image.download' | transloco: { label: label().toLowerCase() }"
              (click)="download()">
              <ng-icon name="lucideDownload" />
              @if (!editable()) { {{ 'styles.image.downloadShort' | transloco }} }
            </button>
          }
          @if (editable() && url()) {
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly url = input<string | null>(null);
  /** Already translated by the parent. */
  readonly label = input('Image');
  readonly editable = input(false);
  readonly sizeClass = input('w-full');
  /** Download file name without extension (e.g. 'S2808MR0000_sketch'); the label when not given. */
  readonly downloadName = input<string | null>(null);
  readonly changed = output<string | null>();

  readonly uploading = signal(false);
  readonly downloading = signal(false);
  readonly broken = signal(false);
  readonly dragOver = signal(false);

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) void this.upload(file);
  }

  /**
   * While a form with an editable image is on screen, a file dropped next to the box must not open in the tab (losing
   * the form). isConnected: dialog content is created before the dialog opens but is not on the page until then.
   */
  guardPage(event: DragEvent): void {
    if (this.editable() && this.host.isConnected && event.dataTransfer?.types.includes('Files')) event.preventDefault();
  }

  // Drop a file on the box (editors only). Only file drags are accepted, so dragging page text does nothing.
  onDragOver(event: DragEvent): void {
    if (!this.editable() || this.uploading() || !event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    this.dragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    // Moving over the image or label inside the box also fires dragleave; only react when the pointer leaves the box.
    const box = event.currentTarget as HTMLElement;
    if (!box.contains(event.relatedTarget as Node | null)) this.dragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    if (!this.editable()) return;
    event.preventDefault();
    this.dragOver.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file && !this.uploading()) void this.upload(file);
  }

  async download(): Promise<void> {
    const url = this.url();
    if (!url) return;
    this.downloading.set(true);
    try {
      await this.svc.downloadImage(url, this.downloadName() ?? this.label());
    } catch {
      toast.error(this.transloco.translate('styles.image.downloadFailed'));
    } finally {
      this.downloading.set(false);
    }
  }

  private async upload(file: File): Promise<void> {
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
