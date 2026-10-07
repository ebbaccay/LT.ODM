import { Oklch, PRIMARY_PALETTES, PrimaryPalette, SURFACE_PALETTES, SurfacePalette } from './palette';

/** 'ltodm' = brand blue from tailwind.css; 'noir' = the surface's own dark/light grey; 'custom' = picked in the theme panel. */
export type Accent = 'ltodm' | 'noir' | PrimaryPalette | 'custom';

/** The stylesheet's surface (tailwind.css :root). */
export const DEFAULT_SURFACE: SurfacePalette = 'zinc';

const ACCENT_VARS = ['--primary', '--primary-foreground', '--ring', '--sidebar-primary', '--sidebar-primary-foreground', '--sidebar-ring'] as const;

const SURFACE_VARS = [
  '--ground',
  '--background',
  '--foreground',
  '--card',
  '--card-foreground',
  '--popover',
  '--popover-foreground',
  '--secondary',
  '--secondary-foreground',
  '--muted',
  '--muted-foreground',
  '--accent',
  '--accent-foreground',
  '--border',
  '--input',
  '--sidebar',
  '--sidebar-foreground',
  '--sidebar-accent',
  '--sidebar-accent-foreground',
  '--sidebar-border',
] as const;

/** Every variable LayoutService may set inline on <html>; anything not returned below is cleared. */
export const THEME_VARS: readonly string[] = [...ACCENT_VARS, ...SURFACE_VARS];

type Vars = Partial<Record<string, string>>;

/** Text goes dark on top of an accent at least this light. */
const LIGHT_ACCENT = 0.68;
const WHITE = 'oklch(0.985 0 0)';

/** Inline theme variables for the chosen accent and surface. Defaults return nothing, so tailwind.css applies. */
export function themeVars(accent: Accent, customHex: string, surface: SurfacePalette, dark: boolean): Vars {
  return { ...surfaceVars(surface, dark), ...accentVars(accent, customHex, surface, dark) };
}

/** Same mapping tailwind.css uses for Zinc (shadcn's base-colour layout). */
function surfaceVars(surface: SurfacePalette, dark: boolean): Vars {
  if (surface === DEFAULT_SURFACE) return {};
  const g = SURFACE_PALETTES[surface];

  if (dark) {
    // Border and input stay as translucent white from the stylesheet.
    return {
      '--ground': css(g[950]),
      '--background': css(g[950]),
      '--foreground': css(g[50]),
      '--card': css(g[900]),
      '--card-foreground': css(g[50]),
      '--popover': css(g[900]),
      '--popover-foreground': css(g[50]),
      '--secondary': css(g[800]),
      '--secondary-foreground': css(g[50]),
      '--muted': css(g[800]),
      '--muted-foreground': css(g[400]),
      '--accent': css(g[800]),
      '--accent-foreground': css(g[50]),
      '--sidebar': css(g[900]),
      '--sidebar-foreground': css(g[50]),
      '--sidebar-accent': css(g[800]),
      '--sidebar-accent-foreground': css(g[50]),
    };
  }
  // Tinted page, white cards and controls (as Sakai's surface ground).
  return {
    '--ground': css(g[50]),
    '--background': 'oklch(1 0 0)',
    '--foreground': css(g[950]),
    '--card': 'oklch(1 0 0)',
    '--card-foreground': css(g[950]),
    '--popover': 'oklch(1 0 0)',
    '--popover-foreground': css(g[950]),
    '--secondary': css(g[100]),
    '--secondary-foreground': css(g[900]),
    '--muted': css(g[100]),
    '--muted-foreground': css(g[500]),
    '--accent': css(g[100]),
    '--accent-foreground': css(g[900]),
    '--border': css(g[200]),
    '--input': css(g[200]),
    '--sidebar': 'oklch(1 0 0)',
    '--sidebar-foreground': css(g[950]),
    '--sidebar-accent': css(g[100]),
    '--sidebar-accent-foreground': css(g[900]),
    '--sidebar-border': css(g[200]),
  };
}

function accentVars(accent: Accent, customHex: string, surface: SurfacePalette, dark: boolean): Vars {
  if (accent === 'ltodm') return {};

  if (accent === 'noir') {
    const g = SURFACE_PALETTES[surface];
    return primaryVars(css(dark ? g[200] : g[900]), css(dark ? g[900] : g[50]), css(dark ? g[500] : g[400]));
  }

  if (accent === 'custom') {
    const base = hexToOklch(customHex);
    if (!base) return {};
    // Dark mode lifts the colour a little so it stands out on the dark background, as the presets do.
    const primary: Oklch = dark ? [clamp(base[0] + 0.08, 0.55, 0.85), base[1], base[2]] : base;
    const ring: Oklch = [clamp(primary[0] + (dark ? -0.1 : 0.1), 0.35, 0.9), primary[1], primary[2]];
    const darkText: Oklch = [0.21, Math.min(primary[1], 0.05), primary[2]];
    return primaryVars(css(primary), css(primary[0] >= LIGHT_ACCENT ? darkText : null), css(ring));
  }

  // Tailwind palette: 600 in light mode, 500 in dark mode, as shadcn's colour themes do.
  const p = PRIMARY_PALETTES[accent];
  const primary = dark ? p[500] : p[600];
  return primaryVars(css(primary), css(primary[0] >= LIGHT_ACCENT ? p[950] : null), css(dark ? p[700] : p[400]));
}

function primaryVars(primary: string, foreground: string, ring: string): Vars {
  return {
    '--primary': primary,
    '--primary-foreground': foreground,
    '--ring': ring,
    '--sidebar-primary': primary,
    '--sidebar-primary-foreground': foreground,
    '--sidebar-ring': ring,
  };
}

/** Swatch colour for the theme panel (light-mode shade). */
export function swatch(color: Oklch): string {
  return css(color);
}

/** sRGB hex → OKLCH (https://bottosson.github.io/posts/oklab/). */
function hexToOklch(hex: string): Oklch | null {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return null;
  const n = parseInt(m[1], 16);
  const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) => {
    const s = v / 255;
    return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });

  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const mm = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

  const L = 0.2104542553 * l + 0.793617785 * mm - 0.0040720468 * s;
  const A = 1.9779984951 * l - 2.428592205 * mm + 0.4505937099 * s;
  const B = 0.0259040371 * l + 0.7827717662 * mm - 0.808675766 * s;

  const c = Math.hypot(A, B);
  const h = c < 1e-4 ? 0 : ((Math.atan2(B, A) * 180) / Math.PI + 360) % 360;
  return [L, c, h];
}

/** null = white text. */
function css(color: Oklch | null): string {
  if (!color) return WHITE;
  const [l, c, h] = color;
  return `oklch(${fmt(l)} ${fmt(c)} ${fmt(h)})`;
}

function fmt(v: number): string {
  return String(Math.round(v * 1000) / 1000);
}

function clamp(v: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, v));
}
