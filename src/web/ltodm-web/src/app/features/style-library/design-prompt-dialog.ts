import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucideDownload, lucideFileText, lucideImage, lucideRotateCcw } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { Colorway, DesignPrompt, DesignPromptMode, StyleHeader, StyleLibraryService, safeFileName, saveBlob, styleError } from './style-library.service';

/**
 * Style page > Design prompt: a prompt for outside design tools (StyTrix, Style3D AI, ...) built from the style, its
 * BOM and a colorway (GET /api/v1/styles/:id/design-prompt; no AI call, nothing is sent anywhere). Text to design
 * describes the whole garment; image to design is for use with the style's sketch (or photo), which can be downloaded
 * here to attach in the tool. The text can be edited before it is copied or downloaded.
 */
@Component({
  selector: 'app-design-prompt-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    Modal, NgIcon, AuthImgDirective, HlmButtonImports, HlmLabelImports, HlmNativeSelectImports, HlmSkeletonImports, HlmTextareaImports,
    TranslocoPipe,
  ],
  providers: [provideIcons({ lucideCopy, lucideDownload, lucideFileText, lucideImage, lucideRotateCcw })],
  template: `
    <app-modal [open]="open()" [heading]="'styles.prompt.title' | transloco: { styleNo: style().styleNo }" size="lg" (closed)="closed.emit()">
      <div class="flex flex-col gap-4">
        <p class="text-muted-foreground text-sm">{{ 'styles.prompt.intro' | transloco }}</p>

        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <span hlmLabel id="dp-mode">{{ 'styles.prompt.mode' | transloco }}</span>
            <div class="bg-muted flex rounded-lg p-1" role="radiogroup" aria-labelledby="dp-mode">
              @for (m of modes; track m.id) {
                <button
                  type="button"
                  role="radio"
                  class="flex min-h-11 flex-1 items-center justify-center gap-1.5 rounded-md px-3 text-sm font-medium whitespace-nowrap transition-colors disabled:opacity-50 lg:min-h-8"
                  [class]="mode() === m.id ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'"
                  [attr.aria-checked]="mode() === m.id"
                  [disabled]="m.id === 'image' && result() !== null && !result()!.hasReference"
                  (click)="mode.set(m.id)"
                >
                  <ng-icon [name]="m.icon" />{{ m.label | transloco }}
                </button>
              }
            </div>
            @if (result() && !result()!.hasReference) {
              <p class="text-muted-foreground text-xs">{{ 'styles.prompt.noReference' | transloco }}</p>
            }
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="dp-colorway">{{ 'styles.prompt.colorway' | transloco }}</label>
            <hlm-native-select selectId="dp-colorway" selectClass="h-11 w-full lg:h-9" [value]="colorwayId()?.toString() ?? ''" (valueChange)="colorwayId.set($event ? +$event : null)">
              <option hlmNativeSelectOption value="">{{ 'styles.prompt.allColorways' | transloco }}</option>
              @for (c of colorways(); track c.colorwayId) {
                <option hlmNativeSelectOption [value]="c.colorwayId.toString()">{{ c.colorwayCode }}{{ c.colorwayName ? ' · ' + c.colorwayName : '' }}</option>
              }
            </hlm-native-select>
          </div>
        </div>

        @if (result()?.mode === 'image' && result()!.referenceUrl) {
          @let r = result()!;
          <div class="flex items-center gap-3 rounded-md border p-3">
            <span class="bg-muted flex size-16 shrink-0 items-center justify-center overflow-hidden rounded-md border">
              <img [appAuthSrc]="r.referenceUrl" alt="" class="size-full object-contain" />
            </span>
            <p class="min-w-0 flex-1 text-sm">{{ (r.referenceKind === 'sketch' ? 'styles.prompt.attachSketch' : 'styles.prompt.attachPhoto') | transloco }}</p>
            <button hlmBtn variant="outline" type="button" class="h-11 shrink-0 lg:h-9" [disabled]="downloading()" (click)="downloadReference()">
              <ng-icon name="lucideDownload" />{{ (r.referenceKind === 'sketch' ? 'styles.prompt.downloadSketch' : 'styles.prompt.downloadPhoto') | transloco }}
            </button>
          </div>
        }

        <div class="flex flex-col gap-1.5">
          <div class="flex items-end justify-between gap-2">
            <label hlmLabel for="dp-text">{{ 'styles.prompt.prompt' | transloco }}</label>
            @if (edited()) {
              <button type="button" class="text-primary flex items-center gap-1 text-xs underline-offset-2 hover:underline" (click)="text.set(result()!.prompt)">
                <ng-icon name="lucideRotateCcw" />{{ 'styles.prompt.reset' | transloco }}
              </button>
            }
          </div>
          @if (loading() && !result()) {
            <div hlmSkeleton class="h-44 w-full"></div>
          } @else {
            <textarea hlmTextarea id="dp-text" rows="8" class="text-sm leading-relaxed" [class.opacity-60]="loading()" [value]="text()" (input)="text.set($any($event.target).value)"></textarea>
            <p class="text-muted-foreground text-right text-xs tabular-nums">{{ 'styles.prompt.length' | transloco: { count: text().length } }}</p>
          }
        </div>

        @if (result(); as r) {
          <details class="text-sm">
            <summary class="text-muted-foreground cursor-pointer text-xs">{{ 'styles.prompt.builtFrom' | transloco }}</summary>
            <dl class="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-xs">
              <dt class="text-muted-foreground">{{ 'styles.prompt.factGarment' | transloco }}</dt>
              <dd>{{ ((r.facts.gender ?? '') + ' ' + (r.facts.garment ?? '')).trim() || '—' }}</dd>
              <dt class="text-muted-foreground">{{ 'styles.prompt.factFabrics' | transloco }}</dt>
              <dd>{{ r.facts.fabrics.join('; ') || '—' }}</dd>
              <dt class="text-muted-foreground">{{ 'styles.prompt.factColours' | transloco }}</dt>
              <dd>{{ r.facts.colours.join(', ') || '—' }}</dd>
              <dt class="text-muted-foreground">{{ 'styles.prompt.factDetails' | transloco }}</dt>
              <dd>{{ r.facts.details.join(', ') || '—' }}</dd>
            </dl>
          </details>
        }
      </div>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" [disabled]="!text()" (click)="downloadText()">
          <ng-icon name="lucideDownload" />{{ 'styles.prompt.downloadText' | transloco }}
        </button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="!text()" (click)="copy()">
          <ng-icon name="lucideCopy" />{{ 'styles.prompt.copy' | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class DesignPromptDialog {
  private readonly svc = inject(StyleLibraryService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly style = input.required<StyleHeader>();
  readonly colorways = input<Colorway[]>([]);
  readonly closed = output<void>();

  readonly modes: { id: DesignPromptMode; label: string; icon: string }[] = [
    { id: 'text', label: 'styles.prompt.modeText', icon: 'lucideFileText' },
    { id: 'image', label: 'styles.prompt.modeImage', icon: 'lucideImage' },
  ];

  readonly mode = signal<DesignPromptMode>('text');
  readonly colorwayId = signal<number | null>(null);
  readonly result = signal<DesignPrompt | null>(null);
  /** The prompt as shown (the user may edit it). */
  readonly text = signal('');
  readonly loading = signal(false);
  readonly downloading = signal(false);

  readonly edited = computed(() => !!this.result() && this.text() !== this.result()!.prompt);
  /** Only the latest request may fill the dialog (options can change faster than answers come back). */
  private request = 0;

  constructor() {
    // Fresh start each time the dialog opens: text mode, all colorways.
    effect(() => {
      if (!this.open()) return;
      untracked(() => {
        this.mode.set('text');
        this.colorwayId.set(null);
        this.result.set(null);
        this.text.set('');
      });
    });
    // Rebuild the prompt when the mode or colorway changes (an edited text is replaced).
    effect(() => {
      if (!this.open()) return;
      const styleId = this.style().styleId;
      const mode = this.mode();
      const colorwayId = this.colorwayId();
      untracked(() => this.load(styleId, mode, colorwayId));
    });
  }

  private load(styleId: number, mode: DesignPromptMode, colorwayId: number | null): void {
    const request = ++this.request;
    this.loading.set(true);
    this.svc.designPrompt(styleId, mode, colorwayId).subscribe({
      next: (r) => {
        if (request !== this.request) return;
        this.loading.set(false);
        this.result.set(r);
        this.text.set(r.prompt);
        // Image mode asked for but the style has nothing to attach: the API answered in text mode.
        if (r.mode !== mode) this.mode.set(r.mode);
      },
      error: (e) => {
        if (request !== this.request) return;
        this.loading.set(false);
        toast.error(styleError(e).message);
      },
    });
  }

  async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.text());
      toast.success(this.t('styles.prompt.copied'));
    } catch {
      toast.error(this.t('styles.prompt.copyFailed'));
    }
  }

  downloadText(): void {
    saveBlob(new Blob([this.text()], { type: 'text/plain;charset=utf-8' }), `${safeFileName(this.baseName())}_prompt.txt`);
  }

  async downloadReference(): Promise<void> {
    const r = this.result();
    if (!r?.referenceUrl) return;
    this.downloading.set(true);
    try {
      await this.svc.downloadImage(r.referenceUrl, `${this.style().styleNo}_${r.referenceKind}`);
    } catch {
      toast.error(this.t('styles.image.downloadFailed'));
    } finally {
      this.downloading.set(false);
    }
  }

  /** Style number, plus the colorway code when one is chosen. */
  private baseName(): string {
    const cw = this.colorways().find((c) => c.colorwayId === this.colorwayId());
    return cw ? `${this.style().styleNo}_${cw.colorwayCode}` : this.style().styleNo;
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(key, params);
  }
}
