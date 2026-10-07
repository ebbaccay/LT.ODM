/**
 * Status colours for screens ported from TMS, as Tailwind classes that follow light/dark mode and the
 * theme accent. Use these instead of hex colours (TMS STATUS_CFG etc.).
 *
 *   <span [class]="toneBadge(cfg.tone)">Approved</span>
 *   <span class="size-2 rounded-full" [class]="toneDot(cfg.tone)"></span>
 */
export type Tone = 'neutral' | 'primary' | 'amber' | 'violet' | 'green' | 'red' | 'teal';

/** Soft badge: tinted background, readable text. */
const BADGE: Record<Tone, string> = {
  neutral: 'bg-muted text-muted-foreground',
  primary: 'bg-primary/12 text-primary',
  amber: 'bg-amber-500/15 text-amber-700 dark:text-amber-400',
  violet: 'bg-violet-500/15 text-violet-700 dark:text-violet-400',
  green: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400',
  red: 'bg-red-500/15 text-red-700 dark:text-red-400',
  teal: 'bg-teal-500/15 text-teal-700 dark:text-teal-400',
};

/** Solid dot / bar. */
const DOT: Record<Tone, string> = {
  neutral: 'bg-muted-foreground/50',
  primary: 'bg-primary',
  amber: 'bg-amber-500',
  violet: 'bg-violet-500',
  green: 'bg-emerald-500',
  red: 'bg-red-500',
  teal: 'bg-teal-500',
};

/** Coloured text (numbers, warnings). */
const TEXT: Record<Tone, string> = {
  neutral: 'text-muted-foreground',
  primary: 'text-primary',
  amber: 'text-amber-600 dark:text-amber-400',
  violet: 'text-violet-600 dark:text-violet-400',
  green: 'text-emerald-600 dark:text-emerald-400',
  red: 'text-red-600 dark:text-red-400',
  teal: 'text-teal-600 dark:text-teal-400',
};

/** Left accent border (grouped lists). */
const BORDER: Record<Tone, string> = {
  neutral: 'border-l-muted-foreground/40',
  primary: 'border-l-primary',
  amber: 'border-l-amber-500',
  violet: 'border-l-violet-500',
  green: 'border-l-emerald-500',
  red: 'border-l-red-500',
  teal: 'border-l-teal-500',
};

/** CSS colour for inline styles (charts, gradients). The matching DOT class keeps the Tailwind variable emitted. */
const COLOR: Record<Tone, string> = {
  neutral: 'color-mix(in oklch, var(--muted-foreground) 55%, transparent)',
  primary: 'var(--primary)',
  amber: 'var(--color-amber-500)',
  violet: 'var(--color-violet-500)',
  green: 'var(--color-emerald-500)',
  red: 'var(--color-red-500)',
  teal: 'var(--color-teal-500)',
};

export const toneBadge = (tone: Tone) => BADGE[tone];
export const toneDot = (tone: Tone) => DOT[tone];
export const toneText = (tone: Tone) => TEXT[tone];
export const toneBorder = (tone: Tone) => BORDER[tone];
export const toneColor = (tone: Tone) => COLOR[tone];
