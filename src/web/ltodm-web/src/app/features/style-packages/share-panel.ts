import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucideExternalLink, lucideLink, lucideThumbsDown, lucideThumbsUp, lucideTriangleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { styleError } from '../style-library/style-library.service';
import { PackageShare, PackagesService } from './packages.service';

/**
 * Package page > Share with client (roadmap 5.16): links the client opens without signing in, one per recipient. A new
 * link is shown once (only its fingerprint is kept), with Copy; each link lists its views and the client's answers, and can
 * be revoked. Making a link turns a draft package into Sent; the client's first visit makes it Viewed.
 */
@Component({
  selector: 'app-share-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon, HlmButtonImports, HlmCardImports, HlmInputImports, HlmLabelImports, HlmSpinnerImports, TranslocoPipe],
  providers: [provideIcons({ lucideCopy, lucideExternalLink, lucideLink, lucideThumbsDown, lucideThumbsUp, lucideTriangleAlert })],
  template: `
    <section hlmCard class="gap-4 p-5">
      <div>
        <h2 class="flex items-center gap-2 font-semibold"><ng-icon name="lucideLink" />{{ 'packages.share.title' | transloco }}</h2>
        <p class="text-muted-foreground text-sm">{{ 'packages.share.hint' | transloco }}</p>
      </div>

      @if (canEdit()) {
        <form class="flex flex-col gap-2 sm:flex-row sm:items-end" (submit)="$event.preventDefault(); create()">
          <div class="flex flex-1 flex-col gap-1.5">
            <label hlmLabel for="share-label">{{ 'packages.share.label' | transloco }}</label>
            <input hlmInput id="share-label" class="h-11 lg:h-9" maxlength="150" autocomplete="off" [placeholder]="'packages.share.labelPlaceholder' | transloco"
                   [value]="label()" (input)="label.set($any($event.target).value)" />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="share-days">{{ 'packages.share.days' | transloco }}</label>
            <input hlmInput id="share-days" type="number" min="1" max="90" class="h-11 w-full sm:w-28 lg:h-9" [value]="days()" (input)="days.set(+$any($event.target).value)" />
          </div>
          <button hlmBtn type="submit" class="h-11 lg:h-9" [disabled]="creating()">
            @if (creating()) { <hlm-spinner class="size-4" /> }
            {{ 'packages.share.create' | transloco }}
          </button>
        </form>
      }

      @if (newLink(); as url) {
        <!-- Shown once: only a fingerprint of the link is kept -->
        <div class="flex flex-col gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3">
          <p class="flex items-start gap-1.5 text-xs text-amber-800 dark:text-amber-300"><ng-icon name="lucideTriangleAlert" class="mt-0.5 shrink-0" />{{ 'packages.share.onceOnly' | transloco }}</p>
          <div class="flex flex-col gap-2 sm:flex-row">
            <input hlmInput readonly class="h-11 flex-1 font-mono text-xs lg:h-9" [value]="url" (focus)="$any($event.target).select()" [attr.aria-label]="'packages.share.link' | transloco" />
            <button hlmBtn type="button" class="h-11 lg:h-9" (click)="copy(url)"><ng-icon name="lucideCopy" />{{ 'packages.share.copy' | transloco }}</button>
            <a hlmBtn variant="outline" class="h-11 lg:h-9" [href]="url" target="_blank" rel="noopener"><ng-icon name="lucideExternalLink" />{{ 'packages.share.open' | transloco }}</a>
          </div>
        </div>
      }

      @if (shares().length) {
        <ul class="divide-y rounded-md border" role="list">
          @for (s of shares(); track s.shareId) {
            <li class="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2.5 text-sm" [class.opacity-60]="!s.active">
              <span class="min-w-0 flex-1">
                <span class="block truncate font-medium">{{ s.label }}</span>
                <span class="text-muted-foreground block text-xs">
                  {{ 'packages.share.made' | transloco: { user: s.createdBy, date: (s.createdUtc | date: 'mediumDate') } }} ·
                  @if (s.revokedUtc) {
                    {{ 'packages.share.revokedOn' | transloco: { date: (s.revokedUtc | date: 'mediumDate') } }}
                  } @else if (!s.active) {
                    {{ 'packages.share.expired' | transloco }}
                  } @else {
                    {{ 'packages.share.expires' | transloco: { date: (s.expiresUtc | date: 'mediumDate') } }}
                  }
                </span>
              </span>
              <span class="text-muted-foreground text-xs">
                {{ s.views ? ('packages.share.views' | transloco: { count: s.views, date: (s.lastViewedUtc | date: 'medium') }) : ('packages.share.notOpened' | transloco) }}
              </span>
              <span class="flex items-center gap-2 text-xs">
                <span class="flex items-center gap-1" [title]="'packages.shared.like' | transloco"><ng-icon name="lucideThumbsUp" />{{ s.likes }}</span>
                <span class="flex items-center gap-1" [title]="'packages.shared.pass' | transloco"><ng-icon name="lucideThumbsDown" />{{ s.passes }}</span>
                <span>{{ 'packages.share.comments' | transloco: { count: s.comments } }}</span>
              </span>
              @if (canEdit() && s.active) {
                @if (s.canCopy) {
                  <button hlmBtn variant="outline" size="sm" type="button" class="h-11 lg:h-8" [disabled]="copying() === s.shareId" (click)="copyAgain(s)">
                    @if (copying() === s.shareId) { <hlm-spinner class="size-4" /> } @else { <ng-icon name="lucideCopy" /> }
                    {{ 'packages.share.copyLink' | transloco }}
                  </button>
                } @else {
                  <span class="text-muted-foreground text-xs" [title]="'packages.share.oldLinkHint' | transloco">{{ 'packages.share.oldLink' | transloco }}</span>
                }
                <button hlmBtn variant="ghost" size="sm" type="button" class="text-destructive h-11 lg:h-8" (click)="revoke(s)">{{ 'packages.share.revoke' | transloco }}</button>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class SharePanel {
  private readonly svc = inject(PackagesService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly packageId = input.required<number>();
  readonly customerName = input('');
  readonly canEdit = input(false);
  /** A link was made or revoked: the package's status may have changed. */
  readonly changed = output<void>();

  readonly shares = signal<PackageShare[]>([]);
  readonly label = signal('');
  readonly days = signal(30);
  readonly creating = signal(false);
  readonly newLink = signal<string | null>(null);
  /** The link being fetched to copy again. */
  readonly copying = signal<number | null>(null);

  constructor() {
    effect(() => {
      const id = this.packageId();
      if (id) this.load();
    });
  }

  load(): void {
    this.svc.shares(this.packageId()).subscribe({ next: (s) => this.shares.set(s), error: () => this.shares.set([]) });
  }

  create(): void {
    if (this.creating()) return;
    const label = this.label().trim() || this.customerName();
    this.creating.set(true);
    this.svc.createShare(this.packageId(), label, this.days() || 30).subscribe({
      next: (created) => {
        this.creating.set(false);
        this.label.set('');
        this.newLink.set(this.urlFor(created.token));
        this.load();
        this.changed.emit();
      },
      error: (e) => {
        this.creating.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  /** Copies an active link again (to re-send it) and shows it, as when it was made. */
  copyAgain(s: PackageShare): void {
    this.copying.set(s.shareId);
    this.svc.shareLink(this.packageId(), s.shareId).subscribe({
      next: (token) => {
        this.copying.set(null);
        const url = this.urlFor(token);
        this.newLink.set(url);
        void this.copy(url);
      },
      error: (e) => {
        this.copying.set(null);
        toast.error(styleError(e).message);
        this.load();
      },
    });
  }

  private urlFor(token: string): string {
    return `${window.location.origin}/share/${token}`;
  }

  async copy(url: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      toast.success(this.transloco.translate('packages.share.copied'));
    } catch {
      toast.error(this.transloco.translate('packages.share.copyFailed'));
    }
  }

  async revoke(s: PackageShare): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('packages.share.revoke'),
      message: this.transloco.translate('packages.share.revokeMessage', { label: s.label }),
      confirmLabel: this.transloco.translate('packages.share.revoke'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.revokeShare(this.packageId(), s.shareId).subscribe({
      next: () => {
        this.load();
        this.changed.emit();
      },
      error: (e) => toast.error(styleError(e).message),
    });
  }
}
