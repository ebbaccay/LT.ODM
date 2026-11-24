import { DecimalPipe } from '@angular/common';
import { Estimate, StylePriceHint, StylePriceSummary } from './costing.service';

/** "8.43 – 8.56", or just "8.50" when the lowest and highest read the same. */
export function fobRange(number: DecimalPipe, s: Pick<StylePriceSummary, 'minFob' | 'maxFob'>): string {
  const low = number.transform(s.minFob, '1.2-2') ?? '';
  const high = number.transform(s.maxFob, '1.2-2') ?? '';
  return low === high ? low : `${low} – ${high}`;
}

/** What a set of styles cost: the range and the middle of their latest FOBs, in the currency most of them use. */
export interface PriceSummary {
  currency: string;
  low: number;
  high: number;
  typical: number;
  styles: number;
}

export function summarizePrices(hints: readonly StylePriceHint[]): PriceSummary | null {
  if (!hints.length) return null;
  const counts = new Map<string, number>();
  for (const h of hints) counts.set(h.currencyCode, (counts.get(h.currencyCode) ?? 0) + 1);
  const currency = [...counts.entries()].sort((a, b) => b[1] - a[1])[0][0];
  const fobs = hints.filter((h) => h.currencyCode === currency).map((h) => h.latestFob).sort((a, b) => a - b);
  const mid = Math.floor(fobs.length / 2);
  const typical = fobs.length % 2 ? fobs[mid] : (fobs[mid - 1] + fobs[mid]) / 2;
  return { currency, low: fobs[0], high: fobs[fobs.length - 1], typical, styles: fobs.length };
}

/** A concept's target price as typed ("USD 12-14 FOB", "14.50"): its highest figure and currency, if any. */
export function parseTargetPrice(text: string | null | undefined): { low: number; high: number; currency: string | null } | null {
  const numbers = (text ?? '').match(/\d+(?:\.\d+)?/g)?.map(Number).filter((n) => n > 0);
  if (!numbers?.length) return null;
  return { low: Math.min(...numbers), high: Math.max(...numbers), currency: (text ?? '').toUpperCase().match(/\b[A-Z]{3}\b(?<!\bFOB\b)/)?.[0] ?? null };
}

/** What an estimate is based on, with its parts when the user may see them. */
export function estimateBasisText(i18n: { translate: (key: string, params?: Record<string, unknown>) => string }, e: Estimate): string {
  const styles = e.peerStyles.split(', ');
  const shown = styles.slice(0, 4).join(', ') + (styles.length > 4 ? ` +${styles.length - 4}` : '');
  const base = i18n.translate('ioPrices.estimate.basis', { count: e.peers, category: e.category, styles: shown });
  if (e.making === null || e.fabric === null) return base;
  const n = (v: number | null) => (v ?? 0).toFixed(2);
  const making = e.cpmFromRate && e.cpm !== null && e.smv !== null
    ? i18n.translate('ioPrices.estimate.makingRate', { making: n(e.making), smv: e.smv.toFixed(1), cpm: e.cpm.toFixed(2), plant: e.plantCode ?? '' })
    : i18n.translate('ioPrices.estimate.makingPeers', { making: n(e.making) });
  return `${base} ${i18n.translate('ioPrices.estimate.parts', { making, fabric: n(e.fabric), trims: n(e.trims), other: n(e.other) })}`;
}
