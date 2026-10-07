import { HttpClient } from '@angular/common/http';
import { DestroyRef, Directive, ElementRef, effect, inject, input, output } from '@angular/core';
import { environment } from '@env/environment';
import { Subscription } from 'rxjs';

/**
 * <img [appAuthSrc]="url"> for images served by the LT ODM API (/api/...), which need the sign-in token.
 * The image is fetched through HttpClient (the auth interceptor adds the token) and shown from a blob URL.
 * Any other URL (blob:, data:, https://...) is used as is.
 */
@Directive({ selector: 'img[appAuthSrc]', host: { '(error)': 'loadError.emit()' } })
export class AuthImgDirective {
  readonly appAuthSrc = input<string | null | undefined>(null);
  /** Emits when the image could not be loaded (show a placeholder). */
  readonly loadError = output<void>();

  private readonly http = inject(HttpClient);
  private readonly img = inject<ElementRef<HTMLImageElement>>(ElementRef).nativeElement;
  private objectUrl: string | null = null;
  private sub?: Subscription;

  constructor() {
    effect(() => this.load(this.appAuthSrc()));
    inject(DestroyRef).onDestroy(() => this.reset());
  }

  private load(url: string | null | undefined): void {
    this.reset();
    if (!url) {
      this.img.removeAttribute('src');
      return;
    }
    if (!url.startsWith('/api/')) {
      this.img.src = url;
      return;
    }
    this.sub = this.http.get(`${environment.apiBaseUrl}${url}`, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        this.objectUrl = URL.createObjectURL(blob);
        this.img.src = this.objectUrl;
      },
      error: () => this.loadError.emit(),
    });
  }

  private reset(): void {
    this.sub?.unsubscribe();
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = null;
  }
}
