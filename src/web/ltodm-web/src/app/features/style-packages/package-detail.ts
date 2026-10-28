import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowDown, lucideArrowLeft, lucideArrowUp, lucideCircleAlert, lucideDownload, lucideFileSpreadsheet, lucideImage, lucideLeaf, lucideLightbulb, lucideLock, lucideThumbsDown, lucideThumbsUp, lucidePackage, lucidePencil, lucidePlus, lucideTrash2,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { toneBadge } from '@tms/shared/ui/tones';
import { GENDERS, StyleLibraryService, StyleLookups, styleError } from '../style-library/style-library.service';
import { PackageFormDialog } from './package-form-dialog';
import { PackageNoticeService } from './package-notice.service';
import { SharePanel } from './share-panel';
import { PackageItemDialog } from './package-item-dialog';
import { OUTCOME_TONE, PACKAGE_OUTCOMES, PackageDetail as Detail, PackageItem, PackageOutcome, PackagesService, STATUS_TONE } from './packages.service';

/**
 * A style package: its header (customer, season, status, owner) and the styles offered, in pitch order, each with its
 * colourways and notes. Admins and merchandisers edit the header, reorder, annotate and remove styles; styles are added from
 * the Styles list, a style page or Similar styles. No prices yet (roadmap 5.19).
 */
