import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendar, lucideClipboardCheck, lucideLeaf, lucideLightbulb, lucidePencil, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { ClientProfileDialog } from './client-profile-dialog';
import { ClientProfilesService, ClientSummary } from './client-profiles.service';

/**
 * Style Packages > Client profiles (roadmap 5.13): every brand, and every customer without a brand, with what it accepts (price
 * band, minimum recycled share, restricted words, preferred mills) and its pitch dates. Clients with a profile come first.
 */
@Component({
  selector: 'app-client-profiles-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, RouterLink, NgIcon, ClientProfileDialog, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSkeletonImports, TranslocoPipe],
  providers: [provideIcons({ lucideCalendar, lucideClipboardCheck, lucideLeaf, lucideLightbulb, lucidePencil, lucideSearch })],
  template: `
    <section class="flex flex-col gap-5">
      <header>
        <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideClipboardCheck" />{{ 'clientProfiles.title' | transloco }}</h1>
        <p class="text-muted-foreground mt-1 max-w-3xl text-sm">{{ 'clientProfiles.subtitle' | transloco }}</p>
      </header>
      <div class="relative w-full sm:max-w-sm">
        <ng-icon name="lucideSearch" class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 -translate-y-1/2" />
        <input hlmInput type="search" class="h-11 w-full pl-9 lg:h-9" [attr.aria-label]="'clientProfiles.search' | transloco" [placeholder]="'clientProfiles.search' | transloco"
               [value]="search()" (input)="search.set($any($event.target).value)" />
      </div>

      @if (loading()) {
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">@for (i of [1, 2, 3]; track i) { <div hlmSkeleton class="h-36 w-full"></div> }</div>
      } @else {
        <ul class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3" role="list">
          @for (c of shown(); track c.clientKey) {
            <li hlmCard class="gap-2 p-4">
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <p class="truncate font-medium">{{ c.name }}</p>
                  <p class="text-muted-foreground text-xs">
                    <span class="font-mono">{{ c.clientKey }}</span> · {{ (c.isBrand ? 'clientProfiles.brandAccounts' : 'clientProfiles.customer') | transloco: { count: c.accounts } }}
                    · {{ 'clientProfiles.counts' | transloco: { styles: (c.styles | number), packages: c.packages } }}
                  </p>
                </div>
                @if (c.hasProfile) { <span hlmBadge variant="secondary" class="shrink-0">{{ 'clientProfiles.set' | transloco }}</span> }
              </div>
              @if (c.hasProfile) {
                <ul class="flex flex-col gap-0.5 text-xs" role="list">
                  @if (c.priceMin !== null || c.priceMax !== null) {
                    <li>{{ 'clientProfiles.priceBand' | transloco: { min: c.priceMin ?? '—', max: c.priceMax ?? '—', currency: c.currency } }}</li>
                  }
                  @if (c.minRecycledPct) { <li class="flex items-center gap-1"><ng-icon name="lucideLeaf" class="text-emerald-600" />{{ 'clientProfiles.recycledRule' | transloco: { pct: c.minRecycledPct } }}</li> }
                  @if (c.restrictedTerms) { <li class="truncate" [title]="c.restrictedTerms">{{ 'clientProfiles.restrictedRule' | transloco: { terms: c.restrictedTerms } }}</li> }
                  @if (c.mills) { <li>{{ 'clientProfiles.millsRule' | transloco: { count: c.mills } }}</li> }
                  @if (c.nextPitchDate) {
                    <li class="flex items-center gap-1"><ng-icon name="lucideCalendar" />{{ 'clientProfiles.nextPitch' | transloco: { date: (c.nextPitchDate | date: 'mediumDate'), season: c.nextPitchSeason ?? '' } }}</li>
                  }
                  @if (c.accountManager) { <li class="text-muted-foreground">{{ 'clientProfiles.managedBy' | transloco: { user: c.accountManager } }}</li> }
                </ul>
              } @else {
                <p class="text-muted-foreground text-xs">{{ 'clientProfiles.none' | transloco }}</p>
              }
              <div class="mt-auto flex flex-wrap gap-2 pt-1">
                <button hlmBtn variant="outline" type="button" class="h-11 lg:h-8" (click)="editing.set(c)">
                  <ng-icon name="lucidePencil" />{{ (canEdit() ? (c.hasProfile ? 'clientProfiles.edit' : 'clientProfiles.setUp') : 'clientProfiles.view') | transloco }}
                </button>
                @if (c.styles) {
                  <a hlmBtn variant="ghost" class="h-11 lg:h-8" routerLink="/packages/offer" [queryParams]="{ customer: firstCustomer(c) }"><ng-icon name="lucideLightbulb" />{{ 'packages.offer.title' | transloco }}</a>
                }
              </div>
            </li>
          } @empty {
            <li class="text-muted-foreground col-span-full py-10 text-center text-sm">{{ 'clientProfiles.noMatch' | transloco }}</li>
          }
        </ul>
      }
    </section>
    <app-client-profile-dialog [open]="!!editing()" [client]="editing()" [lookups]="lookups()" [canEdit]="canEdit()" (closed)="editing.set(null)" (saved)="onSaved()" />
  `,
})
export class ClientProfilesPage implements OnInit {
  private readonly svc = inject(ClientProfilesService);
  private readonly styles = inject(StyleLibraryService);

  readonly canEdit = this.styles.canEdit;
  readonly clients = signal<ClientSummary[]>([]);
  readonly lookups = signal<StyleLookups | null>(null);
  readonly loading = signal(true);
  readonly search = signal('');
  readonly editing = signal<ClientSummary | null>(null);
  readonly shown = computed(() => {
    const q = this.search().trim().toLowerCase();
    return q ? this.clients().filter((c) => c.clientKey.toLowerCase().includes(q) || c.name.toLowerCase().includes(q)) : this.clients();
  });

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l) });
    this.load();
  }

  private load(): void {
    this.svc.list().subscribe({
      next: (list) => {
        this.clients.set(list);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  /** What to offer takes a customer: the client's account with the most styles. */
  firstCustomer(c: ClientSummary): string {
    return c.mainCustomerCode ?? c.clientKey;
  }

  onSaved(): void {
    this.editing.set(null);
    this.load();
  }
}
