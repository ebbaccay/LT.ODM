import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideImagePlus, lucideRefreshCw, lucideSparkles, lucideTrash2, lucideWandSparkles, lucideX } from '@ng-icons/lucide';
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
import { StylePicker } from './style-picker';

const MAX_PROMPT = 2000;

/**
 * AI Studio > Style render: a product picture drawn from the style's own data (garment, fabrics, the colorway's
 * colours, visible trims) and, when the image model accepts one, its uploaded sketch as the silhouette guide.
 * The page shows exactly what is sent; the prompt can be edited or rewritten by the text model before drawing.
 * Renders are kept apart from real photos; an editor can still use one as the style's photo.
 * Address: /ai/render?styleId=..&colorwayId=..
 */
@Component({
  selector: 'app-style-render',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, RouterLink, NgIcon, AuthImgDirective, Modal, AiProviderBanner, StylePicker, HlmBadgeImports, HlmButtonImports, HlmCardImports,
    HlmNativeSelectImports, HlmSkeletonImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideImage, lucideImagePlus, lucideRefreshCw, lucideSparkles, lucideTrash2, lucideWandSparkles, lucideX })],
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

  readonly canEdit = this.styles.canEdit;
  readonly maxPrompt = MAX_PROMPT;

  readonly styleId = signal<number | null>(null);
  readonly brief = signal<RenderBrief | null>(null);
  readonly loading = signal(false);
  readonly notFound = signal(false);
  readonly prompt = signal('');
  readonly useSketch = signal(true);
  readonly writing = signal(false);
  readonly rendering = signal(false);
  readonly preview = signal<StyleRender | null>(null);

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
        this.prompt.set(b.prompt);
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

  resetPrompt(): void {
    this.prompt.set(this.brief()?.prompt ?? '');
  }

  /** The text model rewrites the prompt from the same facts (it never sees more than the page shows). */
  rewrite(): void {
    const b = this.brief();
    if (!b || this.writing()) return;
    this.writing.set(true);
    this.svc.writePrompt(b.style.styleId, b.colorwayId).subscribe({
      next: (r) => {
        this.prompt.set(r.prompt);
        this.writing.set(false);
      },
      error: (e) => {
        this.writing.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
  }

  generate(): void {
    const b = this.brief();
    if (!b || this.rendering()) return;
    this.rendering.set(true);
    this.svc.render(b.style.styleId, b.colorwayId, this.prompt().trim(), this.useSketch() && b.sketchUsable).subscribe({
      next: (r) => {
        this.brief.update((x) => (x ? { ...x, renders: [r, ...x.renders] } : x));
        this.rendering.set(false);
        toast.success(this.transloco.translate('ai.render.done'));
      },
      error: (e) => {
        this.rendering.set(false);
        toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
      },
    });
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
