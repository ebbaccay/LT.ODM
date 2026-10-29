import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { environment } from '@env/environment';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideDownload, lucideImage, lucideLeaf, lucideLink2Off, lucideShirt, lucideThumbsDown, lucideThumbsUp } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';

/** A style on the client's page (the line sheet's content: offered colourways, the note for the client). */
interface SharedItem {
  packageItemId: number;
  styleNo: string;
  seasonCode: string;
  modelName: string | null;
  description: string | null;
  facts: string;
  imageFile: string | null;
  leadTimeDays: number | null;
  fabrics: string[];
  trims: string[];
  recycled: string | null;
  colorways: { code: string; name: string | null; imageFile: string | null }[];
  note: string | null;
  answer: { verdict: 'Like' | 'Pass' | null; comment: string | null; name: string | null; sample: boolean; sampleNote: string | null } | null;
}

/** A concept's story page (roadmap 5.22): the text for the client, trend tags, pictures (files), the styles it covers. */
interface SharedStory {
  name: string;
  story: string;
  tags: string | null;
  imageFiles: string[];
  packageItemIds: number[];
}

/** A person's whole answer on a style. */
interface Answer {
  verdict: 'Like' | 'Pass' | null;
  comment: string | null;
  sample: boolean;
  sampleNote: string | null;
}

interface SharedPackage {
  name: string;
  customerName: string;
  seasonCode: string | null;
  description: string | null;
  label: string;
  expiresUtc: string;
  items: SharedItem[];
  stories: SharedStory[] | null;
}

const NAME_KEY = 'ltodm.share.name';

/**
 * A style package opened by the client through its share link (/share/:token), without signing in (roadmap 5.16). Outside
 * the app's layout and menu. The concepts' stories come first (5.22). Per style: Like / Pass, a comment, and Request sample with
 * a note (5.23), saved at once; the client's name is remembered in this browser only. A link that is unknown, revoked or expired
 * shows one message, whatever the reason.
 */
