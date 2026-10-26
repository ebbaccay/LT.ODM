import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideImagePlus, lucideRefreshCw, lucideSparkles, lucideTextCursorInput, lucideTrash2, lucideWandSparkles, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { problemMessage } from '../../core/auth/auth.service';
import { StyleLibraryService, StyleListItem, styleError } from '../style-library/style-library.service';
import { AiProviderBanner } from './ai-provider-banner';
import { AiStudioService, RenderBrief, StyleRender } from './ai-studio.service';
import { AiTaskRunning } from './ai-task-running';
import { AiTasksSession } from './ai-tasks.session';
import { StylePicker } from './style-picker';

const MAX_PROMPT = 2000;

/**
 * AI Studio > Style render: a product picture drawn from the style's own data (garment, fabrics, the colorway's
 * colours, visible trims) and, when the image model accepts one, its uploaded sketch as the silhouette guide.
 * The page shows exactly what is sent; the prompt can be edited or rewritten by the text model before drawing.
 * Renders are kept apart from real photos; an editor can still use one as the style's photo.
 * Drawing can take minutes on an in-house server: it runs in AiTasksSession, so Minimise (or leaving) does not stop it.
 * Address: /ai/render?styleId=..&colorwayId=..
 */
@Component({
  selector: 'app-style-render',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, RouterLink, NgIcon, AuthImgDirective, Modal, AiProviderBanner, AiTaskRunning, StylePicker, HlmBadgeImports, HlmButtonImports, HlmCardImports,
    HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideImage, lucideImagePlus, lucideRefreshCw, lucideSparkles, lucideTextCursorInput, lucideTrash2, lucideWandSparkles, lucideX })],
  templateUrl: './style-render.html',
})
export class StyleRenderPage implements OnInit {
  private readonly svc = inject(AiStudioService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tasks = inject(AiTasksSession);

  readonly canEdit = this.styles.canEdit;
  readonly maxPrompt = MAX_PROMPT;

  readonly styleId = signal<number | null>(null);
  readonly brief = signal<RenderBrief | null>(null);
  readonly loading = signal(false);
  readonly notFound = signal(false);
  readonly prompt = signal('');
  readonly useSketch = signal(true);
  readonly preview = signal<StyleRender | null>(null);

  /** Rewrite with AI runs as a task too: it can take a while on an in-house server, and its words are followed. */
  readonly promptTask = this.tasks.watch<{ prompt: string }>(computed(() => (this.styleId() ? `prompt:${this.styleId()}` : null)));
  readonly writing = computed(() => this.promptTask()?.status === 'running');
  /** The rewrite already put into the box (so editing it afterwards is not undone). */
  private appliedPrompt = 0;

  /** One render at a time per style, kept by the session while the user is elsewhere. */
  readonly renderTask = this.tasks.watch<StyleRender>(computed(() => (this.styleId() ? `render:${this.styleId()}` : null)));
  readonly rendering = computed(() => this.renderTask()?.status === 'running');

  constructor() {
    // A finished rewrite fills the description box once.
    effect(() => {
      const t = this.promptTask();
      if (t?.status === 'done' && t.result && t.id !== this.appliedPrompt) {
        this.appliedPrompt = t.id;
        untracked(() => this.prompt.set(t.result!.prompt));
      }
    });
    // A render that finished (here or while away) joins the list unless the list already has it from the server.
    effect(() => {
      const t = this.renderTask();
      const b = this.brief();
      const r = t?.status === 'done' ? t.result : null;
      if (r && b && b.style.styleId === r.styleId && !b.renders.some((x) => x.renderId === r.renderId))
        untracked(() => this.brief.set({ ...b, renders: [r, ...b.renders] }));
    });
  }

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      this.styleId.set(Number(p.get('styleId')) || null);
      this.load(Number(p.get('colorwayId')) || null);
    });
  }

  load(colorwayId: number | null): void {
    const id = this.styleId();
    this.notFound.set(false);
    if (!id) {
      this.brief.set(null);
      return;
    }
    this.loading.set(true);
    this.svc.renderBrief(id, colorwayId).subscribe({
      next: (b) => {
        this.brief.set(b);
        // Back from elsewhere after a rewrite finished: keep the rewritten description, not the default.
        const rewritten = this.promptTask();
        this.prompt.set(rewritten?.status === 'done' && rewritten.result ? rewritten.result.prompt : b.prompt);
        this.useSketch.set(b.sketchUsable);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        if (e instanceof HttpErrorResponse && e.status === 404) this.notFound.set(true);
        else toast.error(problemMessage(e));
      },
    });
  }

  pickStyle(s: StyleListItem): void {
    void this.router.navigate([], { queryParams: { styleId: s.styleId, colorwayId: null } });
  }

  pickColorway(value: string): void {
    void this.router.navigate([], { queryParams: { colorwayId: value || null }, queryParamsHandling: 'merge' });
  }

  clear(): void {
    void this.router.navigate([], { queryParams: {} });
  }

  garmentText = (b: RenderBrief) => [b.facts.gender, b.facts.garment].filter((x) => !!x).join(' ');

  /** Shape words read from the sketch, plus "front and back" when it has both views. */
  shapeText = (b: RenderBrief) =>
    [...(b.facts.shape ?? []), ...(b.facts.frontAndBack ? [this.transloco.translate('ai.render.frontAndBack')] : [])].join(', ');

  resetPrompt(): void {
    this.prompt.set(this.brief()?.prompt ?? '');
  }

  /** The text model rewrites the prompt from the same facts (it never sees more than the page shows). */
  rewrite(): void {
    const b = this.brief();
    if (!b || this.writing()) return;
    this.tasks.run('renderPrompt', `prompt:${b.style.styleId}`, b.style.styleNo,
      (progressId) => this.svc.writePrompt(b.style.styleId, b.colorwayId, progressId), { follow: true });
  }

  generate(): void {
    const b = this.brief();
    if (!b || this.rendering()) return;
    const useSketch = this.useSketch() && b.sketchUsable;
    this.tasks.run('render', `render:${b.style.styleId}`, b.style.styleNo,
      (taskId) => this.svc.render(b.style.styleId, b.colorwayId, this.prompt().trim(), useSketch, taskId),
      { estimateSeconds: useSketch ? b.estimate?.withSketchSeconds : b.estimate?.withoutSketchSeconds });
  }

  /** Puts a saved render's description back in the box (descriptions typed or rewritten are not kept between visits). */
  useDescription(r: StyleRender): void {
    this.prompt.set(r.prompt);
    toast.success(this.transloco.translate('ai.render.descriptionUsed'));
  }

  async remove(r: StyleRender): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.transloco.translate('ai.render.delete'),
      message: this.transloco.translate('ai.render.deleteMessage'),
      confirmLabel: this.transloco.translate('ai.render.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteRender(r.styleId, r.renderId).subscribe({
      next: () => {
        this.brief.update((x) => (x ? { ...x, renders: x.renders.filter((y) => y.renderId !== r.renderId) } : x));
        if (this.preview()?.renderId === r.renderId) this.preview.set(null);
      },
      error: (e) => toast.error(problemMessage(e)),
    });
  }

  /** Saves the render as the style's photo (the style is re-read so its version is current). */
  async useAsPhoto(r: StyleRender): Promise<void> {
    const b = this.brief();
    if (!b) return;
    if (b.style.imageUrl) {
      const ok = await this.confirmDlg.confirm({
        title: this.transloco.translate('ai.render.usePhoto'),
        message: this.transloco.translate('ai.render.replacePhotoMessage', { style: b.style.styleNo }),
        confirmLabel: this.transloco.translate('ai.render.usePhoto'),
        cancelLabel: this.transloco.translate('actions.cancel'),
      });
      if (!ok) return;
    }
    this.styles.get(b.style.styleId).subscribe({
      next: (d) => {
        const s = d.style;
        this.styles
          .updateStyle(s.styleId, {
            rowVer: s.rowVer, customerCode: s.customerCode, seasonCode: s.seasonCode, styleNo: s.styleNo, description: s.description,
            modelCode: s.modelCode, modelName: s.modelName, weaveTypeCode: s.weaveTypeCode, productTypeCode: s.productTypeCode, gender: s.gender,
            garmentLeadTimeDays: s.garmentLeadTimeDays, businessUnitCode: s.businessUnitCode, sketchUrl: s.sketchUrl, imageUrl: r.imageUrl,
            isActive: s.isActive,
          })
          .subscribe({
            next: () => {
              this.brief.update((x) => (x ? { ...x, style: { ...x.style, imageUrl: r.imageUrl } } : x));
              toast.success(this.transloco.translate('ai.render.photoSet', { style: s.styleNo }));
            },
            error: (e) => toast.error(styleError(e).message),
          });
      },
      error: (e) => toast.error(styleError(e).message),
    });
  }
}