@Component({
  selector: 'app-package-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, RouterLink, NgIcon, AuthImgDirective, PackageFormDialog, PackageItemDialog, SharePanel, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmNativeSelectImports,
    HlmSkeletonImports, HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [
    provideIcons({ lucideArrowDown, lucideArrowLeft, lucideArrowUp, lucideCircleAlert, lucideDownload, lucideFileSpreadsheet, lucideImage, lucideLeaf, lucideLightbulb, lucideLock, lucideThumbsDown, lucideThumbsUp, lucidePackage, lucidePencil, lucidePlus, lucideTrash2 }),
  ],
  template: `
    <section class="flex flex-col gap-5" [attr.aria-busy]="loading()">
      <a routerLink="/packages" class="text-muted-foreground hover:text-foreground flex w-fit items-center gap-1 text-sm">
        <ng-icon name="lucideArrowLeft" />{{ 'packages.back' | transloco }}
      </a>

      @if (loading() && !detail()) {
        <div hlmSkeleton class="h-40 w-full"></div>
        <div hlmSkeleton class="h-72 w-full"></div>
      } @else if (notFound()) {
        <section hlmCard class="items-center gap-2 px-5 py-14 text-center">
          <p class="font-medium">{{ 'packages.notFound' | transloco }}</p>
          <a routerLink="/packages" class="text-primary text-sm underline-offset-2 hover:underline">{{ 'packages.back' | transloco }}</a>
        </section>
      } @else if (detail(); as d) {
        @let p = d.package;
        <section hlmCard class="gap-4 p-5">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="min-w-0">
              <h1 class="flex flex-wrap items-center gap-2 text-2xl font-semibold tracking-tight">
                <span class="break-words">{{ p.name }}</span>
                <span class="rounded-full px-2 py-0.5 text-xs font-medium" [class]="toneBadge(tone())">{{ 'packages.status.' + p.status | transloco }}</span>
              </h1>
              <div class="mt-2 flex flex-wrap gap-1.5">
                <span hlmBadge variant="secondary">{{ p.customerName }}</span>
                @if (p.brandCode && p.brandCode !== p.customerCode) { <span hlmBadge variant="outline">{{ 'packages.brand' | transloco: { brand: p.brandCode } }}</span> }
                @if (p.seasonCode) { <span hlmBadge variant="secondary">{{ p.seasonCode }}</span> }
              </div>
            </div>
            <div class="flex w-full flex-wrap gap-2 sm:w-auto">
              <!-- Line sheet for the client (everyone who can read the package) -->
              <button hlmBtn variant="outline" type="button" class="h-11 flex-1 sm:flex-initial lg:h-9" [disabled]="!!downloading()" (click)="download('pdf')">
                @if (downloading() === 'pdf') { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideDownload" /> }
                {{ 'packages.lineSheetPdf' | transloco }}
              </button>
              <button hlmBtn variant="outline" type="button" class="h-11 flex-1 sm:flex-initial lg:h-9" [disabled]="!!downloading()" (click)="download('xlsx')">
                @if (downloading() === 'xlsx') { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideFileSpreadsheet" /> }
                {{ 'packages.lineSheetExcel' | transloco }}
              </button>
            @if (canEdit()) {
                <a hlmBtn variant="outline" class="h-11 flex-1 sm:flex-initial lg:h-9" routerLink="/packages/offer" [queryParams]="{ customer: p.customerCode, package: p.packageId }"
                   [title]="'packages.offer.subtitle' | transloco"><ng-icon name="lucideLightbulb" />{{ 'packages.offer.title' | transloco }}</a>
                <a hlmBtn variant="outline" class="h-11 flex-1 sm:flex-initial lg:h-9" routerLink="/styles" [queryParams]="{ customer: p.customerCode }"
                   [title]="'packages.addStylesHint' | transloco"><ng-icon name="lucidePlus" />{{ 'packages.addStyles' | transloco }}</a>
                <button hlmBtn variant="outline" type="button" class="h-11 flex-1 sm:flex-initial lg:h-9" (click)="editOpen.set(true)">
                  <ng-icon name="lucidePencil" />{{ 'packages.edit' | transloco }}
                </button>
                @if (p.status === 'Draft' && !p.sentUtc) {
                  <button hlmBtn variant="ghost" type="button" class="text-destructive h-11 lg:h-9" [attr.aria-label]="'packages.delete' | transloco" (click)="deletePackage()">
                    <ng-icon name="lucideTrash2" />
                  </button>
                }
            }
            </div>
          </div>
          <p class="text-muted-foreground text-xs">{{ 'packages.lineSheetHint' | transloco }}</p>
          @if (p.description) { <p class="text-sm whitespace-pre-line">{{ p.description }}</p> }
          <p class="text-muted-foreground text-xs">
            {{ 'packages.ownedBy' | transloco: { owner: p.ownerName } }}
            · {{ 'packages.createdBy' | transloco: { user: p.createdBy, date: (p.createdUtc | date: 'medium') } }}
            @if (p.sentUtc) { · {{ 'packages.sentOn' | transloco: { date: (p.sentUtc | date: 'mediumDate') } }} }
            @if (p.updatedBy) { · {{ 'packages.changedBy' | transloco: { user: p.updatedBy, date: (p.updatedUtc | date: 'medium') } }} }
          </p>
        </section>

        <app-share-panel [packageId]="p.packageId" [customerName]="p.customerName" [canEdit]="canEdit()" (changed)="load()" />

        <h2 class="text-lg font-semibold">{{ 'packages.styleCount' | transloco: { count: d.items.length } }}</h2>
        @if (!d.items.length) {
          <section hlmCard class="items-center gap-2 px-5 py-12 text-center">
            <ng-icon name="lucidePackage" class="text-muted-foreground text-4xl" />
            <p class="font-medium">{{ 'packages.noStyles' | transloco }}</p>
            <p class="text-muted-foreground max-w-md text-sm">{{ 'packages.noStylesHint' | transloco }}</p>
          </section>
        } @else {
          <ul class="grid gap-4 md:grid-cols-2 2xl:grid-cols-3" role="list">
            @for (i of d.items; track i.packageItemId; let first = $first; let last = $last) {
              <li hlmCard class="gap-3 p-4" [class.opacity-60]="busy()">
                <div class="flex gap-4">
                  <a [routerLink]="['/styles', i.styleId]" class="bg-muted flex size-28 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                    @let thumb = thumbFor(i);
                    @if (thumb) {
                      <img [appAuthSrc]="thumb" [alt]="i.styleNo" class="size-full object-contain" loading="lazy" (loadError)="markBroken(thumb)" />
                    } @else {
                      <ng-icon name="lucideImage" class="text-muted-foreground text-2xl" />
                    }
                  </a>
                  <div class="flex min-w-0 flex-1 flex-col gap-1">
                    <a [routerLink]="['/styles', i.styleId]" class="truncate font-mono text-sm font-medium underline-offset-2 hover:underline">{{ i.styleNo }}</a>
                    <span class="text-muted-foreground truncate text-xs">{{ i.customerCode }} · {{ i.seasonCode }}</span>
                    <span class="truncate text-sm">{{ i.modelName || i.description || '—' }}</span>
                    <span class="text-muted-foreground truncate text-xs">
                      {{ [i.productTypeName, i.gender ? (genderLabel(i.gender) | transloco) : null, i.weaveTypeName].filter(isText).join(' · ') || '—' }}
                    </span>
                    <span class="text-muted-foreground text-xs">
                      {{ i.garmentLeadTimeDays ? ('packages.leadTime' | transloco: { days: i.garmentLeadTimeDays }) : ('packages.leadTimeUnknown' | transloco) }}
                    </span>
                    <div class="flex flex-wrap gap-1">
                      @if (i.outcome) {
                        <span class="rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge(outcomeTone[i.outcome])"
                              [title]="i.outcomeBy ? ('packages.outcome.setBy' | transloco: { user: i.outcomeBy, date: (i.outcomeUtc | date: 'mediumDate') }) : ''">{{ 'packages.outcome.' + i.outcome | transloco }}</span>
                      }
                      @if (!i.isActive) { <span hlmBadge variant="outline">{{ 'styles.inactive' | transloco }}</span> }
                      @if (i.recycledFabricLines) {
                        <span class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium" [class]="toneBadge('green')">
                          <ng-icon name="lucideLeaf" />
                          {{ (i.recycledFabricLines === i.fabricLines ? 'packages.recycledAll' : 'packages.recycledSome') | transloco: { recycled: i.recycledFabricLines, total: i.fabricLines } }}
                        </span>
                      }
                    </div>
                  </div>
                </div>
                @if (i.otherBrand) {
                  <p class="flex items-start gap-1.5 text-xs text-amber-700 dark:text-amber-400">
                    <ng-icon name="lucideCircleAlert" class="mt-0.5 shrink-0" />{{ 'packages.otherBrandItem' | transloco }}
                  </p>
                }
                <!-- Key materials: main fabrics, then trims (nothing guessed: a style without BOM lines says so) -->
                @let fabrics = materialsOf(i, 'Fabric');
                @let trims = materialsOf(i, 'Trim');
                <dl class="flex flex-col gap-1 text-xs">
                  <div class="flex gap-2">
                    <dt class="text-muted-foreground w-14 shrink-0">{{ 'packages.fabrics' | transloco }}</dt>
                    <dd class="min-w-0 flex-1">
                      @for (m of fabrics; track m.materialCode) {
                        <span class="block truncate" [title]="m.description ?? m.materialCode">
                          {{ m.composition || m.description || m.materialCode }}
                          @if (m.recycled) { <ng-icon name="lucideLeaf" class="text-emerald-600 dark:text-emerald-400" [title]="'packages.recycled' | transloco" /> }
                        </span>
                      } @empty {
                        <span class="text-muted-foreground">{{ 'packages.noFabrics' | transloco }}</span>
                      }
                    </dd>
                  </div>
                  @if (trims.length) {
                    <div class="flex gap-2">
                      <dt class="text-muted-foreground w-14 shrink-0">{{ 'packages.trims' | transloco }}</dt>
                      <dd class="text-muted-foreground min-w-0 flex-1">
                        @for (m of trims; track m.materialCode) {
                          <span class="block truncate" [title]="m.description ?? m.materialCode">{{ m.description || m.materialCode }}</span>
                        }
                      </dd>
                    </div>
                  }
                </dl>
                <!-- Colourways offered -->
                @let offered = offeredOf(i);
                <div class="flex flex-col gap-1">
                  <span class="text-muted-foreground text-xs">
                    {{ (i.allColorways ? 'packages.allColorwaysOffered' : 'packages.someColorwaysOffered') | transloco: { count: offered.length } }}
                  </span>
                  <div class="flex flex-wrap gap-1">
                    @for (c of offered.slice(0, 8); track c.colorwayCode) {
                      <span class="rounded border px-1.5 py-0.5 font-mono text-[11px]" [class.text-destructive]="c.missing" [title]="c.missing ? ('packages.item.missing' | transloco) : (c.colorwayName ?? '')">
                        {{ c.colorwayCode }}
                      </span>
                    }
                    @if (offered.length > 8) { <span class="text-muted-foreground text-[11px]">+{{ offered.length - 8 }}</span> }
                  </div>
                </div>
                @if (i.pitchNote) { <p class="text-sm whitespace-pre-line">{{ i.pitchNote }}</p> }
                @if (i.feedback?.length) {
                  <!-- What clients answered through share links: the tally, then each person's answer -->
                  @let likes = countVotes(i, 'Like');
                  @let passes = countVotes(i, 'Pass');
                  <ul class="flex flex-col gap-1 rounded-md border px-2 py-1.5 text-xs" role="list" [attr.aria-label]="'packages.share.answers' | transloco">
                    @if (likes + passes > 1) {
                      <li class="text-muted-foreground flex items-center gap-2 border-b pb-1 font-medium">
                        <span class="flex items-center gap-1"><ng-icon name="lucideThumbsUp" class="text-emerald-600 dark:text-emerald-400" />{{ likes }}</span>
                        <span class="flex items-center gap-1"><ng-icon name="lucideThumbsDown" class="text-destructive" />{{ passes }}</span>
                        @if (likes && passes) { <span>{{ 'packages.share.mixed' | transloco }}</span> }
                      </li>
                    }
                    @for (f of i.feedback!; track $index) {
                      <li class="flex items-start gap-1.5">
                        @if (f.verdict === 'Like') { <ng-icon name="lucideThumbsUp" class="mt-0.5 shrink-0 text-emerald-600 dark:text-emerald-400" /> }
                        @else if (f.verdict === 'Pass') { <ng-icon name="lucideThumbsDown" class="text-destructive mt-0.5 shrink-0" /> }
                        <span class="min-w-0">
                          <span class="text-muted-foreground">{{ f.name || f.label }}{{ f.name ? ' (' + f.label + ')' : '' }}:</span>
                          {{ f.comment || ((f.verdict === 'Like' ? 'packages.shared.like' : 'packages.shared.pass') | transloco) }}
                        </span>
                      </li>
                    }
                  </ul>
                }
                @if (i.internalNote) {
                  <p class="text-muted-foreground bg-muted/50 flex items-start gap-1.5 rounded-md px-2 py-1.5 text-xs whitespace-pre-line">
                    <ng-icon name="lucideLock" class="mt-0.5 shrink-0" [title]="'packages.internalOnly' | transloco" />{{ i.internalNote }}
                  </p>
                }
                @if (canEdit()) {
                  <div class="mt-auto flex flex-wrap items-center gap-1 border-t pt-2">
                    <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 lg:h-8" (click)="openItem(i)"><ng-icon name="lucidePencil" />{{ 'packages.editItem' | transloco }}</button>
                    <!-- What became of it (hit rate) -->
                    <hlm-native-select [selectId]="'outcome-' + i.packageItemId" selectClass="h-11 w-36 text-xs lg:h-8" [attr.aria-label]="'packages.outcome.label' | transloco: { styleNo: i.styleNo }"
                                       [value]="i.outcome ?? ''" (valueChange)="setOutcome(i, $event || null)">
                      <option hlmNativeSelectOption value="">{{ 'packages.outcome.none' | transloco }}</option>
                      @for (o of outcomes; track o) { <option hlmNativeSelectOption [value]="o">{{ 'packages.outcome.' + o | transloco }}</option> }
                    </hlm-native-select>
                    <span class="flex-1"></span>
                    <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 w-11 lg:h-8 lg:w-8" [disabled]="first || busy()"
                            [attr.aria-label]="'packages.moveUp' | transloco" (click)="move(i, -1)"><ng-icon name="lucideArrowUp" /></button>
                    <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 w-11 lg:h-8 lg:w-8" [disabled]="last || busy()"
                            [attr.aria-label]="'packages.moveDown' | transloco" (click)="move(i, 1)"><ng-icon name="lucideArrowDown" /></button>
                    <button hlmBtn variant="ghost" size="sm" type="button" class="text-destructive h-11 w-11 lg:h-8 lg:w-8" [disabled]="busy()"
                            [attr.aria-label]="'packages.removeItem' | transloco: { styleNo: i.styleNo }" (click)="remove(i)"><ng-icon name="lucideTrash2" /></button>
                  </div>
                }
              </li>
            }
          </ul>
        }

        <app-package-form-dialog [open]="editOpen()" [pkg]="p" [lookups]="lookups()" (closed)="editOpen.set(false)" (saved)="onSaved()" />
        <app-package-item-dialog [open]="!!editingItem()" [packageId]="p.packageId" [item]="editingItem()" (closed)="editingItem.set(null)" (saved)="onItemSaved()" />
      }
    </section>
  `,
})
export class PackageDetailPage implements OnInit {
  private readonly svc = inject(PackagesService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly canEdit = this.styles.canEdit;
  readonly toneBadge = toneBadge;
  readonly packageId = signal(0);
  readonly detail = signal<Detail | null>(null);
  readonly lookups = signal<StyleLookups | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly busy = signal(false);
  readonly downloading = signal<'pdf' | 'xlsx' | null>(null);
  readonly editOpen = signal(false);
  readonly editingItem = signal<PackageItem | null>(null);
  readonly broken = signal<ReadonlySet<string>>(new Set());
  readonly tone = computed(() => STATUS_TONE[this.detail()?.package.status ?? 'Draft'] ?? 'neutral');

  readonly isText = (v: string | null): v is string => !!v;
  private readonly sharePanel = viewChild(SharePanel);
  private readonly notices = inject(PackageNoticeService);
  readonly outcomes = PACKAGE_OUTCOMES;
  readonly outcomeTone = OUTCOME_TONE;

  ngOnInit(): void {
    this.styles.lookups().subscribe({ next: (l) => this.lookups.set(l) });
    // A client answered on this package (pushed to its owner and the link maker): show it at once.
    this.notices.answered.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((packageId) => {
      if (packageId !== this.packageId()) return;
      this.load();
      this.sharePanel()?.load();
    });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.packageId.set(Number(params.get('id')) || 0);
      this.detail.set(null);
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.notFound.set(false);
    this.svc.get(this.packageId()).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
        this.busy.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.busy.set(false);
        if (e?.status === 404) this.notFound.set(true);
        else toast.error(styleError(e).message);
      },
    });
  }

  /** The colourways in the offer (all on the style, or the chosen ones, missing ones last). */
  offeredOf(i: PackageItem) {
    return i.colorways.filter((c) => c.offered);
  }

  materialsOf(i: PackageItem, kind: 'Fabric' | 'Trim') {
    return i.materials.filter((m) => m.kind === kind);
  }

  thumbFor(i: PackageItem): string | null {
    const broken = this.broken();
    return [i.imageUrl, i.sketchUrl].find((u): u is string => !!u && !broken.has(u)) ?? null;
  }

  markBroken(url: string): void {
    this.broken.update((s) => new Set(s).add(url));
  }

  genderLabel = (code: string) => (GENDERS.some((g) => g.code === code) ? `styles.gender.${code}` : code);

  openItem(i: PackageItem): void {
    this.editingItem.set(i);
  }

  onSaved(): void {
    this.editOpen.set(false);
    toast.success(this.t('packages.saved'));
    this.load();
  }

  onItemSaved(): void {
    this.editingItem.set(null);
    this.load();
  }

  /** Moves a style one place up (-1) or down (1). */
  move(item: PackageItem, step: -1 | 1): void {
    const d = this.detail();
    if (!d || this.busy()) return;
    const ids = d.items.map((i) => i.packageItemId);
    const at = ids.indexOf(item.packageItemId);
    const to = at + step;
    if (at < 0 || to < 0 || to >= ids.length) return;
    [ids[at], ids[to]] = [ids[to], ids[at]];
    // Shown at once; the reload confirms the saved order.
    const byId = new Map(d.items.map((i) => [i.packageItemId, i]));
    this.detail.set({ ...d, items: ids.map((id) => byId.get(id)!) });
    this.busy.set(true);
    this.svc.reorder(d.package.packageId, ids).subscribe({
      next: () => this.load(),
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  async download(format: 'pdf' | 'xlsx'): Promise<void> {
    const p = this.detail()?.package;
    if (!p || this.downloading()) return;
    this.downloading.set(format);
    try {
      await this.svc.downloadLineSheet(p.packageId, p.name, format);
    } catch {
      toast.error(this.t('packages.lineSheetFailed'));
    } finally {
      this.downloading.set(null);
    }
  }

  /** The style's votes of one kind through all links (one per person). */
  countVotes(i: PackageItem, verdict: 'Like' | 'Pass'): number {
    return (i.feedback ?? []).filter((f) => f.verdict === verdict).length;
  }

  setOutcome(item: PackageItem, outcome: string | null): void {
    const d = this.detail();
    if (!d) return;
    const value = (outcome || null) as PackageOutcome | null;
    this.svc.setOutcome(d.package.packageId, item.packageItemId, value).subscribe({
      next: () => this.load(),
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  async remove(item: PackageItem): Promise<void> {
    const d = this.detail();
    if (!d) return;
    const ok = await this.confirmDlg.confirm({
      title: this.t('packages.removeTitle'),
      message: this.t('packages.removeMessage', { styleNo: item.styleNo, name: d.package.name }),
      confirmLabel: this.t('actions.remove'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.busy.set(true);
    this.svc.removeItems(d.package.packageId, [item.packageItemId]).subscribe({
      next: () => {
        toast.success(this.t('packages.removed', { styleNo: item.styleNo }));
        this.load();
      },
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  async deletePackage(): Promise<void> {
    const p = this.detail()?.package;
    if (!p) return;
    const ok = await this.confirmDlg.confirm({
      title: this.t('packages.delete'),
      message: this.t('packages.deleteMessage', { name: p.name, count: this.detail()!.items.length }),
      confirmLabel: this.t('packages.delete'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.delete(p.packageId, p.rowVer).subscribe({
      next: () => {
        toast.success(this.t('packages.deleted', { name: p.name }));
        void this.router.navigate(['/packages']);
      },
      error: (e) => {
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(key, params);
  }
}