@Component({
  selector: 'app-shared-package-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon, HlmButtonImports, HlmCardImports, HlmInputImports, HlmSkeletonImports, HlmTextareaImports, TranslocoPipe],
  providers: [provideIcons({ lucideCheck, lucideDownload, lucideImage, lucideLeaf, lucideLink2Off, lucideShirt, lucideThumbsDown, lucideThumbsUp })],
  template: `
    <main class="bg-background text-foreground min-h-dvh">
      <div class="mx-auto flex max-w-6xl flex-col gap-5 px-4 py-6 sm:px-6">
        @if (loading()) {
          <div hlmSkeleton class="h-32 w-full"></div>
          <div hlmSkeleton class="h-80 w-full"></div>
        } @else if (gone()) {
          <section hlmCard class="mx-auto mt-16 max-w-md items-center gap-3 px-6 py-12 text-center">
            <ng-icon name="lucideLink2Off" class="text-muted-foreground text-4xl" />
            <h1 class="text-lg font-semibold">{{ 'packages.shared.goneTitle' | transloco }}</h1>
            <p class="text-muted-foreground text-sm">{{ 'packages.shared.goneText' | transloco }}</p>
          </section>
        } @else if (pkg(); as p) {
          <header class="flex flex-col gap-3 border-b pb-5">
            <p class="text-primary text-xs font-semibold tracking-widest uppercase">{{ 'packages.shared.kicker' | transloco }}</p>
            <div class="flex flex-wrap items-end justify-between gap-3">
              <div class="min-w-0">
                <h1 class="text-2xl font-semibold tracking-tight sm:text-3xl">{{ p.name }}</h1>
                <p class="text-muted-foreground">{{ p.customerName }}{{ p.seasonCode ? ' · ' + p.seasonCode : '' }} · {{ 'packages.styleCount' | transloco: { count: p.items.length } }}</p>
              </div>
              <a hlmBtn variant="outline" class="h-11 lg:h-9" [href]="pdfUrl()" download><ng-icon name="lucideDownload" />{{ 'packages.shared.pdf' | transloco }}</a>
            </div>
            @if (p.description) { <p class="max-w-3xl text-sm whitespace-pre-line">{{ p.description }}</p> }
            <div class="flex flex-wrap items-center gap-3">
              <label class="text-muted-foreground text-sm" for="share-name">{{ 'packages.shared.yourName' | transloco }}</label>
              <input hlmInput id="share-name" class="h-11 w-full sm:w-64 lg:h-9" maxlength="100" autocomplete="name" [value]="name()"
                     (input)="setName($any($event.target).value)" (change)="loadAnswers()" />
              <span class="text-muted-foreground text-xs">{{ 'packages.shared.howTo' | transloco }}</span>
            </div>
          </header>

          <!-- The concepts' stories (roadmap 5.22) -->
          @for (st of p.stories ?? []; track st.name) {
            <section hlmCard class="gap-3 p-5">
              <p class="text-primary text-xs font-semibold tracking-widest uppercase">{{ 'packages.shared.story' | transloco }}</p>
              <h2 class="text-xl font-semibold tracking-tight">{{ st.name }}</h2>
              @if (st.tags) { <p class="text-muted-foreground text-sm">{{ st.tags }}</p> }
              <p class="max-w-3xl text-sm whitespace-pre-line">{{ st.story }}</p>
              @if (st.imageFiles.length) {
                <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
                  @for (f of st.imageFiles; track f) {
                    <img [src]="storyImageUrl(f, 640)" alt="" class="bg-muted aspect-square w-full rounded-md border object-contain" loading="lazy" />
                  }
                </div>
              }
              <p class="text-muted-foreground text-xs">{{ 'packages.shared.storyStyles' | transloco: { styles: styleNos(st) } }}</p>
            </section>
          }

          <ul class="grid gap-4 md:grid-cols-2" role="list">
            @for (i of p.items; track i.packageItemId) {
              @let a = answers()[i.packageItemId];
              <li hlmCard class="gap-3 p-4">
                <div class="flex gap-4">
                  <span class="bg-muted flex size-36 shrink-0 items-center justify-center overflow-hidden rounded-md border sm:size-44">
                    @if (i.imageFile) {
                      <img [src]="imageUrl(i.imageFile, 640)" [alt]="i.styleNo" class="size-full object-contain" loading="lazy" />
                    } @else {
                      <ng-icon name="lucideImage" class="text-muted-foreground text-2xl" />
                    }
                  </span>
                  <div class="flex min-w-0 flex-1 flex-col gap-1">
                    <span class="font-mono font-semibold">{{ i.styleNo }}</span>
                    <span class="text-sm">{{ i.modelName || i.description || '—' }}</span>
                    @if (i.facts) { <span class="text-muted-foreground text-xs">{{ i.facts }}</span> }
                    @if (i.leadTimeDays) { <span class="text-muted-foreground text-xs">{{ 'packages.leadTime' | transloco: { days: i.leadTimeDays } }}</span> }
                    @if (i.recycled) {
                      <span class="flex items-center gap-1 text-xs font-medium text-emerald-700 dark:text-emerald-400"><ng-icon name="lucideLeaf" />{{ i.recycled }}</span>
                    }
                  </div>
                </div>
                @if (i.fabrics.length) {
                  <div class="text-xs"><span class="text-muted-foreground">{{ 'packages.fabrics' | transloco }}:</span> {{ i.fabrics.join(' · ') }}</div>
                }
                @if (i.trims.length) {
                  <div class="text-muted-foreground text-xs"><span>{{ 'packages.trims' | transloco }}:</span> {{ i.trims.join(' · ') }}</div>
                }
                @if (i.colorways.length) {
                  <div class="flex flex-wrap gap-1.5">
                    @for (c of i.colorways; track c.code) {
                      <span class="flex items-center gap-1.5 rounded border px-1.5 py-0.5 text-[11px]">
                        @if (c.imageFile) { <img [src]="imageUrl(c.imageFile, 160)" alt="" class="size-5 rounded object-cover" loading="lazy" /> }
                        <span class="font-mono">{{ c.code }}</span>@if (c.name && c.name !== c.code) {<span class="text-muted-foreground">{{ c.name }}</span>}
                      </span>
                    }
                  </div>
                }
                @if (i.note) { <p class="text-sm whitespace-pre-line">{{ i.note }}</p> }

                <!-- The client's answer -->
                <div class="mt-auto flex flex-col gap-2 border-t pt-3">
                  <div class="flex flex-wrap items-center gap-2">
                    <button hlmBtn type="button" class="h-11 lg:h-9" [variant]="a?.verdict === 'Like' ? 'default' : 'outline'" [attr.aria-pressed]="a?.verdict === 'Like'"
                            (click)="setVerdict(i, 'Like')"><ng-icon name="lucideThumbsUp" />{{ 'packages.shared.like' | transloco }}</button>
                    <button hlmBtn type="button" class="h-11 lg:h-9" [variant]="a?.verdict === 'Pass' ? 'default' : 'outline'" [attr.aria-pressed]="a?.verdict === 'Pass'"
                            (click)="setVerdict(i, 'Pass')"><ng-icon name="lucideThumbsDown" />{{ 'packages.shared.pass' | transloco }}</button>
                    <button hlmBtn type="button" class="h-11 lg:h-9" [variant]="a?.sample ? 'default' : 'outline'" [attr.aria-pressed]="!!a?.sample"
                            (click)="toggleSample(i)"><ng-icon name="lucideShirt" />{{ (a?.sample ? 'packages.shared.sampleRequested' : 'packages.shared.requestSample') | transloco }}</button>
                    @if (saved()[i.packageItemId]) {
                      <span class="text-muted-foreground flex items-center gap-1 text-xs"><ng-icon name="lucideCheck" />{{ 'packages.shared.saved' | transloco }}</span>
                    }
                  </div>
                  @if (a?.sample) {
                    <input hlmInput class="h-11 text-sm lg:h-9" maxlength="500" [attr.aria-label]="'packages.shared.sampleNote' | transloco"
                           [placeholder]="'packages.shared.sampleNotePlaceholder' | transloco" [value]="a?.sampleNote ?? ''"
                           (input)="sampleDraft[i.packageItemId] = $any($event.target).value" (blur)="saveSampleNote(i)" />
                  }
                  <textarea hlmTextarea rows="2" maxlength="1000" class="text-sm" [attr.aria-label]="'packages.shared.comment' | transloco: { styleNo: i.styleNo }"
                            [placeholder]="'packages.shared.commentPlaceholder' | transloco" [value]="a?.comment ?? ''" (input)="draft[i.packageItemId] = $any($event.target).value"
                            (blur)="saveComment(i)"></textarea>
                </div>
              </li>
            }
          </ul>
          <p class="text-muted-foreground border-t pt-4 text-xs">{{ 'packages.shared.footer' | transloco: { label: p.label, date: (p.expiresUtc | date: 'mediumDate') } }}</p>
        }
      </div>
    </main>
  `,
})
export class SharedPackagePage implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);
  private readonly api = `${environment.apiBaseUrl}/api/v1/share`;

  readonly token = signal('');
  readonly pkg = signal<SharedPackage | null>(null);
  readonly loading = signal(true);
  readonly gone = signal(false);
  readonly name = signal(this.readName());
  /** The client's answers by style (what was saved). */
  readonly answers = signal<Record<number, Answer>>({});
  readonly saved = signal<Record<number, boolean>>({});
  /** Comments being typed, saved when the box is left. */
  readonly draft: Record<number, string> = {};
  /** Sample notes being typed, saved when the box is left. */
  readonly sampleDraft: Record<number, string> = {};

  ngOnInit(): void {
    this.token.set(this.route.snapshot.paramMap.get('token') ?? '');
    this.load(true);
  }

  /** Another name typed: that person's answers (several people can use one link). */
  loadAnswers(): void {
    this.load(false);
  }

  private load(first: boolean): void {
    const name = this.name().trim();
    const params: Record<string, string> = name ? { name } : {};
    this.http.get<SharedPackage>(`${this.api}/${encodeURIComponent(this.token())}`, { params }).subscribe({
      next: (p) => {
        this.pkg.set(p);
        this.answers.set(Object.fromEntries(p.items.filter((i) => i.answer).map((i) => [i.packageItemId, {
          verdict: i.answer!.verdict, comment: i.answer!.comment, sample: i.answer!.sample, sampleNote: i.answer!.sampleNote,
        }])));
        this.saved.set({});
        if (first) this.loading.set(false);
      },
      error: () => {
        this.gone.set(true);
        this.loading.set(false);
      },
    });
  }

  imageUrl(file: string, width: number): string {
    return `${this.api}/${encodeURIComponent(this.token())}/images/${encodeURIComponent(file)}?width=${width}`;
  }

  storyImageUrl(file: string, width: number): string {
    return `${this.api}/${encodeURIComponent(this.token())}/story-images/${encodeURIComponent(file)}?width=${width}`;
  }

  /** The style numbers a story covers, as on the cards below. */
  styleNos(story: SharedStory): string {
    const byId = new Map((this.pkg()?.items ?? []).map((i) => [i.packageItemId, i.styleNo]));
    return story.packageItemIds.map((id) => byId.get(id)).filter((x) => !!x).join(', ');
  }

  pdfUrl(): string {
    return `${this.api}/${encodeURIComponent(this.token())}/line-sheet.pdf`;
  }

  setName(value: string): void {
    this.name.set(value);
    try {
      localStorage.setItem(NAME_KEY, value.trim());
    } catch {
      // private window or storage blocked: the name is still sent with this visit's answers
    }
  }

  /** The answer as saved, with what is being typed. */
  private current(i: SharedItem): Answer {
    const a = this.answers()[i.packageItemId];
    return {
      verdict: a?.verdict ?? null, comment: this.draft[i.packageItemId] ?? a?.comment ?? null, sample: a?.sample ?? false,
      sampleNote: this.sampleDraft[i.packageItemId] ?? a?.sampleNote ?? null,
    };
  }

  /** The same button again takes the answer back. */
  setVerdict(i: SharedItem, verdict: 'Like' | 'Pass'): void {
    const a = this.current(i);
    this.save(i, { ...a, verdict: a.verdict === verdict ? null : verdict });
  }

  saveComment(i: SharedItem): void {
    const text = this.draft[i.packageItemId];
    if (text === undefined || text.trim() === (this.answers()[i.packageItemId]?.comment ?? '')) return;
    this.save(i, this.current(i));
  }

  /** Asks for a sample of the style (again: takes the request back). */
  toggleSample(i: SharedItem): void {
    const a = this.current(i);
    this.save(i, { ...a, sample: !a.sample, sampleNote: a.sample ? null : a.sampleNote });
  }

  saveSampleNote(i: SharedItem): void {
    const text = this.sampleDraft[i.packageItemId];
    if (text === undefined || text.trim() === (this.answers()[i.packageItemId]?.sampleNote ?? '')) return;
    this.save(i, this.current(i));
  }

  private save(i: SharedItem, answer: Answer): void {
    const body = {
      verdict: answer.verdict, comment: answer.comment?.trim() || null, name: this.name().trim() || null, sample: answer.sample,
      sampleNote: answer.sample ? answer.sampleNote?.trim() || null : null,
    };
    this.http.put<void>(`${this.api}/${encodeURIComponent(this.token())}/items/${i.packageItemId}/feedback`, body).subscribe({
      next: () => {
        this.answers.update((a) => ({
          ...a, [i.packageItemId]: { verdict: body.verdict, comment: body.comment, sample: body.sample, sampleNote: body.sampleNote },
        }));
        this.saved.update((s) => ({ ...s, [i.packageItemId]: true }));
        delete this.draft[i.packageItemId];
        delete this.sampleDraft[i.packageItemId];
      },
      error: (e: unknown) => {
        if (e instanceof HttpErrorResponse && e.status === 404) this.gone.set(true);
        else toast.error(this.transloco.translate((e as HttpErrorResponse)?.status === 429 ? 'packages.shared.tooMany' : 'packages.shared.notSaved'));
      },
    });
  }

  private readName(): string {
    try {
      return localStorage.getItem(NAME_KEY) ?? '';
    } catch {
      return '';
    }
  }
}
