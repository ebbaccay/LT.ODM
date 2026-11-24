import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideTag } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { CostingService, ClientBand, PackageItemQuotes, QuoteBasisKind } from '../costing/costing.service';
import { estimateBasisText } from '../costing/price-summary';
import { styleError } from '../style-library/style-library.service';

/**
 * A style's quote on the package page (roadmap 5.19; Admin, Merchandiser and Costing only): what its IO history suggests, with the basis stated,
 * to accept or type over. Every change is a new version. The client sees only the price, and only where a link or line sheet shows prices.
 */
@Component({
  selector: 'app-package-item-quote',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, NgIcon, HlmButtonImports, HlmInputImports, TranslocoPipe],
  providers: [provideIcons({ lucideCircleAlert, lucideTag })],
  template: `
    <div class="flex flex-col gap-1.5 rounded-md border px-2.5 py-2 text-xs">
      @if (editing()) {
        <form class="flex flex-wrap items-end gap-2" (submit)="$event.preventDefault(); saveTyped()">
          <label class="flex flex-col gap-1">
            <span class="text-muted-foreground">{{ 'packages.quote.fob' | transloco }}</span>
            <input hlmInput type="number" min="0.01" step="0.01" class="h-11 w-24 lg:h-8" [value]="fob()" (input)="fob.set($any($event.target).value)" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-muted-foreground">{{ 'packages.quote.currency' | transloco }}</span>
            <input hlmInput maxlength="3" class="h-11 w-16 uppercase lg:h-8" [value]="currency()" (input)="currency.set($any($event.target).value)" />
          </label>
          <button hlmBtn size="sm" type="submit" class="h-11 lg:h-8" [disabled]="saving()">{{ 'packages.quote.save' | transloco }}</button>
          <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 lg:h-8" (click)="editing.set(false)">{{ 'packages.quote.cancel' | transloco }}</button>
        </form>
        @if (bandText(); as b) { <p class="text-muted-foreground">{{ b }}</p> }
        @if (typedOutOfBand()) {
          <p class="flex items-center gap-1 text-amber-800 dark:text-amber-300"><ng-icon name="lucideCircleAlert" />{{ 'packages.quote.outOfBand' | transloco }}</p>
        }
      } @else if (current(); as q) {
        <p class="flex flex-wrap items-center gap-x-2 gap-y-1">
          <ng-icon name="lucideTag" class="text-muted-foreground" />
          <span class="text-muted-foreground">{{ 'packages.quote.quoted' | transloco }}</span>
          <span class="text-sm font-semibold tabular-nums">{{ q.fob | number: '1.2-2' }} {{ q.currencyCode }}</span>
          @if (canEdit()) { <button type="button" class="text-primary min-h-6 underline-offset-2 hover:underline" (click)="startEdit()">{{ 'packages.quote.change' | transloco }}</button> }
        </p>
        @if (q.basisText) { <p class="text-muted-foreground">{{ q.basisText }}</p> }
        <p class="text-muted-foreground">{{ 'packages.quote.setBy' | transloco: { user: q.quotedBy, date: (q.quotedUtc | date: 'mediumDate') } }}</p>
        @if (data()?.quotes?.length! > 1) {
          <details class="text-muted-foreground">
            <summary class="cursor-pointer">{{ 'packages.quote.history' | transloco: { count: data()!.quotes.length } }}</summary>
            <ul class="mt-1 flex flex-col gap-0.5" role="list">
              @for (h of data()!.quotes; track h.version) {
                <li class="tabular-nums">v{{ h.version }}: {{ h.fob | number: '1.2-2' }} {{ h.currencyCode }} · {{ h.quotedBy }} · {{ h.quotedUtc | date: 'mediumDate' }}</li>
              }
            </ul>
          </details>
        }
      } @else if (suggestion(); as s) {
        <p class="flex flex-wrap items-center gap-x-2 gap-y-1">
          <ng-icon name="lucideTag" class="text-muted-foreground" />
          <span class="text-muted-foreground">{{ 'packages.quote.suggested' | transloco }}</span>
          <span class="text-sm font-semibold tabular-nums">{{ rounded(s.latestFob) | number: '1.2-2' }} {{ s.currencyCode }}</span>
          @if (canEdit()) {
            <button hlmBtn variant="outline" size="sm" type="button" class="h-11 lg:h-7" [disabled]="saving()" (click)="useSuggestion()">{{ 'packages.quote.use' | transloco }}</button>
            <button type="button" class="text-primary min-h-6 underline-offset-2 hover:underline" (click)="startEdit()">{{ 'packages.quote.typeOwn' | transloco }}</button>
          }
        </p>
        <p class="text-muted-foreground">{{ basis() }}</p>
        @if (suggestionOutOfBand()) {
          <p class="flex items-center gap-1 text-amber-800 dark:text-amber-300"><ng-icon name="lucideCircleAlert" />{{ bandText() }}</p>
        }
      } @else if (estimate(); as e) {
        <p class="flex flex-wrap items-center gap-x-2 gap-y-1">
          <ng-icon name="lucideTag" class="text-muted-foreground" />
          <span class="text-muted-foreground">{{ 'packages.quote.estimated' | transloco }}</span>
          <span class="text-sm font-semibold tabular-nums">{{ rounded(e.estimatedFob) | number: '1.2-2' }} {{ e.currencyCode }}</span>
          @if (canEdit()) {
            <button hlmBtn variant="outline" size="sm" type="button" class="h-11 lg:h-7" [disabled]="saving()" (click)="useEstimate()">{{ 'packages.quote.use' | transloco }}</button>
            <button type="button" class="text-primary min-h-6 underline-offset-2 hover:underline" (click)="startEdit()">{{ 'packages.quote.typeOwn' | transloco }}</button>
          }
        </p>
        <p class="text-muted-foreground">{{ estimateBasis() }}</p>
        @if (estimateOutOfBand()) {
          <p class="flex items-center gap-1 text-amber-800 dark:text-amber-300"><ng-icon name="lucideCircleAlert" />{{ bandText() }}</p>
        }
      } @else {
        <p class="flex flex-wrap items-center gap-2">
          <ng-icon name="lucideTag" class="text-muted-foreground" />
          <span class="text-muted-foreground">{{ 'packages.quote.noPrices' | transloco }}</span>
          @if (canEdit()) { <button type="button" class="text-primary min-h-6 underline-offset-2 hover:underline" (click)="startEdit()">{{ 'packages.quote.setPrice' | transloco }}</button> }
        </p>
      }
    </div>
  `,
})
export class PackageItemQuote {
  private readonly svc = inject(CostingService);
  private readonly i18n = inject(TranslocoService);

