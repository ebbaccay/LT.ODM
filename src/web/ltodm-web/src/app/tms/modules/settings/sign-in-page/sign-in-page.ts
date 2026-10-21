import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, effect, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCalculator,
  lucideImage,
  lucideImageUp,
  lucideImages,
  lucideMoon,
  lucideSun,
  lucideLanguages,
  lucideListTree,
  lucideRotateCcw,
  lucideSave,
  lucideSparkles,
  lucideTimer,
  lucideTrash2,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { forkJoin, map } from 'rxjs';
import {
  DEFAULT_SIGN_IN_APPEARANCE,
  SignInAppearance,
  SignInAppearanceDto,
  SignInAppearanceService,
  SignInFocus,
  SignInIntroText,
  SignInModeLook,
  SignInModeLookDto,
  SignInOverlay,
  lookFor,
  modeLookOf,
} from '../../../../core/branding/sign-in-appearance';
import { LayoutService } from '../../../../layout/service/layout.service';
import { AuthBackdrop, glassVars, introTextClass } from '../../../../features/auth/auth-backdrop';
import { TranslationLanguage, TranslationRow, TranslationsAdminService } from '@services/translations-admin.service';

/** The sign-in texts shown on this screen (all of them stay in Settings > Translations too). */
const TEXT_KEYS: { key: string; multiline?: boolean }[] = [
  { key: 'auth.shell.subtitle' },
  { key: 'auth.shell.badge' },
  { key: 'auth.shell.headline' },
  { key: 'auth.shell.headlineAccent' },
  { key: 'auth.shell.intro', multiline: true },
  { key: 'auth.shell.featureStyles' },
  { key: 'auth.shell.featureMaterials' },
  { key: 'auth.shell.featureWorkmanship' },
  { key: 'auth.shell.featureCosting' },
  { key: 'auth.login.title' },
  { key: 'auth.login.subtitle' },
  { key: 'auth.login.noAccount' },
];

const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

/** key -> lang -> text */
type TextEdits = Record<string, Record<string, string>>;

/**
 * Settings > Sign-in page: background picture (focus point, blur, tint), the frosted-glass card, the headline panel
 * and its texts, with a live preview. The look is stored by the API (Files:Root/sign-in-page.json, picture in
 * sign-in-images); the texts are translation corrections, saved through Settings > Translations' API per language.
 */
@Component({
  selector: 'app-sign-in-page-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, RouterLink, TranslocoPipe, NgIcon, AuthBackdrop, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports,
    HlmLabelImports, HlmSkeletonImports, HlmSpinnerImports, HlmTextareaImports,
  ],
  providers: [
    provideIcons({
      lucideImage, lucideImageUp, lucideSun, lucideMoon, lucideTrash2, lucideRotateCcw, lucideSave, lucideLanguages, lucideSparkles, lucideImages, lucideListTree,
      lucideTimer, lucideCalculator,
    }),
  ],
  templateUrl: './sign-in-page.html',
})
export class SignInPageSettings implements OnInit {
  private readonly svc = inject(SignInAppearanceService);
  private readonly translations = inject(TranslationsAdminService);
  private readonly transloco = inject(TranslocoService);
  protected readonly layout = inject(LayoutService);

  protected readonly focuses: SignInFocus[] = ['center', 'top', 'bottom', 'left', 'right'];
  protected readonly overlays: SignInOverlay[] = ['none', 'dark', 'light', 'brand'];
  protected readonly introTexts: SignInIntroText[] = ['auto', 'light', 'dark'];
  protected readonly textKeys = TEXT_KEYS;
  protected readonly features = [
    { label: 'auth.shell.featureStyles', icon: 'lucideImages' },
    { label: 'auth.shell.featureMaterials', icon: 'lucideListTree' },
    { label: 'auth.shell.featureWorkmanship', icon: 'lucideTimer' },
    { label: 'auth.shell.featureCosting', icon: 'lucideCalculator' },
  ];

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly uploading = signal(false);

  /** Last saved look, and the one being edited (the preview shows the edited one). */
  readonly saved = signal<SignInAppearanceDto>(DEFAULT_SIGN_IN_APPEARANCE);
  readonly look = signal<SignInAppearanceDto>(DEFAULT_SIGN_IN_APPEARANCE);

  readonly languages = signal<TranslationLanguage[]>([]);
  private readonly rows = signal<Record<string, TranslationRow>>({});
  private readonly savedTexts = signal<TextEdits>({});
  readonly texts = signal<TextEdits>({});

  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  /**
   * Which mode's picture look is being edited (and previewed). Dark mode uses the light-mode look until it is given its
   * own; the glow, frosting and headline-panel options are shared.
   */
  readonly editMode = signal<'light' | 'dark'>(this.layout.isDark() ? 'dark' : 'light');
  /** Dark mode selected but it still shares the light-mode look: edits apply to both modes. */
  readonly sharedDark = computed(() => this.editMode() === 'dark' && !this.look().dark);
  /** The picture fields being edited. */
  readonly edited = computed<SignInModeLookDto>(() => {
    const l = this.look();
    return this.editMode() === 'dark' && l.dark ? l.dark : modeLookOf(l);
  });

