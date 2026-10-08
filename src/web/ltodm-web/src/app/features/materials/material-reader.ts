import { DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideSparkles, lucideSquare, lucideTriangleAlert, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/auth/auth.service';
import { AiProviderBanner } from '../ai-studio/ai-provider-banner';
import { StyleLibraryService, StyleLookups } from '../style-library/style-library.service';
import { MaterialSpec, MaterialSpecPage, MaterialsService, SpecState } from './materials.service';

const PAGE = 50;
/** Pause between calls in "Read all": stays under the per-user AI limit (10 a minute) and free-tier rate limits. */
const PAUSE_MS = 7000;

/**
 * Materials > Description reader: the Text job reads material descriptions (25 per call) into fibre composition,
 * construction, weight, width and a suggested section. Readings are Pending until someone accepts them; the description
 * itself never changes. Admins and merchandisers read and review; others look.
 */
@Component({
  selector: 'app-material-reader',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, PercentPipe, RouterLink, NgIcon, AiProviderBanner, HlmButtonImports, HlmCardImports, HlmNativeSelectImports, HlmSkeletonImports,
    HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [provideIcons({ lucideCheck, lucideSparkles, lucideSquare, lucideTriangleAlert, lucideX })],
  templateUrl: './material-reader.html',
  // Spacing between the AI banner, the hint, the reading card, the review bar and the list.
  host: { class: 'flex flex-col gap-4' },
})
export class MaterialReader implements OnInit {
  private readonly svc = inject(MaterialsService);
  private readonly styles = inject(StyleLibraryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly lookups = input<StyleLookups | null>(null);
  readonly canEdit = this.styles.canEdit;

  readonly contentClass = signal('');
  readonly status = signal<SpecState | ''>('');
  readonly page = signal<MaterialSpecPage | null>(null);
  readonly items = signal<MaterialSpec[]>([]);
  readonly loading = signal(false);
  readonly selected = signal<ReadonlySet<number>>(new Set());
  readonly reviewing = signal(false);

  // Reading
  readonly reading = signal(false);
  readonly readAll = signal(false);
  readonly progress = signal<{ read: number; remaining: number } | null>(null);
  private stopRequested = false;

  readonly states: (SpecState | '')[] = ['', 'Pending', 'Unread', 'Accepted', 'Rejected', 'Outdated'];
  readonly toRead = computed(() => (this.page()?.counts.unread ?? 0) + (this.page()?.counts.outdated ?? 0));
  readonly selectable = computed(() => this.items().filter((i) => i.state === 'Pending'));
  readonly highConfidence = computed(() => this.selectable().filter((i) => (i.confidence ?? 0) >= 0.9 && !i.notes));

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => (this.stopRequested = true));
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      // Fabrics are where a reading helps most: start there.
      if (!p.has('contentClass')) {
        void this.router.navigate([], { queryParams: { contentClass: 'FAB' }, queryParamsHandling: 'merge', replaceUrl: true });
        return;
      }
      this.contentClass.set(p.get('contentClass') ?? '');
      this.status.set((p.get('specStatus') as SpecState) ?? '');
      this.load();
    });
  }

  setFilter(key: 'contentClass' | 'specStatus', value: string): void {
    void this.router.navigate([], { queryParams: { [key]: value }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  load(more = false): void {
    this.loading.set(true);
    if (!more) this.selected.set(new Set());
    this.svc
      .specs({ contentClass: this.contentClass() || null, status: this.status() || null, skip: more ? this.items().length : 0, take: PAGE })
      .subscribe({
        next: (p) => {
          this.page.set(p);
          this.items.set(more ? [...this.items(), ...p.items] : p.items);
          this.loading.set(false);
        },
        error: (e) => {
          this.loading.set(false);
          toast.error(problemMessage(e));
        },
      });
  }

  // ── Reading ────────────────────────────────────────────────

  async read(all: boolean): Promise<void> {
    if (this.reading()) return;
    this.reading.set(true);
    this.readAll.set(all);
    this.stopRequested = false;
    let total = 0;
    try {
      for (;;) {
        const r = await firstValueFrom(this.svc.readSpecs(this.contentClass() || null, null));
        total += r.read;
        this.progress.set({ read: total, remaining: r.remaining });
        if (!all || r.read === 0 || r.remaining === 0 || this.stopRequested) break;
        this.load();
        await new Promise((resolve) => setTimeout(resolve, PAUSE_MS));
        if (this.stopRequested) break;
      }
      toast.success(this.transloco.translate('materials.reader.readDone', { count: total }));
    } catch (e) {
      toast.error(this.transloco.translate('ai.failed'), { description: problemMessage(e) });
    } finally {
      this.reading.set(false);
      this.readAll.set(false);
      this.load();
    }
  }

  stop(): void {
    this.stopRequested = true;
  }

  // ── Review ─────────────────────────────────────────────────

  toggle(id: number): void {
    this.selected.update((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  selectHighConfidence(): void {
    this.selected.set(new Set(this.highConfidence().map((i) => i.materialId)));
  }

  review(ids: number[], status: 'Accepted' | 'Rejected' | 'Pending'): void {
    if (!ids.length || this.reviewing()) return;
    this.reviewing.set(true);
    this.svc.reviewSpecs(ids, status).subscribe({
      next: (r) => {
        this.reviewing.set(false);
        toast.success(this.transloco.translate('materials.reader.reviewed.' + status, { count: r.updated }));
        this.load();
      },
      error: (e) => {
        this.reviewing.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  selectedIds = () => [...this.selected()];
  stateClass = (s: SpecState) =>
    ({
      Unread: 'bg-muted text-muted-foreground',
      Pending: 'bg-amber-500/15 text-amber-700 dark:text-amber-400',
      Accepted: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400',
      Rejected: 'bg-red-500/15 text-red-700 dark:text-red-400',
      Outdated: 'bg-violet-500/15 text-violet-700 dark:text-violet-400',
    })[s];
  confidenceClass = (c: number | null) =>
    c === null ? '' : c >= 0.9 ? 'text-emerald-700 dark:text-emerald-400' : c >= 0.7 ? 'text-amber-700 dark:text-amber-400' : 'text-red-700 dark:text-red-400';
  countFor = (s: SpecState | '') => {
    const c = this.page()?.counts;
    if (!c) return 0;
    return s === '' ? c.total : c[(s.charAt(0).toLowerCase() + s.slice(1)) as keyof typeof c];
  };
}
