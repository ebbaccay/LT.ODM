import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideChevronsUpDown, lucideSearch } from '@ng-icons/lucide';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';

/** The fields of a saved concept (web_rd_get_concept_studio_results) the picker shows. */
export interface PickerConcept {
  recid: number;
  concept_name: string;
  customer: string;
  season: string;
}

let nextId = 0;

/**
 * Searchable concept selector used by the Manage Offerings screens (TMS had a copy in each screen).
 *
 *   <app-concept-picker [concepts]="concepts()" [loaded]="loaded()" [selected]="recid()" (selectedChange)="select($event)"
 *                       label="Concept" placeholder="Search concepts…" emptyText="No saved concepts." noMatchText="No match" />
 */
@Component({
  selector: 'app-concept-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmInputImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideCheck, lucideChevronsUpDown, lucideSearch })],
  template: `
    <div class="flex flex-col gap-1.5">
      <span class="text-sm font-medium" [id]="labelId">{{ label() }}</span>
      @if (!loaded()) {
        <div hlmSkeleton class="h-11 w-full lg:h-9"></div>
      } @else if (!concepts().length) {
        <p class="text-muted-foreground text-sm">{{ emptyText() }}</p>
      } @else {
        <div class="relative">
          <button
            type="button"
            class="border-input bg-background hover:bg-accent/40 flex h-11 w-full items-center justify-between gap-2 rounded-md border px-3 text-left text-sm shadow-xs lg:h-9"
            aria-haspopup="listbox"
            [attr.aria-labelledby]="labelId"
            [attr.aria-expanded]="open()"
            (click)="toggle()"
          >
            <span class="truncate font-medium">{{ current()?.concept_name || placeholder() }}</span>
            <ng-icon name="lucideChevronsUpDown" class="text-muted-foreground shrink-0" />
          </button>
          @if (open()) {
            <div class="fixed inset-0 z-40" (click)="open.set(false)" aria-hidden="true"></div>
            <div
              class="bg-popover text-popover-foreground absolute inset-x-0 top-full z-50 mt-1 flex max-h-80 flex-col overflow-hidden rounded-md border shadow-lg"
              (keydown.escape)="open.set(false)"
            >
              <div class="relative border-b p-2">
                <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-5 -translate-y-1/2" />
                <input
                  hlmInput
                  type="search"
                  class="h-11 w-full pl-9 lg:h-9"
                  autofocus
                  [attr.aria-label]="placeholder()"
                  [placeholder]="placeholder()"
                  [value]="query()"
                  (input)="query.set($any($event.target).value)"
                />
              </div>
              <ul class="overflow-y-auto p-1" role="listbox" [attr.aria-labelledby]="labelId">
                @for (c of filtered(); track c.recid) {
                  <li role="option" [attr.aria-selected]="c.recid === selected()">
                    <button type="button" class="hover:bg-accent flex min-h-11 w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left lg:min-h-9" (click)="pick(c.recid)">
                      <ng-icon name="lucideCheck" class="shrink-0" [class.invisible]="c.recid !== selected()" />
                      <span class="min-w-0">
                        <span class="block truncate text-sm font-medium">{{ c.concept_name }}</span>
                        <span class="text-muted-foreground block truncate text-xs">{{ c.customer }} · {{ c.season }}</span>
                      </span>
                    </button>
                  </li>
                } @empty {
                  <li class="text-muted-foreground px-3 py-4 text-center text-sm">{{ noMatchText() }}</li>
                }
              </ul>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class ConceptPicker {
  readonly concepts = input<PickerConcept[]>([]);
  readonly loaded = input(false);
  readonly selected = input<number | null>(null);
  readonly label = input('');
  readonly placeholder = input('');
  readonly emptyText = input('');
  readonly noMatchText = input('');
  readonly selectedChange = output<number>();

  protected readonly labelId = `concept-picker-${nextId++}`;
  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly current = computed(() => this.concepts().find((c) => c.recid === this.selected()) ?? null);
  protected readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    return q
      ? this.concepts().filter((c) => [c.concept_name, c.customer, c.season].some((v) => (v ?? '').toLowerCase().includes(q)))
      : this.concepts();
  });

  protected toggle(): void {
    this.query.set('');
    this.open.update((o) => !o);
  }

  protected pick(recid: number): void {
    this.open.set(false);
    this.selectedChange.emit(recid);
  }
}
