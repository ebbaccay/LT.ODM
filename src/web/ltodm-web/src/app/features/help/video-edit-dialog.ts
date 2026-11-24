import { HttpClient, HttpErrorResponse, HttpEvent, HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { environment } from '@env/environment';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { Modal } from '@tms/shared/ui/modal';
import { Observable } from 'rxjs';
import type { TrainingVideo } from './video-tutorials';

interface RoleOption {
  name: string;
  displayName: string | null;
}

/** Largest video the API takes (TrainingVideoCatalog.MaxUploadBytes). */
const MAX_BYTES = 100 * 1024 * 1024;

/** The length of a chosen video file in whole seconds, read by the browser (null when it cannot tell). */
function measureDuration(file: File): Promise<number | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const video = document.createElement('video');
    const done = (value: number | null) => {
      URL.revokeObjectURL(url);
      resolve(value);
    };
    video.preload = 'metadata';
    video.onloadedmetadata = () => done(Number.isFinite(video.duration) && video.duration > 0 ? Math.round(video.duration) : null);
    video.onerror = () => done(null);
    video.src = url;
  });
}

/**
 * Admins: add a video (video null) or edit one: title, description, who may watch (no role ticked = everyone who may open
 * the page), and a new video file (required when adding; on an existing video it replaces the file and keeps the
 * details). On a release video, Use original details / Use the original file undo the changes made on the page.
 * Emits saved when something changed (the page reloads the list).
 */
