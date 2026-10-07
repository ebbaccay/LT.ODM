import { DestroyRef, Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { Subject, of, switchMap } from 'rxjs';

/**
 * Route titles are translation keys ("titles.styles"); the tab shows "<translated> - LT ODM" and follows language
 * changes. A title that is not a titles.* key is shown as it is.
 */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private readonly routeTitle = new Subject<string | undefined>();

  constructor() {
    super();
    this.routeTitle
      .pipe(
        switchMap((t) => (t?.startsWith('titles.') ? this.transloco.selectTranslate(t) : of(t))),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((t) => this.title.setTitle(t ? `${t} - LT ODM` : 'LT ODM'));
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.routeTitle.next(this.buildTitle(snapshot));
  }
}
