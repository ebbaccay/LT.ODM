import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { Subject, debounceTime, switchMap } from 'rxjs';
import { HistoryRelation, StyleHeader, StyleLibraryService, StyleListItem, styleError } from './style-library.service';

/** Record by hand which style this one was reused from (kept when styles are imported again). */
@Component({
  selector: 'app-history-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, NgIcon, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmNativeSelectImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCheck, lucideSearch })],
  template: `
    <app-modal [open]="open()" [heading]="'styles.history.title' | transloco" (closed)="closed.emit()">
      <div class="flex flex-col gap-4">
        <p class="text-muted-foreground text-sm">{{ 'styles.history.hint' | transloco: { customer: style()?.customerCode, styleNo: style()?.styleNo } }}</p>
        <div class="relative">
          <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 -translate-y-1/2" />
          <input hlmInput type="search" class="h-11 w-full pl-9 lg:h-9" [attr.aria-label]="'styles.history.searchLabel' | transloco" [placeholder]="'styles.history.searchPlaceholder' | transloco" (input)="search$.next($any($event.target).value)" />
        </div>
        <ul class="max-h-64 divide-y overflow-y-auto rounded-md border" role="listbox" [attr.aria-label]="'styles.history.listLabel' | transloco">
          @for (s of results(); track s.styleId) {
            <li>
              <button type="button" role="option" class="hover:bg-accent/50 flex w-full items-center gap-3 px-3 py-2 text-left" [attr.aria-selected]="picked()?.styleId === s.styleId" (click)="picked.set(s)">
                <span class="min-w-0 flex-1">
                  <span class="block truncate font-mono text-sm">{{ s.styleNo }}</span>
                  <span class="text-muted-foreground block truncate text-xs">{{ s.seasonCode }} · {{ s.modelName }}</span>
                </span>
                @if (picked()?.styleId === s.styleId) { <ng-icon name="lucideCheck" class="text-primary" /> }
              </button>
            </li>
          } @empty {
            <li class="text-muted-foreground px-3 py-6 text-center text-sm">{{ (searching() ? 'styles.history.searching' : 'styles.history.typeToFind') | transloco }}</li>
          }
        </ul>
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="hd-rel">{{ 'styles.history.relation' | transloco }}</label>
            <hlm-native-select selectId="hd-rel" selectClass="h-11 w-full lg:h-9" [value]="relation()" (valueChange)="relation.set($any($event) ?? 'CarryOver')">
              <option hlmNativeSelectOption value="CarryOver">{{ 'styles.history.carryOver' | transloco }}</option>
              <option hlmNativeSelectOption value="Variant">{{ 'styles.history.variant' | transloco }}</option>
            </hlm-native-select>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="hd-note">{{ 'styles.history.note' | transloco }}</label>
            <input hlmInput id="hd-note" class="h-11 lg:h-9" maxlength="400" [value]="note()" (input)="note.set($any($event.target).value)" />
          </div>
        </div>
      </div>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="!picked() || saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ 'styles.history.save' | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class HistoryDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly style = input<StyleHeader | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly search$ = new Subject<string>();
  readonly results = signal<StyleListItem[]>([]);
  readonly searching = signal(false);
  readonly picked = signal<StyleListItem | null>(null);
  readonly relation = signal<HistoryRelation>('CarryOver');
  readonly note = signal('');
  readonly saving = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.results.set([]);
      this.picked.set(null);
      this.relation.set('CarryOver');
      this.note.set('');
    });
    this.search$
      .pipe(
        debounceTime(250),
        switchMap((q) => {
          this.searching.set(true);
          return this.svc.list({ search: q.trim(), customer: this.style()?.customerCode, take: 20 });
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe({
        next: (page) => {
          this.searching.set(false);
          this.results.set(page.items.filter((s) => s.styleId !== this.style()?.styleId));
        },
        error: (e) => {
          this.searching.set(false);
          toast.error(styleError(e).message);
        },
      });
  }

  save(): void {
    const s = this.style();
    const source = this.picked();
    if (!s || !source || this.saving()) return;
    this.saving.set(true);
    this.svc.setHistory(s.styleId, source.styleId, this.relation(), this.note().trim() || null).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success(this.transloco.translate('styles.history.linked', { styleNo: source.styleNo }));
        this.saved.emit();
      },
      error: (err) => {
        this.saving.set(false);
        toast.error(styleError(err).message);
      },
    });
  }
}