  readonly packageId = input.required<number>();
  readonly packageItemId = input.required<number>();
  readonly data = input<PackageItemQuotes | undefined>();
  readonly band = input<ClientBand | null>(null);
  /** False for designers: they see quotes and suggestions but do not set them. */
  readonly canEdit = input(true);
  readonly saved = output<void>();

  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly fob = signal('');
  readonly currency = signal('USD');

  readonly current = computed(() => this.data()?.quotes[0] ?? null);
  readonly suggestion = computed(() => this.data()?.suggestion ?? null);
  readonly estimate = computed(() => (this.suggestion() ? null : (this.data()?.estimate ?? null)));

  readonly estimateBasis = computed(() => {
    const e = this.estimate();
    return e ? estimateBasisText(this.i18n, e) : '';
  });

  readonly estimateOutOfBand = computed(() => {
    const e = this.estimate();
    return !!e && this.outOfBand(this.rounded(e.estimatedFob), e.currencyCode);
  });

  readonly basis = computed(() => {
    const s = this.suggestion();
    if (!s) return '';
    const n = (v: number) => v.toFixed(2);
    return this.i18n.translate(`packages.quote.basis${s.source}`, {
      ios: s.ios, min: n(s.minFob), max: n(s.maxFob), currency: s.currencyCode, latest: n(s.latestFob), io: s.latestIoNo ?? '', style: s.sourceStyleNo,
    });
  });

  readonly bandText = computed(() => {
    const b = this.band();
    if (!b) return null;
    return this.i18n.translate('packages.quote.band', { min: b.priceMin ?? '-', max: b.priceMax ?? '-', currency: b.currency });
  });

  readonly suggestionOutOfBand = computed(() => {
    const s = this.suggestion();
    return !!s && this.outOfBand(this.rounded(s.latestFob), s.currencyCode);
  });

  readonly typedOutOfBand = computed(() => this.outOfBand(Number(this.fob()), this.currency().trim().toUpperCase()));

  /** Clients are quoted to 2 decimals. */
  rounded(v: number): number {
    return Math.round(v * 100) / 100;
  }

  private outOfBand(fob: number, currency: string): boolean {
    const b = this.band();
    if (!b || !fob || b.currency !== currency) return false;
    return (b.priceMin !== null && fob < b.priceMin) || (b.priceMax !== null && fob > b.priceMax);
  }

  startEdit(): void {
    const q = this.current();
    const s = this.suggestion();
    const e = this.estimate();
    this.fob.set(q ? String(this.rounded(q.fob)) : s ? String(this.rounded(s.latestFob)) : e ? String(this.rounded(e.estimatedFob)) : '');
    this.currency.set(q?.currencyCode ?? s?.currencyCode ?? e?.currencyCode ?? this.band()?.currency ?? 'USD');
    this.editing.set(true);
  }

  useEstimate(): void {
    const e = this.estimate();
    if (e) this.save(this.rounded(e.estimatedFob), e.currencyCode, 'Estimate', this.estimateBasis());
  }

  useSuggestion(): void {
    const s = this.suggestion();
    if (s) this.save(this.rounded(s.latestFob), s.currencyCode, s.source, this.basis());
  }

  saveTyped(): void {
    const s = this.suggestion();
    const fob = Number(this.fob());
    const currency = this.currency().trim().toUpperCase();
    const same = !!s && s.currencyCode === currency && this.rounded(s.latestFob) === fob;
    this.save(fob, currency, same ? s!.source : 'Typed', same ? this.basis() : this.i18n.translate('packages.quote.basisTyped'));
  }

  private save(fob: number, currency: string, basisKind: QuoteBasisKind, basisText: string): void {
    this.saving.set(true);
    this.svc.saveQuote(this.packageId(), this.packageItemId(), { fob, currency, basisKind, basisText }).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.saved.emit();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(styleError(e).message);
      },
    });
  }
}