  readonly lookChanged = computed(() => JSON.stringify(toRequest(this.look())) !== JSON.stringify(toRequest(this.saved())));
  readonly changedTexts = computed(() => {
    const now = this.texts();
    const before = this.savedTexts();
    return Object.entries(now).flatMap(([key, langs]) =>
      Object.entries(langs).filter(([lang, text]) => (before[key]?.[lang] ?? '') !== text).map(([lang, text]) => ({ key, lang, text })),
    );
  });
  readonly dirty = computed(() => this.lookChanged() || this.changedTexts().length > 0);
  readonly isDefaultLook = computed(() => JSON.stringify(toRequest(this.look())) === JSON.stringify(toRequest(DEFAULT_SIGN_IN_APPEARANCE)));

  // Preview: the real layout at 1280 x 800, scaled to the panel's width.
  private readonly previewFrame = viewChild<ElementRef<HTMLElement>>('previewFrame');
  private readonly destroyRef = inject(DestroyRef);
  readonly previewScale = signal(0.4);
  readonly previewLook = computed(() => lookFor(this.look(), this.editMode() === 'dark'));
  /** The preview can show dark mode inside a light app, not light mode inside a dark app. */
  readonly previewModeMismatch = computed(() => this.editMode() === 'light' && this.layout.isDark());
  protected readonly glass = computed(() => glassVars(this.previewLook()));
  protected readonly introClass = computed(() => (this.previewLook().backgroundUrl ? introTextClass(this.previewLook()) : ''));

  ngOnInit(): void {
    forkJoin([this.svc.get(), this.translations.table()]).subscribe({
      next: ([look, table]) => {
        const full = { ...DEFAULT_SIGN_IN_APPEARANCE, ...look };
        this.saved.set(full);
        this.look.set(full);
        this.languages.set(table.languages);
        const rows: Record<string, TranslationRow> = {};
        const texts: TextEdits = {};
        for (const { key } of TEXT_KEYS) {
          const row = table.rows.find((r) => r.key === key);
          if (!row) continue;
          rows[key] = row;
          texts[key] = Object.fromEntries(table.languages.map((l) => [l.code, row.overrides[l.code] ?? row.base[l.code] ?? '']));
        }
        this.rows.set(rows);
        this.savedTexts.set(texts);
        this.texts.set(structuredClone(texts));
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(errorMessage(e));
      },
    });
  }

  constructor() {
    // The panel's width changes after the first paint (sidebar, scrollbar, fonts) and with the window, so the scale
    // follows the panel itself rather than being measured once.
    let observer: ResizeObserver | undefined;
    effect(() => {
      const frame = this.previewFrame()?.nativeElement;
      observer?.disconnect();
      if (!frame) return;
      observer = new ResizeObserver(([entry]) => {
        const width = entry.contentRect.width;
        if (width) this.previewScale.set(width / 1280);
      });
      observer.observe(frame);
    });
    this.destroyRef.onDestroy(() => observer?.disconnect());
  }


  patch(change: Partial<SignInAppearanceDto>): void {
    this.look.update((l) => ({ ...l, ...change }));
  }

  /** Changes a picture field of the mode being edited. */
  patchPicture(change: Partial<SignInModeLookDto>): void {
    if (this.editMode() === 'dark' && this.look().dark) this.look.update((l) => ({ ...l, dark: { ...l.dark!, ...change } }));
    else this.patch(change);
  }

  /** On: dark mode starts from a copy of the light-mode look. Off: dark mode goes back to the light-mode look. */
  setSeparateDark(on: boolean): void {
    this.patch({ dark: on ? modeLookOf(this.look()) : null });
  }

  rangeValue = (e: Event) => Number((e.target as HTMLInputElement).value);

