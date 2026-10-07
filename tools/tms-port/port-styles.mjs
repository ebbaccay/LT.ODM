// Converts a stylesheet ported from TMS to the LT ODM theme.
//
//   node tools/tms-port/port-styles.mjs <file.scss> [...more]
//
// - adds `@use 'tms-theme' as tms;` and `:host { @include tms.tokens; }` (see src/styles/_tms-theme.scss)
// - renames TMS --accent to --tms-accent (Spartan uses --accent for hover surfaces)
// - drops TMS font declarations (the app font is used)
// - replaces hard-coded colours with theme tokens so the screen follows light/dark mode and the accent colour
// Prints any colours it left alone, for a manual look.
import fs from 'node:fs';

const families = {
  'var(--white)': ['fff', 'ffffff'],
  'var(--slate-50)': ['f8fafc', 'f9fafb', 'f7f9fb', 'f5f6f7', 'f5f6f8', 'fafafa', 'fafbfc', 'f8f9fa'],
  'var(--slate-100)': ['f1f5f9', 'f3f4f6', 'f0f0f0', 'edf1f6', 'f4f6f8', 'f2f4f7', 'f5f5f5', 'eee', 'eeeeee'],
  'var(--slate-200)': ['e2e8f0', 'e5e7eb', 'e5e5e5', 'd9e1ec', 'e0e0e0', 'e4e7ec', 'ddd', 'dddddd', 'e8e8e8'],
  'var(--slate-300)': ['cbd5e1', 'd1d5db', 'ccc', 'cccccc', 'd0d0e8', 'c0c0c0', 'bdbdbd'],
  'var(--slate-400)': ['94a3b8', '9ca3af', 'aaa', 'aaaaaa', '999', '999999'],
  'var(--slate-500)': ['64748b', '6b7280', '6a6d70', '556b82', '777', '777777', '888', '888888', '666', '666666'],
  'var(--slate-600)': ['475569', '4b5563', '555', '555555'],
  'var(--slate-700)': ['334155', '374151', '32363a', '444', '444444'],
  'var(--slate-800)': ['1e293b', '1f2937', '1d2d3e', '333', '333333', '222', '222222'],
  'var(--slate-900)': ['0f172a', '111827', '000', '000000', '111', '111111'],
  'var(--navy)': ['3f6fa6', '3f5f86', '3470c8', '3b82f6', '2563eb', '1b3a6b', '3b3b9b', '2e2e80', '1d4ed8', '0a6ed1', '0854a0', '1e40af', '0b2545', '0070f2'],
  'var(--blue-50)': ['eff6ff', 'eef4fc', 'f0f0fa', 'f0f7ff', 'ebf5ff', 'e8f0fe'],
  'var(--blue-100)': ['dbeafe', 'e8eef8', 'e0ebf8', 'dce8f5'],
  'var(--blue-200)': ['bfdbfe', 'c7d7ee'],
  'var(--red)': ['dc2626', 'ef4444', 'b91c1c', 'fca5a5', 'f87171', 'e53935', 'bb0000', 'd32f2f'],
  'var(--red-fg)': ['991b1b', '7f1d1d'],
  'var(--red-bg)': ['fee2e2', 'fef2f2', 'fecaca', 'ffebee', 'fff1f2'],
  'var(--amber)': ['f59e0b', 'fcd34d', 'd97706', 'fbbf24', 'e9730c', 'f97316', 'ea580c'],
  'var(--amber-fg)': ['92400e', 'b45309', '78350f', '9a3412'],
  'var(--amber-bg)': ['fef3c7', 'fffbeb', 'fde68a', 'fff7ed', 'ffedd5'],
  'var(--green)': ['059669', '22c55e', '16a34a', '10b981', '107e3e', '2e7d32', '256f3a'],
  'var(--green-fg)': ['065f46', '166534', '047857', '14532d'],
  'var(--green-bg)': ['d1fae5', 'dcfce7', 'f0fdf4', 'ecfdf5', 'bbf7d0', 'e8f5e9'],
  'var(--teal)': ['17a2b8', '2ab89a', '0d9488', '14b8a6', '0891b2'],
  'var(--teal-pale)': ['ccfbf1', 'f0fdfa', 'cffafe', 'ecfeff'],
  'var(--violet-fg)': ['6d28d9', '7c3aed', '5b21b6', '8b5cf6'],
  'var(--violet-bg)': ['ede9fe', 'f5f3ff'],
};
const map = new Map();
for (const [token, hexes] of Object.entries(families)) for (const h of hexes) map.set(h, token);

const WHITE = new Set(['fff', 'ffffff']);

for (const file of process.argv.slice(2)) {
  let src = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  const left = new Map();
  let replaced = 0;

  // TMS --accent -> --tms-accent
  src = src.replace(/--accent(-lt)?\b(?=\s*[:),])/g, (_m, lt) => `--tms-accent${lt ?? ''}`);
  // App font instead of the TMS fonts
  src = src.replace(/^\s*--font-(sans|mono)\s*:[^;]*;\s*\n/gm, '');

  // Colours, property by property
  src = src.replace(/([a-z-]+)(\s*:\s*)([^;{}]*#[0-9a-fA-F]{3,8}\b[^;{}]*)(;|\n|})/g, (whole, prop, sep, value, end) => {
    if (prop.startsWith('--') && /^--(sh|shadow)/.test(prop)) return whole; // keep custom shadows
    const v = value.replace(/#([0-9a-fA-F]{3,8})\b/g, (hex, h) => {
      const k = h.toLowerCase();
      if (prop === 'color' && WHITE.has(k)) { replaced++; return 'var(--primary-foreground)'; }
      // box-shadow colours stay as they are
      if (prop.includes('shadow')) return hex;
      const token = map.get(k);
      if (!token) { left.set(k, (left.get(k) ?? 0) + 1); return hex; }
      replaced++;
      return token;
    });
    return `${prop}${sep}${v}${end}`;
  });

  if (!src.includes("@use 'tms-theme'")) src = `@use 'tms-theme' as tms;\n\n${src}`;
  if (!src.includes('@include tms.tokens')) src = `${src.trimEnd()}\n\n// LT ODM theme (see src/styles/_tms-theme.scss). Last, so it overrides the TMS palette above.\n:host {\n  @include tms.tokens;\n}\n`;

  fs.writeFileSync(file, src);
  const rest = [...left.entries()].sort((a, b) => b[1] - a[1]).map(([h, n]) => `#${h}(${n})`).join(' ');
  console.log(`${file}: ${replaced} colours themed${rest ? `; left: ${rest}` : ''}`);
}
