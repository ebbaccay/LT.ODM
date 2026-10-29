import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { Modal } from '@tms/shared/ui/modal';
import { StyleLookups, styleError } from '../style-library/style-library.service';
import { ClientProfilesService, ClientSummary, PitchDate } from './client-profiles.service';

interface ProfileForm {
  priceMin: string;
  priceMax: string;
  currency: string;
  minRecycledPct: string;
  restrictedTerms: string;
  accountManager: string;
  notes: string;
  mills: { code: string; name: string }[];
  pitchDates: PitchDate[];
}

/**
 * A client's profile (roadmap 5.13): FOB price band, minimum recycled share, restricted words, preferred mills, pitch dates and
 * the LT account manager (who gets the pitch reminders). Packages and What to offer are checked against it.
 */
@Component({
  selector: 'app-client-profile-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, Modal, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe],
  providers: [provideIcons({ lucidePlus, lucideX })],
  template: `
    <app-modal [open]="open()" size="lg" [heading]="'clientProfiles.dialogTitle' | transloco: { name: client()?.name ?? '' }" (closed)="closed.emit()">
      @if (loading()) {
        <div class="flex justify-center py-10"><hlm-spinner /></div>
      } @else {
        @let f = form();
        @let e = errors();
        <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
          <div class="grid gap-4 sm:grid-cols-3">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="cp-min">{{ 'clientProfiles.priceMin' | transloco }}</label>
              <input hlmInput id="cp-min" type="number" min="0" step="0.01" inputmode="decimal" class="h-11 lg:h-9" [value]="f.priceMin" (input)="set('priceMin', $any($event.target).value)" />
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="cp-max">{{ 'clientProfiles.priceMax' | transloco }}</label>
              <input hlmInput id="cp-max" type="number" min="0" step="0.01" inputmode="decimal" class="h-11 lg:h-9" [value]="f.priceMax" (input)="set('priceMax', $any($event.target).value)"
                     [attr.aria-invalid]="!!e['priceMax']" />
              @if (e['priceMax']) { <p class="text-destructive text-xs">{{ e['priceMax'] }}</p> }
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="cp-cur">{{ 'clientProfiles.currency' | transloco }}</label>
              <input hlmInput id="cp-cur" class="h-11 uppercase lg:h-9" maxlength="3" [value]="f.currency" (input)="set('currency', $any($event.target).value)"
                     [attr.aria-invalid]="!!e['currency']" />
              @if (e['currency']) { <p class="text-destructive text-xs">{{ e['currency'] }}</p> }
            </div>
          </div>
          <p class="text-muted-foreground -mt-2 text-xs">{{ 'clientProfiles.priceHint' | transloco }}</p>

          <div class="grid gap-4 sm:grid-cols-[10rem_minmax(0,1fr)]">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="cp-rec">{{ 'clientProfiles.minRecycled' | transloco }}</label>
              <input hlmInput id="cp-rec" type="number" min="0" max="100" step="1" inputmode="numeric" class="h-11 lg:h-9" [value]="f.minRecycledPct"
                     (input)="set('minRecycledPct', $any($event.target).value)" [attr.aria-invalid]="!!e['minRecycledPct']" />
              @if (e['minRecycledPct']) { <p class="text-destructive text-xs">{{ e['minRecycledPct'] }}</p> }
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="cp-terms">{{ 'clientProfiles.restricted' | transloco }}</label>
              <input hlmInput id="cp-terms" class="h-11 lg:h-9" maxlength="1000" [placeholder]="'clientProfiles.restrictedPlaceholder' | transloco"
                     [value]="f.restrictedTerms" (input)="set('restrictedTerms', $any($event.target).value)" />
              <p class="text-muted-foreground text-xs">{{ 'clientProfiles.restrictedHint' | transloco }}</p>
            </div>
          </div>

          <fieldset class="flex flex-col gap-1.5">
            <legend class="mb-1.5 text-sm font-medium">{{ 'clientProfiles.mills' | transloco }}</legend>
            <div class="flex flex-wrap gap-1.5">
              @for (m of f.mills; track m.code) {
                <span class="bg-muted flex items-center gap-1 rounded-full py-0.5 pr-1 pl-2.5 text-xs">
                  <span class="font-mono">{{ m.code }}</span>@if (m.name && m.name !== m.code) { <span class="text-muted-foreground">{{ m.name }}</span> }
                  <button type="button" class="hover:bg-background flex size-6 items-center justify-center rounded-full" [attr.aria-label]="'actions.remove' | transloco"
                          (click)="removeMill(m.code)"><ng-icon name="lucideX" /></button>
                </span>
              }
            </div>
            <div class="flex gap-2">
              <input hlmInput list="cp-suppliers" class="h-11 flex-1 lg:h-9" maxlength="32" [placeholder]="'clientProfiles.millPlaceholder' | transloco"
                     [value]="millInput()" (input)="millInput.set($any($event.target).value)" (keydown.enter)="$event.preventDefault(); addMill()" />
              <datalist id="cp-suppliers">
                @for (s of lookups()?.suppliers ?? []; track s.code) { <option [value]="s.code">{{ s.name }}</option> }
              </datalist>
              <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="addMill()"><ng-icon name="lucidePlus" />{{ 'clientProfiles.addMill' | transloco }}</button>
            </div>
            @if (e['mills']) { <p class="text-destructive text-xs">{{ e['mills'] }}</p> }
            @else { <p class="text-muted-foreground text-xs">{{ 'clientProfiles.millsHint' | transloco }}</p> }
          </fieldset>

          <fieldset class="flex flex-col gap-2">
            <legend class="mb-1.5 text-sm font-medium">{{ 'clientProfiles.pitchDates' | transloco }}</legend>
            <datalist id="cp-seasons">
              @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code">{{ s.code === s.name ? '' : s.name }}</option> }
            </datalist>
            @for (d of f.pitchDates; track $index; let n = $index) {
              <div class="grid gap-2 sm:grid-cols-[9rem_10rem_minmax(0,1fr)_auto] sm:items-center">
                <input hlmInput list="cp-seasons" class="h-11 lg:h-9" maxlength="16" placeholder="2028-SS" [attr.aria-label]="'clientProfiles.season' | transloco"
                       [value]="d.seasonCode ?? ''" (input)="setDate(n, 'seasonCode', $any($event.target).value)" />
                <input hlmInput type="date" class="h-11 lg:h-9" [attr.aria-label]="'clientProfiles.date' | transloco" [value]="d.pitchDate"
                       (input)="setDate(n, 'pitchDate', $any($event.target).value)" />
                <input hlmInput class="h-11 lg:h-9" maxlength="200" [placeholder]="'clientProfiles.notePlaceholder' | transloco" [value]="d.note ?? ''"
                       (input)="setDate(n, 'note', $any($event.target).value)" />
                <button hlmBtn variant="ghost" size="icon" type="button" class="size-11 lg:size-9" [attr.aria-label]="'actions.remove' | transloco" (click)="removeDate(n)">
                  <ng-icon name="lucideX" />
                </button>
              </div>
            }
            <button hlmBtn variant="outline" type="button" class="h-11 w-fit lg:h-9" (click)="addDate()"><ng-icon name="lucidePlus" />{{ 'clientProfiles.addDate' | transloco }}</button>
            @if (e['pitchDates']) { <p class="text-destructive text-xs">{{ e['pitchDates'] }}</p> }
          </fieldset>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cp-am">{{ 'clientProfiles.accountManager' | transloco }}</label>
            <input hlmInput id="cp-am" class="h-11 lg:h-9 sm:max-w-xs" maxlength="64" autocomplete="off" [value]="f.accountManager" (input)="set('accountManager', $any($event.target).value)" />
            <p class="text-muted-foreground text-xs">{{ 'clientProfiles.accountManagerHint' | transloco }}</p>
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="cp-notes">{{ 'clientProfiles.notes' | transloco }}</label>
            <textarea hlmTextarea id="cp-notes" rows="3" maxlength="2000" [value]="f.notes" (input)="set('notes', $any($event.target).value)"></textarea>
          </div>
          <button type="submit" class="hidden"></button>
        </form>
      }
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        @if (canEdit()) {
          <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving() || loading()" (click)="save()">
            @if (saving()) { <hlm-spinner /> }
            {{ 'actions.save' | transloco }}
          </button>
        }
      </ng-container>
    </app-modal>
  `,
})
export class ClientProfileDialog {
  private readonly svc = inject(ClientProfilesService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly client = input<ClientSummary | null>(null);
  readonly lookups = input<StyleLookups | null>(null);
  readonly canEdit = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly errors = signal<Record<string, string>>({});
  readonly millInput = signal('');
  readonly form = signal<ProfileForm>(this.empty());
  private rowVer: string | null = null;

  constructor() {
    effect(() => {
      const c = this.client();
      if (!this.open() || !c) return;
      this.loading.set(true);
      this.errors.set({});
      this.millInput.set('');
      this.svc.get(c.clientKey).subscribe({
        next: (p) => {
          this.rowVer = p.rowVer;
          this.form.set({
            priceMin: p.priceMin?.toString() ?? '', priceMax: p.priceMax?.toString() ?? '', currency: p.currency, minRecycledPct: p.minRecycledPct?.toString() ?? '',
            restrictedTerms: p.restrictedTerms ?? '', accountManager: p.accountManager ?? '', notes: p.notes ?? '', mills: p.mills, pitchDates: p.pitchDates,
          });
          this.loading.set(false);
        },
        error: (e) => {
          this.loading.set(false);
          toast.error(styleError(e).message);
        },
      });
    });
  }

  private empty(): ProfileForm {
    return { priceMin: '', priceMax: '', currency: 'USD', minRecycledPct: '', restrictedTerms: '', accountManager: '', notes: '', mills: [], pitchDates: [] };
  }

  set(key: Exclude<keyof ProfileForm, 'mills' | 'pitchDates'>, value: string): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  addMill(): void {
    const code = this.millInput().trim();
    if (!code) return;
    const known = (this.lookups()?.suppliers ?? []).find((s) => s.code.toLowerCase() === code.toLowerCase());
    if (!known) {
      this.errors.update((e) => ({ ...e, mills: this.transloco.translate('clientProfiles.unknownMill') }));
      return;
    }
    this.errors.update(({ mills: _, ...rest }) => rest);
    this.form.update((f) => (f.mills.some((m) => m.code === known.code) ? f : { ...f, mills: [...f.mills, { code: known.code, name: known.name }] }));
    this.millInput.set('');
  }

  removeMill(code: string): void {
    this.form.update((f) => ({ ...f, mills: f.mills.filter((m) => m.code !== code) }));
  }

  addDate(): void {
    this.form.update((f) => ({ ...f, pitchDates: [...f.pitchDates, { seasonCode: null, pitchDate: '', note: null }] }));
  }

  setDate(n: number, key: keyof PitchDate, value: string): void {
    this.form.update((f) => ({ ...f, pitchDates: f.pitchDates.map((d, i) => (i === n ? { ...d, [key]: value } : d)) }));
  }

  removeDate(n: number): void {
    this.form.update((f) => ({ ...f, pitchDates: f.pitchDates.filter((_, i) => i !== n) }));
  }

  save(): void {
    const c = this.client();
    if (!c || this.saving()) return;
    const f = this.form();
    const num = (v: string) => (v.trim() === '' ? null : Number(v));
    const dates = f.pitchDates.filter((d) => d.pitchDate || d.seasonCode || d.note);
    if (dates.some((d) => !d.pitchDate)) {
      this.errors.set({ pitchDates: this.transloco.translate('clientProfiles.dateRequired') });
      return;
    }
    this.saving.set(true);
    this.svc
      .save(c.clientKey, {
        rowVer: this.rowVer, priceMin: num(f.priceMin), priceMax: num(f.priceMax), currency: f.currency.trim() || null, minRecycledPct: num(f.minRecycledPct),
        restrictedTerms: f.restrictedTerms.trim() || null, accountManager: f.accountManager.trim() || null, notes: f.notes.trim() || null,
        mills: f.mills.map((m) => m.code), pitchDates: dates.map((d) => ({ seasonCode: d.seasonCode?.trim().toUpperCase() || null, pitchDate: d.pitchDate, note: d.note?.trim() || null })),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.saved.emit();
        },
        error: (err) => {
          this.saving.set(false);
          const { message, fields } = styleError(err);
          this.errors.set(fields);
          toast.error(message);
        },
      });
  }
}
