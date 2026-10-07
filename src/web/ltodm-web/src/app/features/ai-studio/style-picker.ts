import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { Subject, debounceTime, distinctUntilChanged, of, switchMap, catchError } from 'rxjs';
import { StyleLibraryService, StyleListItem } from '../style-library/style-library.service';

/** Finds a style by number, model or colorway code (Styles list search) and emits the one picked. */
@Component({
  selector: 'app-style-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmInputImports, TranslocoPipe],
  providers: [provideIcons({ lucideSearch })],
  template: `
    <div class="relative">
      <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 -translate-y-1/2" />
      <input
        hlmInput
        type="search"
        class="h-11 w-full pl-9 lg:h-9"
        [id]="inputId()"
        [attr.aria-label]="label() | transloco"
        [placeholder]="'ai.picker.placeholder' | transloco"
        [value]="text()"
        (input)="onInput($event)"
        (keydown.enter)="pickFirst($event)"
        (keydown.escape)="results.set([])"
        autocomplete="off"
      />
      @if (results().length) {
        <!-- In the page flow, not absolute: cards clip overflow, which hid an absolute dropdown. -->
        <ul class="bg-popover mt-1 max-h-72 w-full overflow-auto rounded-md border p-1 shadow-sm" role="listbox">
          @for (s of results(); track s.styleId) {
            <li role="option" [attr.aria-selected]="false">
              <button type="button" class="hover:bg-accent flex w-full items-baseline justify-between gap-3 rounded px-2 py-2 text-left text-sm" (click)="pick(s)">
                <span class="min-w-0">
                  <span class="block truncate font-mono font-medium">{{ s.styleNo }}</span>
                  <span class="text-muted-foreground block truncate text-xs">{{ s.modelName || s.description || '—' }}</span>
                </span>
                <span class="text-muted-foreground shrink-0 text-xs">{{ s.customerCode }} · {{ s.seasonCode }}</span>
              </button>
            </li>
          }
        </ul>
      } @else if (searched() && text().trim().length >= 2) {
        <p class="text-muted-foreground mt-1 text-xs">{{ 'ai.picker.none' | transloco }}</p>
      }
    </div>
  `,
})
export class StylePicker {
  private readonly styles = inject(StyleLibraryService);
  private readonly input$ = new Subject<string>();

  readonly label = input('ai.picker.label');
  readonly inputId = input('style-picker');
  readonly picked = output<StyleListItem>();

  readonly text = signal('');
  readonly results = signal<StyleListItem[]>([]);
  readonly searched = signal(false);

  constructor() {
    this.input$
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((q) =>
          q.trim().length < 2
            ? of({ items: [] as StyleListItem[], total: 0 })
            : this.styles.list({ search: q.trim(), take: 8 }).pipe(catchError(() => of({ items: [] as StyleListItem[], total: 0 }))),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((page) => {
        this.results.set(page.items);
        this.searched.set(true);
      });
  }

  onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.text.set(value);
    this.searched.set(false);
    this.input$.next(value);
  }

  pickFirst(event: Event): void {
    event.preventDefault();
    const first = this.results()[0];
    if (first) this.pick(first);
  }

  pick(style: StyleListItem): void {
    this.results.set([]);
    this.text.set('');
    this.picked.emit(style);
  }
}