  // ── Picture ─────────────────────────────────────────────────

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (!/^image\/(jpeg|png|gif|webp)$/.test(file.type)) {
      toast.error(this.transloco.translate('settings.signInPage.onlyImages'));
      return;
    }
    if (file.size > MAX_UPLOAD_BYTES) {
      toast.error(this.transloco.translate('settings.signInPage.tooLarge'));
      return;
    }
    this.uploading.set(true);
    this.svc.upload(file).subscribe({
      next: ({ backgroundImage, backgroundUrl }) => {
        this.uploading.set(false);
        const first = !this.edited().backgroundUrl;
        // A first picture usually needs a tint, light headline text and a fairly solid card to stay readable; the glow
        // mostly gets in the way.
        this.patchPicture(
          first
            ? { backgroundImage, backgroundUrl, overlay: 'dark', overlayStrength: 35, introText: 'light', glassOpacity: Math.max(this.edited().glassOpacity, 70) }
            : { backgroundImage, backgroundUrl },
        );
        // The glow is shared by both modes: only the light-mode (or shared) look turns it off.
        if (first && (this.editMode() === 'light' || this.sharedDark())) this.patch({ showGlow: false });
      },
      error: (e) => {
        this.uploading.set(false);
        toast.error(errorMessage(e));
      },
    });
  }

  removePicture(): void {
    this.patchPicture({ backgroundImage: null, backgroundUrl: null, overlay: 'none', introText: 'auto' });
    if (this.editMode() === 'light' || this.sharedDark()) this.patch({ showGlow: true });
  }

  // ── Texts ───────────────────────────────────────────────────

  text(key: string, lang: string): string {
    return this.texts()[key]?.[lang] ?? '';
  }

  setText(key: string, lang: string, value: string): void {
    this.texts.update((t) => ({ ...t, [key]: { ...t[key], [lang]: value } }));
  }

  isCorrected(key: string, lang: string): boolean {
    const base = this.rows()[key]?.base[lang] ?? '';
    return this.text(key, lang) !== base;
  }

  resetText(key: string, lang: string): void {
    this.setText(key, lang, this.rows()[key]?.base[lang] ?? '');
  }

  /** The edited text in the language the app is shown in, for the preview. */
  t = (key: string): string => this.texts()[key]?.[this.activeLang()] ?? this.transloco.translate(key);

  hasText = (key: string) => this.textKeys.some((k) => k.key === key) && !!this.rows()[key];

  // ── Save ────────────────────────────────────────────────────

  resetLook(): void {
    this.look.set({ ...DEFAULT_SIGN_IN_APPEARANCE, updatedBy: this.saved().updatedBy, updatedUtc: this.saved().updatedUtc });
  }

  discard(): void {
    this.look.set(this.saved());
    this.texts.set(structuredClone(this.savedTexts()));
  }

  save(): void {
    if (!this.dirty() || this.saving()) return;
    this.saving.set(true);
    const textChanges = this.changedTexts();
    const look$ = this.lookChanged() ? this.svc.save(toRequest(this.look())) : null;
    // A text equal to the deployed one is sent as null: the correction is removed.
    const texts$ = textChanges.map(({ key, lang, text }) =>
      this.translations.set(lang, key, text === (this.rows()[key]?.base[lang] ?? '') ? null : text).pipe(map((row) => ({ key, row }))),
    );
    forkJoin([...(look$ ? [look$.pipe(map((dto) => ({ dto })))] : []), ...texts$]).subscribe({
      next: (results) => {
        for (const r of results) {
          if ('dto' in r) {
            const full = { ...DEFAULT_SIGN_IN_APPEARANCE, ...r.dto };
            this.saved.set(full);
            this.look.set(full);
          } else {
            this.rows.update((rows) => ({ ...rows, [r.key]: r.row }));
          }
        }
        this.savedTexts.set(structuredClone(this.texts()));
        if (texts$.length) this.translations.refreshLive();
        this.saving.set(false);
        toast.success(this.transloco.translate('settings.signInPage.saved'), {
          description: this.transloco.translate('settings.signInPage.savedHint'),
        });
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(errorMessage(e));
      },
    });
  }
}

function toRequest(a: SignInAppearanceDto | SignInAppearance): SignInAppearance {
  return {
    backgroundImage: a.backgroundImage,
    focus: a.focus,
    backgroundBlur: a.backgroundBlur,
    overlay: a.overlay,
    overlayStrength: a.overlayStrength,
    showGlow: a.showGlow,
    glassOpacity: a.glassOpacity,
    glassBlur: a.glassBlur,
    showIntro: a.showIntro,
    showFeatures: a.showFeatures,
    introText: a.introText,
    dark: a.dark ? toModeRequest(a.dark) : null,
  };
}

function toModeRequest(d: SignInModeLook | SignInModeLookDto): SignInModeLook {
  return {
    backgroundImage: d.backgroundImage,
    focus: d.focus,
    backgroundBlur: d.backgroundBlur,
    overlay: d.overlay,
    overlayStrength: d.overlayStrength,
    introText: d.introText,
    glassOpacity: d.glassOpacity,
  };
}

function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { title?: string; errors?: Record<string, string[]> } | null;
    const first = body?.errors ? Object.values(body.errors).flat()[0] : undefined;
    if (first) return first;
    if (err.status === 0) return 'The server could not be reached. Check your connection and try again.';
    if (err.status === 403) return 'You need the Admin role to change the sign-in page.';
    if (err.status === 413) return 'The picture is too large. Pictures must be 10 MB or smaller.';
    if (body?.title && err.status < 500) return body.title;
  }
  return 'The change could not be saved. Please try again.';
}
