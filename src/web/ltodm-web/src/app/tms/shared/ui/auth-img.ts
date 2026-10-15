import { HttpClient } from '@angular/common/http';
import { DestroyRef, Directive, ElementRef, effect, inject, input, output } from '@angular/core';
import { environment } from '@env/environment';
import { Subscription } from 'rxjs';

/** Thumbnail widths the API offers (?w=); keep in line with ImageRules.ThumbnailWidths. */
const THUMB_WIDTHS = [160, 320, 640];
/** Picture addresses served by the API's image stores (they accept ?w=). */
const STORED_IMAGE = /^\/api\/v1\/[a-z-]+\/images\/[0-9a-f]{32}\.(jpg|png|gif|webp)$/;

/**
 * <img [appAuthSrc]="url"> for images served by the LT ODM API (/api/...), which need the sign-in token.
 * The image is fetched through HttpClient (the auth interceptor adds the token) and shown from a blob URL.
 * Any other URL (blob:, data:, https://...) is used as is.
 *
 * Stored pictures come as a thumbnail sized to the box they are drawn in (box size x screen pixel density, rounded up
 * to 160 / 320 / 640 px); a box that cannot be measured or is larger gets the original. [thumb]="false" always asks
 * for the original (e.g. pages meant for printing). Browsers keep the pictures (the API marks them immutable).
 */
@Directive({ selector: 'img[appAuthSrc]', host: { '(error)': 'loadError.emit()' } })
export class AuthImgDirective {
  readonly appAuthSrc = input<string | null | undefined>(null);
  /** false = always the original picture. */
  readonly thumb = input(true);
  /** Emits when the image could not be loaded (show a placeholder). */
  readonly loadError = output<void>();

  private readonly http = inject(HttpClient);
  private readonly img = inject<ElementRef<HTMLImageElement>>(ElementRef).nativeElement;
  private objectUrl: string | null = null;
  private sub?: Subscription;
  private frame = 0;

  constructor() {
    effect(() => {
      const url = this.appAuthSrc();
      const thumb = this.thumb();
      this.reset();
      // One frame later the box has its laid-out size.
      this.frame = requestAnimationFrame(() => this.load(url, thumb));
    });
    inject(DestroyRef).onDestroy(() => this.reset());
  }

  private load(url: string | null | undefined, thumb: boolean): void {
    if (!url) {
      this.img.removeAttribute('src');
      return;
    }
    if (!url.startsWith('/api/')) {
      this.img.src = url;
      return;
    }
    this.sub = this.http.get(`${environment.apiBaseUrl}${url}${thumb ? this.widthQuery(url) : ''}`, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        this.objectUrl = URL.createObjectURL(blob);
        this.img.src = this.objectUrl;
      },
      error: () => this.loadError.emit(),
    });
  }

  /** "?w=N" for the smallest thumbnail that fills the box sharply, or "" for the original. */
  private widthQuery(url: string): string {
    if (!STORED_IMAGE.test(url)) return '';
    const box = this.img.getBoundingClientRect();
    const needed = Math.max(box.width, box.height) * (window.devicePixelRatio || 1);
    if (needed <= 0) return '';
    const width = THUMB_WIDTHS.find((w) => w >= needed);
    return width ? `?w=${width}` : '';
  }

  private reset(): void {
    cancelAnimationFrame(this.frame);
    this.sub?.unsubscribe();
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = null;
  }
}
