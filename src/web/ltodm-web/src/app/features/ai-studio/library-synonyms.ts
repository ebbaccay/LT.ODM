import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { problemMessage } from '../../core/auth/auth.service';
import { StyleLibraryService, StyleLookups } from '../style-library/style-library.service';
import { LibraryAssistantService, Synonym, SynonymKind } from './library-assistant.service';

const KINDS: SynonymKind[] = ['productType', 'customer', 'season', 'businessUnit', 'gender', 'weaveType', 'words'];
const GENDERS = ['MALE', 'FEMALE', 'KIDS', 'UNISEX'];

/**
 * Library assistant > Synonyms (Admin): words people use that the library writes differently ("tees" = product type TOP,
 * "puffer" = JACKET). Each one is replaced in every question before it is read; the answer says so.
 */
@Component({
  selector: 'app-library-synonyms',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucidePlus, lucideTrash2 })],
  template: `
    <section class="flex flex-col gap-4">
      <p class="text-muted-foreground max-w-3xl text-sm">{{ 'ai.assistant.synonyms.hint' | transloco }}</p>

      <form hlmCard class="grid gap-3 p-4 sm:grid-cols-[1fr_12rem_1fr_auto] sm:items-end" (submit)="$event.preventDefault(); add()">
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.synonyms.word' | transloco }}</span>
          <input hlmInput class="h-11 lg:h-9" maxlength="60" placeholder="tees" [value]="word()" (input)="word.set($any($event.target).value)" />
        </label>
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.synonyms.kind' | transloco }}</span>
          <select class="border-input bg-background h-11 rounded-md border px-3 text-sm lg:h-9" (change)="setKind($any($event.target).value)">
            @for (k of kinds; track k) {
              <option [value]="k" [selected]="kind() === k">{{ 'ai.assistant.synonyms.kinds.' + k | transloco }}</option>
            }
          </select>
        </label>
        <label class="flex flex-col gap-1 text-xs">
          <span class="font-medium">{{ 'ai.assistant.synonyms.value' | transloco }}</span>
          @if (kind() === 'gender') {
            <select class="border-input bg-background h-11 rounded-md border px-3 text-sm lg:h-9" (change)="value.set($any($event.target).value)">
              <option value="" [selected]="!value()">—</option>
              @for (g of genders; track g) {
                <option [value]="g" [selected]="value() === g">{{ g }}</option>
              }
            </select>
          } @else {
            <input
              hlmInput
              class="h-11 lg:h-9"
              maxlength="100"
              list="syn-values"
              autocomplete="off"
              [placeholder]="(kind() === 'words' ? 'ai.assistant.synonyms.wordsPlaceholder' : 'ai.assistant.synonyms.codePlaceholder') | transloco"
              [value]="value()"
              (input)="value.set($any($event.target).value)"
            />
            <datalist id="syn-values">
              @for (o of options(); track o.code) {
                <option [value]="o.code">{{ o.code === o.name ? '' : o.name }}</option>
              }
            </datalist>
          }
        </label>
        <button hlmBtn type="submit" class="h-11 lg:h-9" [disabled]="busy() || !word().trim() || !value().trim()"><ng-icon name="lucidePlus" />{{ 'ai.assistant.synonyms.add' | transloco }}</button>
      </form>

      @if (loading()) {
        <p class="text-muted-foreground flex items-center gap-2 text-sm"><hlm-spinner class="size-4" />{{ 'ai.assistant.thinking' | transloco }}</p>
      } @else if (!rows().length) {
        <p class="text-muted-foreground text-sm">{{ 'ai.assistant.synonyms.none' | transloco }}</p>
      } @else {
        <div class="overflow-x-auto rounded-md border">
          <table class="w-full text-sm">
            <thead class="bg-muted/50 text-muted-foreground text-xs">
              <tr>
                <th class="px-3 py-2 text-left font-medium">{{ 'ai.assistant.synonyms.word' | transloco }}</th>
                <th class="px-3 py-2 text-left font-medium">{{ 'ai.assistant.synonyms.kind' | transloco }}</th>
                <th class="px-3 py-2 text-left font-medium">{{ 'ai.assistant.synonyms.value' | transloco }}</th>
                <th class="px-3 py-2 text-left font-medium">{{ 'ai.assistant.synonyms.changed' | transloco }}</th>
                <th class="w-0 px-3 py-2"><span class="sr-only">{{ 'actions.delete' | transloco }}</span></th>
              </tr>
            </thead>
            <tbody class="divide-y">
              @for (s of rows(); track s.synonymId) {
                <tr class="hover:bg-accent/30 cursor-pointer" (click)="pick(s)">
                  <td class="px-3 py-2 font-medium">{{ s.word }}</td>
                  <td class="text-muted-foreground px-3 py-2">{{ 'ai.assistant.synonyms.kinds.' + s.kind | transloco }}</td>
                  <td class="px-3 py-2 font-mono text-xs">{{ s.value }}</td>
                  <td class="text-muted-foreground px-3 py-2 text-xs">{{ s.changedBy }} · {{ s.changedUtc | date: 'mediumDate' }}</td>
                  <td class="px-3 py-1">
                    <button hlmBtn variant="ghost" size="icon" class="text-red-700 dark:text-red-400" [attr.aria-label]="'actions.delete' | transloco" [disabled]="busy()" (click)="$event.stopPropagation(); remove(s)">
                      <ng-icon name="lucideTrash2" />
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <p class="text-muted-foreground text-xs">{{ 'ai.assistant.synonyms.editHint' | transloco }}</p>
      }
    </section>
  `,
})
export class LibrarySynonyms implements OnInit {
  private readonly svc = inject(LibraryAssistantService);
  private readonly styles = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly kinds = KINDS;
  readonly genders = GENDERS;
  readonly rows = signal<Synonym[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly lookups = signal<StyleLookups | null>(null);
  readonly word = signal('');
  readonly kind = signal<SynonymKind>('productType');
  readonly value = signal('');

  /** Codes offered for the chosen kind. */
  readonly options = computed(() => {
    const l = this.lookups();
    if (!l) return [];
    switch (this.kind()) {
      case 'productType': return l.productTypes;
      case 'customer': return l.customers;
      case 'season': return l.seasons;
      case 'businessUnit': return l.businessUnits;
      case 'weaveType': return l.weaveTypes;
      default: return [];
    }
  });

  ngOnInit(): void {
    this.load();
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l), error: () => this.lookups.set(null) });
  }

  load(): void {
    this.loading.set(true);
    this.svc.synonyms().subscribe({
      next: (r) => {
        this.rows.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  setKind(kind: SynonymKind): void {
    this.kind.set(kind);
    this.value.set('');
  }

  /** Clicking a row puts it in the form, to change what it means (saving the same word replaces it). */
  pick(s: Synonym): void {
    this.word.set(s.word);
    this.kind.set(s.kind);
    this.value.set(s.value);
  }

  add(): void {
    this.busy.set(true);
    this.svc.saveSynonym(this.word().trim(), this.kind(), this.value().trim()).subscribe({
      next: () => {
        this.busy.set(false);
        toast.success(this.transloco.translate('ai.assistant.synonyms.saved', { word: this.word().trim().toLowerCase() }));
        this.word.set('');
        this.value.set('');
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  remove(s: Synonym): void {
    this.busy.set(true);
    this.svc.deleteSynonym(s.synonymId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        toast.error(problemMessage(e));
      },
    });
  }
}
