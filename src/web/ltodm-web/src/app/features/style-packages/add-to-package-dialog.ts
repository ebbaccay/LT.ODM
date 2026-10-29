import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideCircleCheck, lucideInfo, lucidePackage, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Modal } from '@tms/shared/ui/modal';
import { toneBadge } from '@tms/shared/ui/tones';
import { Subject, debounceTime } from 'rxjs';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { PackageAddResult, PackageListItem, PackageSource, PackagesService, STATUS_TONE } from './packages.service';

/**
 * Adds library styles to a style package: an existing one (latest first, searchable) or a new one started here. Only styles
 * of the package's brand go in; the result lists the others (another brand's style is offered as a copy, roadmap 5.12) and
 * the ones already in the package.
 */
@Component({
  selector: 'app-add-to-package-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, Modal, HlmButtonImports, HlmInputImports, HlmLabelImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCircleAlert, lucideCircleCheck, lucideInfo, lucidePackage, lucideSearch })],
  template: `
    <app-modal [open]="open()" size="lg" [heading]="'packages.add.title' | transloco: { count: styleIds().length }" (closed)="closed.emit()">
      @if (results(); as r) {
        <datalist id="ap-seasons">
          @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code">{{ s.code === s.name ? '' : s.name }}</option> }
        </datalist>
        <!-- What happened to each style -->
        <div class="flex flex-col gap-3 text-sm">
          @if (added().length) {
            <p class="flex items-start gap-2"><ng-icon name="lucideCircleCheck" class="mt-0.5 shrink-0 text-emerald-600 dark:text-emerald-400" />
              {{ 'packages.add.added' | transloco: { count: added().length, name: targetName() } }}</p>
          }
          @if (alreadyIn().length) {
            <p class="text-muted-foreground flex items-start gap-2"><ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />
              {{ 'packages.add.alreadyIn' | transloco: { count: alreadyIn().length, styles: styleList(alreadyIn()) } }}</p>
          }
          @if (otherBrand().length) {
            <div class="flex items-start gap-2 text-amber-700 dark:text-amber-400">
              <ng-icon name="lucideCircleAlert" class="mt-0.5 shrink-0" />
              <div>
                <p>{{ 'packages.add.otherBrand' | transloco: { count: otherBrand().length, styles: styleList(otherBrand()) } }}</p>
                <p class="text-muted-foreground mt-1 text-xs">{{ 'packages.add.otherBrandHint' | transloco }}</p>
              </div>
            </div>
            <!-- Offer each as a new style of the package's customer: season and its own style number -->
            <ul class="flex flex-col gap-2" role="list">
              @for (r of otherBrand(); track r.styleId) {
                @let c = copies()[r.styleId];
                <li class="flex flex-col gap-2 rounded-md border p-3">
                  <span class="text-sm"><span class="font-mono font-medium">{{ r.styleNo }}</span>
                    <span class="text-muted-foreground"> · {{ r.customerCode }} {{ r.seasonCode }}</span></span>
                  @if (c?.done) {
                    <span class="flex items-center gap-1.5 text-sm text-emerald-700 dark:text-emerald-400">
                      <ng-icon name="lucideCircleCheck" />{{ 'packages.copy.done' | transloco: { styleNo: c!.styleNo } }}
                    </span>
                  } @else {
                    <div class="grid gap-2 sm:grid-cols-[8rem_minmax(0,1fr)_auto] sm:items-start">
                      <div class="flex flex-col gap-1">
                        <label hlmLabel class="text-xs" [for]="'cp-season-' + r.styleId">{{ 'packages.copy.season' | transloco }}</label>
                        <input hlmInput [id]="'cp-season-' + r.styleId" list="ap-seasons" class="h-11 lg:h-9" maxlength="16" autocomplete="off" placeholder="2028-SS"
                               [value]="c?.seasonCode ?? ''" (input)="setCopy(r.styleId, 'seasonCode', $any($event.target).value)" />
                      </div>
                      <div class="flex flex-col gap-1">
                        <label hlmLabel class="text-xs" [for]="'cp-no-' + r.styleId">{{ 'packages.copy.styleNo' | transloco }}</label>
                        <input hlmInput [id]="'cp-no-' + r.styleId" class="h-11 font-mono lg:h-9" maxlength="100" autocomplete="off"
                               [value]="c?.styleNo ?? ''" (input)="setCopy(r.styleId, 'styleNo', $any($event.target).value)" />
                      </div>
                      <button hlmBtn variant="outline" type="button" class="h-11 sm:mt-5 lg:h-9" [disabled]="c?.saving" (click)="copy(r)">
                        @if (c?.saving) { <hlm-spinner class="size-4" /> }
                        {{ 'packages.copy.submit' | transloco }}
                      </button>
                    </div>
                    @if (c?.error) { <p class="text-destructive text-xs">{{ c!.error }}</p> }
                  }
                </li>
              }
            </ul>
            <p class="text-muted-foreground text-xs">{{ 'packages.copy.what' | transloco }}</p>
          }
          @if (notFound().length) {
            <p class="text-destructive flex items-start gap-2"><ng-icon name="lucideCircleAlert" class="mt-0.5 shrink-0" />
              {{ 'packages.add.notFound' | transloco: { count: notFound().length } }}</p>
          }
        </div>
      } @else {
        <div class="flex flex-col gap-4">
          <div class="bg-muted flex w-full rounded-lg p-1 sm:w-fit" role="tablist">
            @for (m of [{ id: 'existing', label: 'packages.add.existing' }, { id: 'new', label: 'packages.add.new' }]; track m.id) {
              <button type="button" role="tab" class="h-11 flex-1 rounded-md px-3 text-sm font-medium transition-colors sm:flex-initial lg:h-8"
                      [class]="mode() === m.id ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'"
                      [attr.aria-selected]="mode() === m.id" (click)="mode.set($any(m.id))">{{ m.label | transloco }}</button>
            }
          </div>

          @if (mode() === 'existing') {
            <div class="relative">
              <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 -translate-y-1/2" />
              <input hlmInput type="search" class="h-11 w-full pl-9 lg:h-9" [attr.aria-label]="'packages.add.search' | transloco"
                     [placeholder]="'packages.add.search' | transloco" (input)="searchInput.next($any($event.target).value)" />
            </div>
            @if (loading() && !packages().length) {
              <div class="flex justify-center py-8"><hlm-spinner /></div>
            } @else if (!packages().length) {
              <p class="text-muted-foreground py-6 text-center text-sm">{{ 'packages.add.noPackages' | transloco }}</p>
            } @else {
              <ul class="flex max-h-80 flex-col gap-1 overflow-y-auto" role="radiogroup" [attr.aria-label]="'packages.add.existing' | transloco">
                @for (p of packages(); track p.packageId) {
                  <li>
                    <button type="button" role="radio" [attr.aria-checked]="selected() === p.packageId"
                            class="flex min-h-11 w-full items-center gap-3 rounded-md border px-3 py-2 text-left transition-colors"
                            [class]="selected() === p.packageId ? 'border-primary bg-primary/5' : 'hover:bg-accent/40 border-transparent'"
                            (click)="selected.set(p.packageId)">
                      <ng-icon name="lucidePackage" class="text-muted-foreground shrink-0" />
                      <span class="min-w-0 flex-1">
                        <span class="block truncate text-sm font-medium">{{ p.name }}</span>
                        <span class="text-muted-foreground block truncate text-xs">
                          {{ p.customerCode }}{{ p.seasonCode ? ' · ' + p.seasonCode : '' }} · {{ 'packages.styleCount' | transloco: { count: p.items } }}
                        </span>
                      </span>
                      <span class="shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge(tone(p))">{{ 'packages.status.' + p.status | transloco }}</span>
                    </button>
                  </li>
                }
              </ul>
            }
          } @else {
            @let e = errors();
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="ap-name">{{ 'packages.form.name' | transloco }}</label>
              <input hlmInput id="ap-name" class="h-11 lg:h-9" maxlength="150" autocomplete="off" [placeholder]="'packages.form.namePlaceholder' | transloco"
                     [value]="newName()" (input)="newName.set($any($event.target).value)" [attr.aria-invalid]="!!e['name']" />
              @if (e['name']) { <p class="text-destructive text-xs">{{ e['name'] }}</p> }
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="ap-customer">{{ 'packages.form.customer' | transloco }}</label>
                <input hlmInput id="ap-customer" list="ap-customers" class="h-11 lg:h-9" maxlength="32" autocomplete="off"
                       [value]="newCustomer()" (input)="newCustomer.set($any($event.target).value)" [attr.aria-invalid]="!!e['customerCode']" />
                <datalist id="ap-customers">
                  @for (c of lookups()?.customers ?? []; track c.code) { <option [value]="c.code">{{ c.code === c.name ? '' : c.name }}</option> }
                </datalist>
                @if (e['customerCode']) { <p class="text-destructive text-xs">{{ e['customerCode'] }}</p> }
              </div>
              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="ap-season">{{ 'packages.form.season' | transloco }}</label>
                <input hlmInput id="ap-season" list="ap-seasons" class="h-11 lg:h-9" maxlength="16" autocomplete="off" placeholder="2028-SS"
                       [value]="newSeason()" (input)="newSeason.set($any($event.target).value)" />
                <datalist id="ap-seasons">
                  @for (s of lookups()?.seasons ?? []; track s.code) { <option [value]="s.code">{{ s.code === s.name ? '' : s.name }}</option> }
                </datalist>
              </div>
            </div>
          }
          <p class="text-muted-foreground flex items-start gap-1.5 text-xs"><ng-icon name="lucideInfo" class="mt-0.5 shrink-0" />{{ 'packages.add.brandRule' | transloco }}</p>
        </div>
      }
      <ng-container footer>
        @if (results()) {
          @if (targetId(); as id) {
            <a hlmBtn variant="outline" class="h-11 lg:h-9" [routerLink]="['/packages', id]" (click)="closed.emit()">{{ 'packages.add.openPackage' | transloco }}</a>
          }
          <button hlmBtn type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.close' | transloco }}</button>
        } @else {
          <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
          <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving() || (mode() === 'existing' && !selected())" (click)="add()">
            @if (saving()) { <hlm-spinner /> }
            {{ 'packages.add.submit' | transloco: { count: styleIds().length } }}
          </button>
        }
      </ng-container>
    </app-modal>
  `,
})
export class AddToPackageDialog {
  private readonly svc = inject(PackagesService);
  private readonly styles = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly styleIds = input<number[]>([]);
  readonly addedFrom = input.required<PackageSource>();
  /** For a new package: the customer and season of the styles (when they share one). */
  readonly customerCode = input<string | null>(null);
  readonly seasonCode = input<string | null>(null);
  /** A package to choose for the user (e.g. the one the page was opened for). */
  readonly packageId = input<number | null>(null);
  /** The concept the styles are offered for (the package items keep it; roadmap 5.7). */
  readonly conceptId = input<number | null>(null);
  readonly closed = output<void>();
  /** How many styles were added (the page may refresh its package list). */
  readonly stylesAdded = output<number>();

