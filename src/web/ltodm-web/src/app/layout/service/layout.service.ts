import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';
import { PRIMARY_PALETTES, SURFACE_PALETTES, SurfacePalette } from './palette';
import { Accent, DEFAULT_SURFACE, THEME_VARS, themeVars } from './theme-colors';

export type { Accent } from './theme-colors';
export type { SurfacePalette } from './palette';

export type ThemeMode = 'light' | 'dark' | 'system';
export type MenuMode = 'static' | 'overlay';

export interface LayoutConfig {
  mode: ThemeMode;
  accent: Accent;
  /** "#rrggbb" picked in the theme panel; used when accent is 'custom'. */
  customAccent: string;
  /** Grey family for backgrounds, cards, borders and muted text. */
  surface: SurfacePalette;
  /** Corner radius in rem (Spartan --radius). */
  radius: number;
  menuMode: MenuMode;
  /** Glass effect: translucent, blurred panels over a soft glow of the accent colours. */
  glass: boolean;
}

const STORAGE_KEY = 'ltodm.layout';

/** Desktop starts above 1024px (see src/styles/_breakpoints.scss and the Tailwind lg breakpoint). */
const DESKTOP_QUERY = '(min-width: 1025px)';

const DEFAULT_CONFIG: LayoutConfig = {
  mode: 'system',
  accent: 'ltodm',
  customAccent: '#0d47a1',
  surface: DEFAULT_SURFACE,
  radius: 0.625,
  menuMode: 'static',
  glass: true,
};

/** Theme choices (saved per browser) and sidebar state. */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  private readonly document = inject(DOCUMENT);
  private readonly darkQuery = matchMedia('(prefers-color-scheme: dark)');
  private readonly desktopQuery = matchMedia(DESKTOP_QUERY);

  readonly config = signal<LayoutConfig>(loadConfig());

  readonly isDesktop = signal(this.desktopQuery.matches);
  private readonly systemDark = signal(this.darkQuery.matches);

  /** Desktop + static mode: the user collapsed the sidebar. */
  readonly staticMenuHidden = signal(false);
  /** Phone/tablet, or overlay mode: the slide-out menu is open. */
  readonly drawerOpen = signal(false);

  readonly isDark = computed(() => {
    const mode = this.config().mode;
    return mode === 'dark' || (mode === 'system' && this.systemDark());
  });

  /** Sidebar is pinned beside the content (desktop, static mode, not collapsed). */
  readonly sidebarDocked = computed(() => this.isDesktop() && this.config().menuMode === 'static' && !this.staticMenuHidden());

  constructor() {
    this.darkQuery.addEventListener('change', (e) => this.systemDark.set(e.matches));
    this.desktopQuery.addEventListener('change', (e) => {
      this.isDesktop.set(e.matches);
      this.drawerOpen.set(false);
    });

    effect(() => {
      const config = this.config();
      const root = this.document.documentElement;
      root.classList.toggle('dark', this.isDark());
      root.classList.toggle('glass', config.glass);
      // Defaults come from tailwind.css; other choices set the variables inline (they beat the stylesheet).
      const vars = themeVars(config.accent, config.customAccent, config.surface, this.isDark());
      for (const name of THEME_VARS) {
        const value = vars[name];
        if (value) root.style.setProperty(name, value);
        else root.style.removeProperty(name);
      }
      root.style.setProperty('--radius', `${config.radius}rem`);
      saveConfig(config);
    });

    effect(() => this.document.body.classList.toggle('overflow-hidden', this.drawerOpen()));
  }

  update(patch: Partial<LayoutConfig>): void {
    this.config.update((c) => ({ ...c, ...patch }));
  }

  toggleDarkMode(): void {
    this.update({ mode: this.isDark() ? 'light' : 'dark' });
  }

  onMenuToggle(): void {
    if (this.isDesktop() && this.config().menuMode === 'static') {
      this.staticMenuHidden.update((hidden) => !hidden);
    } else {
      this.drawerOpen.update((open) => !open);
    }
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }
}

function loadConfig(): LayoutConfig {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    return saved ? sanitize({ ...DEFAULT_CONFIG, ...(JSON.parse(saved) as Partial<LayoutConfig>) }) : DEFAULT_CONFIG;
  } catch {
    return DEFAULT_CONFIG;
  }
}

/** Drops values saved by older versions of the theme panel. */
function sanitize(config: LayoutConfig): LayoutConfig {
  const accent = (config.accent as string) === 'neutral' ? 'noir' : config.accent;
  const knownAccent = accent === 'ltodm' || accent === 'noir' || accent === 'custom' || accent in PRIMARY_PALETTES;
  return {
    ...config,
    accent: knownAccent ? accent : DEFAULT_CONFIG.accent,
    surface: config.surface in SURFACE_PALETTES ? config.surface : DEFAULT_CONFIG.surface,
    glass: typeof config.glass === 'boolean' ? config.glass : DEFAULT_CONFIG.glass,
  };
}

function saveConfig(config: LayoutConfig): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(config));
  } catch {
    // Storage blocked (private mode, policy): preferences just won't persist.
  }
}