@Component({
  selector: 'app-video-edit-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal, HlmButtonImports, HlmCheckboxImports, HlmInputImports, HlmLabelImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" size="lg" [heading]="(video() ? 'help.videos.editTitle' : 'help.videos.addTitle') | transloco" (closed)="close()">
      @let e = errors();
      <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="tv-file">{{ (video() ? 'help.videos.replaceFile' : 'help.videos.file') | transloco }}</label>
          <input #fileInput hlmInput id="tv-file" type="file" accept="video/mp4,.mp4" class="h-11 pt-2 lg:h-9 lg:pt-1.5" [disabled]="saving()"
                 (change)="pickFile($any($event.target).files?.[0] ?? null)" />
          <p class="text-muted-foreground text-xs">{{ (video() ? 'help.videos.replaceHint' : 'help.videos.fileHint') | transloco }}</p>
          @if (e['file']) { <p class="text-destructive text-xs">{{ e['file'] }}</p> }
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="tv-title">{{ 'help.videos.titleLabel' | transloco }}</label>
          <input hlmInput id="tv-title" class="h-11 lg:h-9" maxlength="200" autocomplete="off" [value]="title()" (input)="title.set($any($event.target).value)" />
          @if (e['title']) { <p class="text-destructive text-xs">{{ e['title'] }}</p> }
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="tv-desc">{{ 'help.videos.descriptionLabel' | transloco }}</label>
          <textarea hlmTextarea id="tv-desc" rows="3" maxlength="1000" [value]="description()" (input)="description.set($any($event.target).value)"></textarea>
          @if (e['description']) { <p class="text-destructive text-xs">{{ e['description'] }}</p> }
        </div>
        <fieldset class="flex flex-col gap-2">
          <legend class="mb-1 text-sm font-medium">{{ 'help.videos.whoLabel' | transloco }}</legend>
          <p class="text-muted-foreground text-xs">{{ 'help.videos.whoHint' | transloco }}</p>
          <div class="flex flex-wrap gap-x-5 gap-y-2">
            @for (r of roleOptions(); track r.name) {
              <label class="flex min-h-9 items-center gap-2 text-sm" [for]="'tv-role-' + r.name">
                <hlm-checkbox [inputId]="'tv-role-' + r.name" [checked]="roles().includes(r.name)" (checkedChange)="toggleRole(r.name)" />
                {{ r.displayName || r.name }}
              </label>
            }
          </div>
        </fieldset>
        @if (progress() !== null) {
          <div class="flex flex-col gap-1" role="status">
            <span class="text-xs">{{ 'help.videos.uploading' | transloco: { percent: progress() } }}</span>
            <div class="bg-muted h-2 w-full overflow-hidden rounded-full"><div class="bg-primary h-full transition-[width]" [style.width.%]="progress()"></div></div>
          </div>
        }
        @if (video(); as v) {
          @if (v.edited || v.source === 'Replaced') {
            <div class="flex flex-wrap gap-2 border-t pt-3">
              @if (v.edited) {
                <button hlmBtn variant="outline" size="sm" type="button" class="h-11 lg:h-8" [disabled]="saving()" (click)="resetDetails()">{{ 'help.videos.useOriginalDetails' | transloco }}</button>
              }
              @if (v.source === 'Replaced') {
                <button hlmBtn variant="outline" size="sm" type="button" class="h-11 lg:h-8" [disabled]="saving()" (click)="undoReplace()">{{ 'help.videos.useOriginalFile' | transloco }}</button>
              }
            </div>
          }
        }
        <button type="submit" class="hidden"></button>
      </form>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="close()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ (video() ? 'actions.save' : 'help.videos.add') | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class VideoEditDialog {
  private readonly http = inject(HttpClient);
  private readonly transloco = inject(TranslocoService);
  private readonly api = `${environment.apiBaseUrl}/api/v1/training-videos`;

  readonly open = input(false);
  /** The video to edit; null = a new one. */
  readonly video = input<TrainingVideo | null>(null);
  readonly closed = output<void>();
  readonly saved = output<string | null>();
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  readonly title = signal('');
  readonly description = signal('');
  readonly roles = signal<string[]>([]);
  readonly roleOptions = signal<RoleOption[]>([]);
  readonly file = signal<File | null>(null);
  readonly duration = signal<number | null>(null);
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);
  /** Upload progress in percent; null when not uploading. */
  readonly progress = signal<number | null>(null);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const v = this.video();
      this.title.set(v?.title ?? '');
      this.description.set(v?.description ?? '');
      this.roles.set([...(v?.roles ?? [])]);
      this.file.set(null);
      this.duration.set(null);
      this.errors.set({});
      this.progress.set(null);
      // The browser keeps the last file picked: clear it, or picking the same file again would not count as a change.
      const box = untracked(() => this.fileInput()?.nativeElement);
      if (box) box.value = '';
      if (!untracked(() => this.roleOptions().length)) {
        this.http.get<RoleOption[]>(`${environment.apiBaseUrl}/api/v1/admin/roles`).subscribe({ next: (r) => this.roleOptions.set(r), error: () => this.roleOptions.set([]) });
      }
    });
  }

  close(): void {
    if (!this.saving()) this.closed.emit();
  }

  async pickFile(file: File | null): Promise<void> {
    this.errors.update((e) => ({ ...e, file: '' }));
    this.file.set(file);
    this.duration.set(null);
    if (!file) return;
    if (file.size > MAX_BYTES) {
      this.errors.update((e) => ({ ...e, file: this.transloco.translate('help.videos.tooBig') }));
      return;
    }
    if (!this.title().trim() && !this.video()) this.title.set(file.name.replace(/\.mp4$/i, '').replace(/[-_]+/g, ' ').trim());
    this.duration.set(await measureDuration(file));
  }

  toggleRole(name: string): void {
    this.roles.update((list) => (list.includes(name) ? list.filter((r) => r !== name) : [...list, name]));
  }

  save(): void {
    const errors: Record<string, string> = {};
    if (!this.title().trim()) errors['title'] = this.transloco.translate('help.videos.titleRequired');
    const v = this.video();
    if (!v && !this.file()) errors['file'] = this.transloco.translate('help.videos.fileRequired');
    if (this.file() && this.file()!.size > MAX_BYTES) errors['file'] = this.transloco.translate('help.videos.tooBig');
    this.errors.set(errors);
    if (Object.keys(errors).length) return;

    this.saving.set(true);
    if (!v) {
      const form = this.form();
      form.append('title', this.title().trim());
      form.append('description', this.description().trim());
      form.append('roles', this.roles().join(','));
      this.upload(this.http.post<{ file: string }>(this.api, form, { observe: 'events', reportProgress: true }), (body) => this.done('help.videos.addedDone', body?.file ?? null));
      return;
    }
    const body = { title: this.title().trim(), description: this.description().trim(), roles: this.roles(), durationSeconds: null };
    this.http.put<void>(`${this.api}/${encodeURIComponent(v.file)}`, body).subscribe({
      next: () => {
        if (!this.file()) {
          this.done('help.videos.savedDone', v.file);
          return;
        }
        this.upload(this.http.post<void>(`${this.api}/${encodeURIComponent(v.file)}/file`, this.form(), { observe: 'events', reportProgress: true }),
          () => this.done('help.videos.replacedDone', v.file));
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  resetDetails(): void {
    const v = this.video();
    if (!v) return;
    this.saving.set(true);
    this.http.delete<void>(`${this.api}/${encodeURIComponent(v.file)}/details`).subscribe({
      next: () => this.done('help.videos.detailsResetDone', v.file),
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  undoReplace(): void {
    const v = this.video();
    if (!v) return;
    this.saving.set(true);
    this.http.delete<void>(`${this.api}/${encodeURIComponent(v.file)}/file`).subscribe({
      next: () => this.done('help.videos.fileResetDone', v.file),
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  /** The chosen file and its measured length, as the upload endpoints take them. */
  private form(): FormData {
    const form = new FormData();
    const file = this.file();
    if (file) form.append('file', file, file.name);
    const seconds = this.duration();
    if (seconds) form.append('durationSeconds', String(seconds));
    return form;
  }

  private upload<T>(request: Observable<HttpEvent<T>>, next: (body: T | null) => void): void {
    this.progress.set(0);
    request.subscribe({
      next: (e) => {
        if (e.type === HttpEventType.UploadProgress) this.progress.set(e.total ? Math.min(99, Math.round((e.loaded / e.total) * 100)) : 0);
        else if (e.type === HttpEventType.Response) {
          this.progress.set(null);
          next(e.body);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.progress.set(null);
        this.fail(err);
      },
    });
  }

  private done(messageKey: string, file: string | null): void {
    this.saving.set(false);
    toast.success(this.transloco.translate(messageKey, { title: this.title().trim() }));
    this.saved.emit(file);
  }

  /** Field errors from the API go next to their field; anything else is a toast. */
  private fail(e: HttpErrorResponse): void {
    this.saving.set(false);
    const fields = (e.error?.errors ?? {}) as Record<string, string[]>;
    const mapped: Record<string, string> = {};
    for (const [k, v] of Object.entries(fields)) mapped[k.charAt(0).toLowerCase() + k.slice(1)] = v?.[0] ?? '';
    if (Object.keys(mapped).length) this.errors.set(mapped);
    else toast.error(this.transloco.translate(e.status === 413 ? 'help.videos.tooBig' : 'help.videos.stateFailed'));
  }
}