  readonly toneBadge = toneBadge;
  readonly mode = signal<'existing' | 'new'>('existing');
  readonly lookups = signal<StyleLookups | null>(null);
  readonly packages = signal<PackageListItem[]>([]);
  readonly loading = signal(false);
  readonly selected = signal<number | null>(null);
  readonly newName = signal('');
  readonly newCustomer = signal('');
  readonly newSeason = signal('');
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);
  readonly results = signal<PackageAddResult[] | null>(null);
  readonly targetId = signal<number | null>(null);
  readonly targetName = signal('');
  readonly searchInput = new Subject<string>();
  /** Copies of another brand's styles being made, by source style id. */
  readonly copies = signal<Record<number, { seasonCode: string; styleNo: string; saving?: boolean; done?: boolean; error?: string }>>({});
  private targetSeason: string | null = null;

  readonly added = computed(() => (this.results() ?? []).filter((r) => r.result === 'Added'));
  readonly alreadyIn = computed(() => (this.results() ?? []).filter((r) => r.result === 'AlreadyIn'));
  readonly otherBrand = computed(() => (this.results() ?? []).filter((r) => r.result === 'OtherBrand'));
  readonly notFound = computed(() => (this.results() ?? []).filter((r) => r.result === 'NotFound'));

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.results.set(null);
      this.copies.set({});
      this.targetId.set(null);
      this.selected.set(null);
      this.errors.set({});
      this.newName.set('');
      this.newCustomer.set(this.customerCode() ?? '');
      this.newSeason.set(this.seasonCode() ?? '');
      this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l) });
      this.loadPackages('');
    });
    this.searchInput.pipe(debounceTime(300), takeUntilDestroyed()).subscribe((q) => this.loadPackages(q));
  }

  tone(p: PackageListItem) {
    return STATUS_TONE[p.status] ?? 'neutral';
  }

  /** Up to 5 style numbers, then "+N". */
  styleList(rows: PackageAddResult[]): string {
    const names = rows.map((r) => r.styleNo ?? String(r.styleId));
    return names.length > 5 ? `${names.slice(0, 5).join(', ')} +${names.length - 5}` : names.join(', ');
  }

  setCopy(styleId: number, key: 'seasonCode' | 'styleNo', value: string): void {
    this.copies.update((c) => ({ ...c, [styleId]: { ...c[styleId], [key]: value, error: undefined } }));
  }

  /** Copies another brand's style as a new style of the package's customer and adds it to the package. */
  copy(r: PackageAddResult): void {
    const packageId = this.targetId();
    const c = this.copies()[r.styleId];
    if (!packageId || !c || c.saving) return;
    const seasonCode = c.seasonCode.trim().toUpperCase();
    const styleNo = c.styleNo.trim();
    if (!seasonCode || !styleNo) {
      this.copies.update((x) => ({ ...x, [r.styleId]: { ...c, error: this.transloco.translate('packages.copy.required') } }));
      return;
    }
    this.copies.update((x) => ({ ...x, [r.styleId]: { ...c, saving: true, error: undefined } }));
    this.svc.addCopy(packageId, r.styleId, seasonCode, styleNo, this.conceptId()).subscribe({
      next: () => {
        this.copies.update((x) => ({ ...x, [r.styleId]: { seasonCode, styleNo, done: true } }));
        this.styles.refreshLookups();
        this.stylesAdded.emit(1);
      },
      error: (err) => this.copies.update((x) => ({ ...x, [r.styleId]: { ...c, saving: false, error: styleError(err).message } })),
    });
  }

  private loadPackages(search: string): void {
    this.loading.set(true);
    // Closed packages are history: not offered here (open the package to add to it).
    this.svc.list({ search: search.trim() || undefined, take: 30 }).subscribe({
      next: (page) => {
        const open = page.items.filter((p) => p.status !== 'Closed');
        this.packages.set(open);
        // The only package of the styles' customer is chosen for the user.
        const ofCustomer = open.filter((p) => p.customerCode === this.customerCode());
        const wanted = open.find((p) => p.packageId === this.packageId());
        if (!this.selected() && wanted) this.selected.set(wanted.packageId);
        else if (!this.selected() && ofCustomer.length === 1) this.selected.set(ofCustomer[0].packageId);
        this.loading.set(false);
        if (!open.length && !search.trim()) this.mode.set('new');
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  add(): void {
    if (this.saving()) return;
    if (this.mode() === 'existing') {
      const p = this.packages().find((x) => x.packageId === this.selected());
      if (p) {
        this.targetSeason = p.seasonCode;
        this.addTo(p.packageId, p.name);
      }
      return;
    }
    const errors: Record<string, string> = {};
    if (!this.newName().trim()) errors['name'] = this.transloco.translate('packages.form.nameRequired');
    if (!this.newCustomer().trim()) errors['customerCode'] = this.transloco.translate('packages.form.customerRequired');
    this.errors.set(errors);
    if (Object.keys(errors).length) return;
    this.saving.set(true);
    const name = this.newName().trim();
    this.targetSeason = this.newSeason().trim().toUpperCase() || null;
    this.svc.create({ name, customerCode: this.newCustomer().trim().toUpperCase(), seasonCode: this.newSeason().trim().toUpperCase() || null }).subscribe({
      next: (id) => {
        this.styles.refreshLookups();
        this.addTo(id, name);
      },
      error: (err) => {
        this.saving.set(false);
        const { message, fields } = styleError(err);
        this.errors.set(fields);
        toast.error(message);
      },
    });
  }

  private addTo(packageId: number, name: string): void {
    this.saving.set(true);
    this.svc.addStyles(packageId, this.styleIds(), this.addedFrom(), this.conceptId()).subscribe({
      next: (results) => {
        this.saving.set(false);
        this.targetId.set(packageId);
        this.targetName.set(name);
        this.results.set(results);
        // Copies start in the package's season; the style number is the user's to give.
        this.copies.set(Object.fromEntries(results.filter((r) => r.result === 'OtherBrand').map((r) => [r.styleId, { seasonCode: this.targetSeason ?? '', styleNo: '' }])));
        const n = results.filter((r) => r.result === 'Added').length;
        if (n) this.stylesAdded.emit(n);
      },
      error: (err) => {
        this.saving.set(false);
        toast.error(styleError(err).message);
      },
    });
  }
}
