import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCloud, lucideServer, lucideTriangleAlert } from '@ng-icons/lucide';
import { catchError, of } from 'rxjs';
import { AiProviderInfo, AiStudioService } from './ai-studio.service';

/**
 * Says which AI service a page uses and whether the data sent leaves LT (cloud) or stays on the company network
 * (in-house server). Shown at the top of every AI Studio page, so nobody sends style data to a cloud service unaware.
 */
@Component({
  selector: 'app-ai-provider-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideCloud, lucideServer, lucideTriangleAlert })],
  template: `
    @for (p of providers(); track p.kind) {
      @if (!p.info.isConfigured) {
        <p class="text-muted-foreground flex items-start gap-2 rounded-md border border-dashed px-3 py-2 text-xs">
          <ng-icon name="lucideTriangleAlert" class="mt-0.5 shrink-0 text-amber-500" />
          <span>{{ (p.kind === 'image' ? 'ai.provider.imageNotSetUp' : 'ai.provider.notSetUp') | transloco }}</span>
        </p>
      } @else if (p.info.leavesNetwork) {
        <p class="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-300">
          <ng-icon name="lucideCloud" class="mt-0.5 shrink-0" />
          <span>
            <strong>{{ (p.kind === 'image' ? 'ai.provider.imageCloud' : 'ai.provider.cloud') | transloco: { provider: p.info.provider, model: p.info.model } }}</strong>
            {{ 'ai.provider.cloudHint' | transloco }}
          </span>
        </p>
      } @else {
        <p class="flex items-start gap-2 rounded-md border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-800 dark:text-emerald-300">
          <ng-icon name="lucideServer" class="mt-0.5 shrink-0" />
          <span>{{ (p.kind === 'image' ? 'ai.provider.imageInHouse' : 'ai.provider.inHouse') | transloco: { model: p.info.model } }}</span>
        </p>
      }
    }
  `,
  host: { class: 'flex flex-col gap-2' },
})
export class AiProviderBanner {
  private readonly svc = inject(AiStudioService);

  /** Which providers the page uses. */
  readonly uses = input<'text' | 'image' | 'both'>('text');

  private readonly status = toSignal(this.svc.status().pipe(catchError(() => of(null))), { initialValue: null });

  readonly providers = computed(() => {
    const s = this.status();
    if (!s) return [];
    const list: { kind: 'text' | 'image'; info: AiProviderInfo }[] = [];
    if (this.uses() !== 'image') list.push({ kind: 'text', info: s.text });
    if (this.uses() !== 'text') list.push({ kind: 'image', info: s.image });
    return list;
  });
}
