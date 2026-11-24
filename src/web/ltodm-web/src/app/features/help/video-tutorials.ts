import { HttpClient, HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { environment } from '@env/environment';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCirclePlay, lucideEye, lucideEyeOff, lucideInfo, lucidePencil, lucidePlus, lucideRotateCcw, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { Subscription } from 'rxjs';
import { VideoEditDialog } from './video-edit-dialog';

export interface TrainingVideo {
  file: string;
  title: string;
  description: string | null;
  durationSeconds: number | null;
  sizeBytes: number;
  /** Admins only: Hidden (only Admins see it) or Deleted (gone from the list, can be restored); null = shown. */
  state?: 'Hidden' | 'Deleted' | null;
  stateBy?: string | null;
  stateUtc?: string | null;
  /** Admins only: who may watch (empty = everyone who may open the page). */
  roles?: string[] | null;
  /** Admins only: Release, Uploaded (added on the page) or Replaced (a release video with a file uploaded on the page). */
  source?: 'Release' | 'Uploaded' | 'Replaced' | null;
  /** Admins only: a release video whose details were changed on the page. */
  edited?: boolean | null;
}

interface TrainingVideoList {
  available: boolean;
  videos: TrainingVideo[];
  /** The user may hide, delete and restore videos (Admin). */
  canManage?: boolean;
}

/** "5:31" from seconds. */
export function formatDuration(seconds: number | null): string {
  if (!seconds) return '';
  const s = Math.round(seconds);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

/**
 * Help > Video tutorials: the narrated MP4 manuals shipped with the release (TrainingVideos:Folder). The API needs the
 * sign-in token, which a <video> element cannot send, so the chosen video is fetched through HttpClient (the auth
 * interceptor adds the token) and played from a blob URL. Who may watch is the TrainingVideos:Roles setting (default: everyone).
 * Admins can add videos (uploaded, kept across releases), edit any video's title, description and who may watch, replace
 * its file, hide a video (others no longer see it; Admins see it marked), delete it from the list (kept under Deleted
 * videos to restore) and delete an added video for good. The release's own files are never touched.
 */
@Component({
  selector: 'app-video-tutorials',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon, HlmButtonImports, HlmCardImports, HlmSkeletonImports, TranslocoPipe, VideoEditDialog],
  providers: [provideIcons({ lucideCirclePlay, lucideEye, lucideEyeOff, lucideInfo, lucidePencil, lucidePlus, lucideRotateCcw, lucideTrash2 })],
  template: `
    <section class="flex flex-col gap-5" [attr.aria-busy]="loading()">
      <header class="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 class="flex items-center gap-2 text-2xl font-semibold tracking-tight"><ng-icon name="lucideCirclePlay" />{{ 'titles.videoTutorials' | transloco }}</h1>
          <p class="text-muted-foreground mt-1 max-w-3xl text-sm">{{ 'help.videos.subtitle' | transloco }}</p>
        </div>
        @if (canManage()) {
          <button hlmBtn type="button" class="h-11 lg:h-9" (click)="editing.set('new')"><ng-icon name="lucidePlus" />{{ 'help.videos.add' | transloco }}</button>
        }
      </header>

      @if (loading()) {
        <div hlmSkeleton class="aspect-video w-full max-w-4xl rounded-xl"></div>
      } @else if (forbidden()) {
        <p hlmCard class="text-muted-foreground flex-row items-center gap-2 p-4 text-sm" role="status"><ng-icon name="lucideInfo" />{{ 'help.videos.forbidden' | transloco }}</p>
      } @else if (failed()) {
        <p hlmCard class="text-destructive p-4 text-sm" role="alert">{{ 'help.videos.loadFailed' | transloco }}</p>
      } @else if (!shown().length && !deleted().length) {
        <p hlmCard class="text-muted-foreground flex-row items-center gap-2 p-4 text-sm" role="status"><ng-icon name="lucideInfo" />{{ 'help.videos.none' | transloco }}</p>
      } @else {
        <div class="grid gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
          <!-- Player -->
          <div class="flex min-w-0 flex-col gap-3">
            <div class="bg-muted relative aspect-video w-full overflow-hidden rounded-xl">
              @if (videoUrl(); as url) {
                <video class="size-full bg-black" [src]="url" controls autoplay playsinline [attr.aria-label]="current()?.title"></video>
              } @else if (downloading() !== null) {
                <div class="absolute inset-0 flex flex-col items-center justify-center gap-3 p-6 text-sm" role="status">
                  <span>{{ 'help.videos.loading' | transloco: { percent: downloading() } }}</span>
                  <div class="bg-background h-2 w-64 max-w-full overflow-hidden rounded-full">
                    <div class="bg-primary h-full transition-[width]" [style.width.%]="downloading()"></div>
                  </div>
                </div>
              } @else if (playFailed()) {
                <p class="text-destructive absolute inset-0 flex items-center justify-center p-6 text-sm" role="alert">{{ 'help.videos.playFailed' | transloco }}</p>
              } @else if (current(); as c) {
                <!-- Nothing is downloaded until the user asks for it (the files are 10-20 MB) -->
                <button type="button" class="absolute inset-0 flex cursor-pointer flex-col items-center justify-center gap-2 text-sm" (click)="play(c)">
                  <ng-icon name="lucideCirclePlay" class="text-primary" size="64" />
                  <span class="font-medium">{{ 'help.videos.play' | transloco: { title: c.title } }}</span>
                </button>
              }
            </div>
            @if (current(); as c) {
              <div>
                <h2 class="text-lg font-semibold">{{ c.title }}</h2>
                @if (c.description) { <p class="text-muted-foreground mt-1 text-sm">{{ c.description }}</p> }
              </div>
            }
          </div>

          <!-- List -->
          <div class="flex flex-col gap-3">
            <ul class="flex flex-col gap-2" role="list" [attr.aria-label]="'help.videos.listLabel' | transloco">
              @for (v of shown(); track v.file) {
                <li hlmCard class="gap-0 overflow-hidden p-0 transition-colors" [class.bg-accent]="current()?.file === v.file"
                    [class.border-primary]="current()?.file === v.file" [class.opacity-70]="v.state === 'Hidden'">
                  <button type="button" class="hover:bg-accent flex w-full cursor-pointer items-start gap-3 p-3 text-left transition-colors"
                          [attr.aria-current]="current()?.file === v.file ? 'true' : null" (click)="play(v)">
                    <ng-icon name="lucideCirclePlay" class="text-primary mt-0.5 shrink-0" size="20" />
                    <span class="flex min-w-0 flex-col gap-0.5">
                      <span class="font-medium">{{ v.title }}</span>
                      @if (v.description) { <span class="text-muted-foreground line-clamp-2 text-xs">{{ v.description }}</span> }
                      <span class="text-muted-foreground text-xs">{{ meta(v) }}</span>
                      @if (v.state === 'Hidden') {
                        <span class="flex items-center gap-1 text-xs font-medium text-amber-700 dark:text-amber-400">
                          <ng-icon name="lucideEyeOff" />{{ 'help.videos.hidden' | transloco }}
                        </span>
                      }
                      @if (canManage()) {
                        <!-- Admins: where it comes from and who may watch -->
                        <span class="text-muted-foreground text-[11px]">
                          {{ ('help.videos.source' + (v.source ?? 'Release')) | transloco }}@if (v.edited) { · {{ 'help.videos.edited' | transloco }} }
                          · {{ v.roles?.length ? ('help.videos.onlyRoles' | transloco: { roles: v.roles!.join(', ') }) : ('help.videos.everyone' | transloco) }}
                        </span>
                      }
                    </span>
                  </button>
                  @if (canManage()) {
                    <!-- Admins: hide / show again, delete from the list -->
                    <div class="flex flex-wrap gap-1 border-t px-2 py-1">
                      <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 lg:h-8" [disabled]="busy() === v.file"
                              (click)="editing.set(v)"><ng-icon name="lucidePencil" />{{ 'help.videos.edit' | transloco }}</button>
                      @if (v.state === 'Hidden') {
                        <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 lg:h-8" [disabled]="busy() === v.file"
                                (click)="setState(v, null)"><ng-icon name="lucideEye" />{{ 'help.videos.show' | transloco }}</button>
                      } @else {
                        <button hlmBtn variant="ghost" size="sm" type="button" class="h-11 lg:h-8" [disabled]="busy() === v.file"
                                [title]="'help.videos.hideHint' | transloco" (click)="setState(v, 'Hidden')"><ng-icon name="lucideEyeOff" />{{ 'help.videos.hide' | transloco }}</button>
                      }
                      <button hlmBtn variant="ghost" size="sm" type="button" class="text-destructive h-11 lg:h-8" [disabled]="busy() === v.file"
                              (click)="remove(v)"><ng-icon name="lucideTrash2" />{{ 'help.videos.delete' | transloco }}</button>
                    </div>
                  }
                </li>
              }
            </ul>
            @if (canManage() && deleted().length) {
              <details class="rounded-md border px-3 py-2 text-sm">
                <summary class="text-muted-foreground cursor-pointer">{{ 'help.videos.deletedTitle' | transloco: { count: deleted().length } }}</summary>
                <p class="text-muted-foreground mt-2 text-xs">{{ 'help.videos.deletedHint' | transloco }}</p>
                <ul class="mt-2 flex flex-col gap-1" role="list">
                  @for (v of deleted(); track v.file) {
                    <li class="flex items-center justify-between gap-2">
                      <span class="min-w-0">
                        <span class="block truncate">{{ v.title }}</span>
                        @if (v.stateBy) {
                          <span class="text-muted-foreground block text-xs">{{ 'help.videos.deletedBy' | transloco: { user: v.stateBy, date: (v.stateUtc | date: 'mediumDate') } }}</span>
                        }
                      </span>
                      <span class="flex shrink-0 gap-1">
                        <button hlmBtn variant="outline" size="sm" type="button" class="h-11 lg:h-8" [disabled]="busy() === v.file"
                                (click)="setState(v, null)"><ng-icon name="lucideRotateCcw" />{{ 'help.videos.restore' | transloco }}</button>
                        @if (v.source === 'Uploaded') {
                          <button hlmBtn variant="ghost" size="sm" type="button" class="text-destructive h-11 lg:h-8" [disabled]="busy() === v.file"
                                  (click)="deleteForGood(v)">{{ 'help.videos.deleteForGood' | transloco }}</button>
                        }
                      </span>
                    </li>
                  }
                </ul>
              </details>
            }
          </div>
        </div>
      }
      @if (canManage()) {
        <app-video-edit-dialog [open]="editing() !== null" [video]="editingVideo()" (closed)="editing.set(null)" (saved)="onSaved($event)" />
      }
    </section>
  `,
})
export class VideoTutorials implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly api = `${environment.apiBaseUrl}/api/v1/training-videos`;

  readonly loading = signal(true);
  readonly forbidden = signal(false);
  readonly failed = signal(false);
  readonly videos = signal<TrainingVideo[]>([]);
  readonly canManage = signal(false);
  /** In the list: everything not deleted (Admins also see hidden ones, marked). */
  readonly shown = computed(() => this.videos().filter((v) => v.state !== 'Deleted'));
  /** Admins: deleted from the list, to restore. */
  readonly deleted = computed(() => this.videos().filter((v) => v.state === 'Deleted'));
  /** The video whose state is being saved. */
  readonly busy = signal<string | null>(null);
  /** The Add / Edit dialog: 'new', the video being edited, or null (closed). */
  readonly editing = signal<'new' | TrainingVideo | null>(null);
  readonly editingVideo = computed(() => { const e = this.editing(); return e && e !== 'new' ? e : null; });
  readonly current = signal<TrainingVideo | null>(null);
  readonly videoUrl = signal<string | null>(null);
  /** Download progress in percent while the chosen video loads; null when not loading. */
  readonly downloading = signal<number | null>(null);
  readonly playFailed = signal(false);

  private download?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.release());
  }

  ngOnInit(): void {
    this.load();
  }

  /** Loads the list; <paramref name="select"/> picks that video afterwards (else the current one, else the first). */
  private load(select: string | null = null): void {
    this.http.get<TrainingVideoList>(this.api).subscribe({
      next: (list) => {
        this.videos.set(list.videos);
        this.canManage.set(!!list.canManage);
        this.loading.set(false);
        const want = select ?? this.current()?.file ?? null;
        const pick = this.shown().find((v) => v.file === want) ?? this.shown()[0];
        if (!pick) {
          this.release();
          this.current.set(null);
        } else if (pick.file !== this.current()?.file || select) {
          this.release();
          this.current.set(null);
          this.play(pick, false);
        } else {
          this.current.set(pick);   // same video: fresh title and description
        }
      },
      error: (err: HttpErrorResponse) => {
        this.forbidden.set(err.status === 403);
        this.failed.set(err.status !== 403);
        this.loading.set(false);
      },
    });
  }

  meta(v: TrainingVideo): string {
    const mb = `${(v.sizeBytes / 1024 / 1024).toFixed(0)} MB`;
    return v.durationSeconds ? `${formatDuration(v.durationSeconds)} · ${mb}` : mb;
  }

  /** Loads the video (with progress) and plays it; autoplay only after a click. */
  play(v: TrainingVideo, start = true): void {
    if (this.current()?.file === v.file && (this.videoUrl() || this.downloading() !== null)) return;
    this.release();
    this.current.set(v);
    this.playFailed.set(false);
    if (!start) return;
    this.downloading.set(0);
    this.download = this.http
      .get(`${this.api}/${encodeURIComponent(v.file)}`, { responseType: 'blob', observe: 'events', reportProgress: true })
      .subscribe({
        next: (e) => {
          if (e.type === HttpEventType.DownloadProgress) {
            const total = e.total || v.sizeBytes;
            this.downloading.set(total ? Math.min(99, Math.round((e.loaded / total) * 100)) : 0);
          } else if (e.type === HttpEventType.Response && e.body) {
            this.downloading.set(null);
            this.videoUrl.set(URL.createObjectURL(e.body));
          }
        },
        error: () => {
          this.downloading.set(null);
          this.playFailed.set(true);
        },
      });
  }

  /** After Add / Edit: the list again, with the saved video chosen (a replaced file is downloaded afresh on Play). */
  onSaved(file: string | null): void {
    this.editing.set(null);
    this.load(file);
  }

  /** An added video, gone for good: its file and everything kept for it (release videos cannot). */
  async deleteForGood(v: TrainingVideo): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('help.videos.deleteForGood'),
      message: this.transloco.translate('help.videos.deleteForGoodMessage', { title: v.title }),
      confirmLabel: this.transloco.translate('help.videos.deleteForGood'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.busy.set(v.file);
    this.http.delete<void>(`${this.api}/${encodeURIComponent(v.file)}`).subscribe({
      next: () => {
        this.busy.set(null);
        this.videos.update((list) => list.filter((x) => x.file !== v.file));
        toast.success(this.transloco.translate('help.videos.deletedForGoodDone', { title: v.title }));
      },
      error: () => {
        this.busy.set(null);
        toast.error(this.transloco.translate('help.videos.stateFailed'));
      },
    });
  }

  /** Deletes the video from the list after asking (Admins can restore it under Deleted videos). */
  async remove(v: TrainingVideo): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('help.videos.deleteTitle'),
      message: this.transloco.translate('help.videos.deleteMessage', { title: v.title }),
      confirmLabel: this.transloco.translate('help.videos.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (ok) this.setState(v, 'Deleted');
  }

  /** Hidden, Deleted, or null = shown again. */
  setState(v: TrainingVideo, state: 'Hidden' | 'Deleted' | null): void {
    this.busy.set(v.file);
    this.http.put<void>(`${this.api}/${encodeURIComponent(v.file)}/state`, { state }).subscribe({
      next: () => {
        this.busy.set(null);
        this.videos.update((list) => list.map((x) => (x.file === v.file ? { ...x, state, stateBy: null, stateUtc: null } : x)));
        if (state === 'Deleted' && this.current()?.file === v.file) {
          this.release();
          this.current.set(null);
          const next = this.shown()[0];
          if (next) this.play(next, false);
        } else if (!this.current() && this.shown().length) {
          this.play(this.shown()[0], false);
        }
        const key = state === 'Hidden' ? 'help.videos.hiddenDone' : state === 'Deleted' ? 'help.videos.deletedDone' : 'help.videos.shownDone';
        toast.success(this.transloco.translate(key, { title: v.title }));
      },
      error: () => {
        this.busy.set(null);
        toast.error(this.transloco.translate('help.videos.stateFailed'));
      },
    });
  }

  private release(): void {
    this.download?.unsubscribe();
    this.download = undefined;
    this.downloading.set(null);
    const url = this.videoUrl();
    if (url) URL.revokeObjectURL(url);
    this.videoUrl.set(null);
  }
}
