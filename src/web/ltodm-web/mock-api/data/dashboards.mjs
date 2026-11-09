// Home page dashboards: GET /api/v1/dashboards/mine and /api/v1/dashboards/designer (invented sample data).
// The designer checks mirror DesignerChecks in the API (Application/Dashboards/DesignerDashboard.cs).
import { roles } from './admin.mjs';

const READERS = ['Admin', 'Merchandiser', 'Designer', 'Costing', 'Viewer'];
const KEYS = ['library', 'designer'];
const CHECKS = ['picture', 'productType', 'gender', 'colorways', 'bom', 'colorwaysOnBom', 'bomColours', 'bomDetails', 'consumption', 'suppliers'];

const facts = [
  // styleNo, customer, season, model, sketch, productType, gender, colorways, notOnBom, lines, noColour, noDetails, noLco, noSupplier, packages, outcome
  ['LT28-WB01', 'ADI', '2028-SS', 'Packable windbreaker', true, 'JACKET', 'FEMALE', 4, 0, 18, 0, 0, 0, 0, 1, 'Sampling'],
  ['LT28-WB02', 'ADI', '2028-SS', 'Windbreaker hooded', true, 'JACKET', 'MALE', 3, 1, 16, 2, 0, 4, 0, 0, null],
  ['LT28-TR05', 'SKX', '2028-SS', 'Running short 5in', true, 'SHORTS', null, 2, 0, 11, 0, 1, 11, 1, 0, null],
  ['LT28-TR06', 'SKX', '2028-SS', 'Running tight', false, 'TIGHTS', 'FEMALE', 0, 0, 0, 0, 0, 0, 0, 0, null],
  ['LT28-PO01', 'ADI', '2028-SS', 'Quarter-zip pullover', false, null, null, 0, 0, 0, 0, 0, 0, 0, 0, null],
  ['LT27-JK11', 'ADI', '2027-FW', 'Puffer vest', true, 'VEST', 'MALE', 5, 0, 24, 0, 0, 0, 0, 2, 'Ordered'],
  ['LT27-JK12', 'ADI', '2027-FW', 'Insulated parka', true, 'JACKET', 'FEMALE', 3, 0, 27, 3, 2, 0, 5, 1, null],
];

function evaluate(f) {
  const [, , , , sketch, pt, gender, cw, notOnBom, lines, noColour, noDetails, noLco, noSupplier] = f;
  const missing = [];
  let passed = 0;
  const check = (key, ok, blocked = false, count = null) => {
    if (ok) passed++;
    else if (!blocked) missing.push({ check: key, count });
  };
  check('picture', sketch);
  check('productType', !!pt);
  check('gender', !!gender);
  check('colorways', cw > 0);
  check('bom', lines > 0);
  check('colorwaysOnBom', lines > 0 && cw > 0 && notOnBom === 0, !lines || !cw, notOnBom);
  check('bomColours', lines > 0 && cw > 0 && noColour === 0, !lines || !cw, noColour);
  check('bomDetails', lines > 0 && noDetails === 0, !lines, noDetails);
  check('consumption', lines > 0 && noLco === 0, !lines, noLco);
  check('suppliers', lines > 0 && noSupplier === 0, !lines, noSupplier);
  return { score: Math.floor((passed * 100) / CHECKS.length), missing };
}

function designer(season) {
  const day = (i) => new Date(Date.UTC(2026, 10, 4 - i)).toISOString();
  const styles = facts
    .map((f, i) => ({ f, i }))
    .filter(({ f }) => !season || f[2] === season)
    .map(({ f, i }) => ({
      styleId: 9000 + i,
      styleNo: f[0],
      description: f[3],
      modelName: f[3],
      customerCode: f[1],
      seasonCode: f[2],
      imageUrl: null,
      sketchUrl: null,
      conceptId: i === 0 ? 6 : null,
      colorways: f[7],
      bomLines: f[9],
      packages: f[14],
      outcome: f[15],
      ...evaluate(f),
      createdUtc: day(i + 10),
      lastChangedUtc: day(i),
    }));
  const seasons = [...new Set(facts.map((f) => f[2]))].map((s) => ({ seasonCode: s, styles: facts.filter((f) => f[2] === s).length }));
  const pitches = [
    { packageId: 10, packageName: 'LT Design SS28', packageStatus: 'Sampling', customerCode: 'ADI', styleId: 9000, styleNo: 'LT28-WB01', outcome: 'Sampling', atUtc: day(2) },
    { packageId: 4, packageName: 'Outerwear FW27', packageStatus: 'Ordered', customerCode: 'ADI', styleId: 9005, styleNo: 'LT27-JK11', outcome: 'Ordered', atUtc: day(9) },
    { packageId: 4, packageName: 'Outerwear FW27', packageStatus: 'Ordered', customerCode: 'ADI', styleId: 9006, styleNo: 'LT27-JK12', outcome: null, atUtc: day(9) },
  ];
  return {
    season: season || null,
    seasons,
    totals: {
      styles: styles.length,
      complete: styles.filter((s) => s.score === 100).length,
      averageScore: styles.length ? Math.floor(styles.reduce((a, s) => a + s.score, 0) / styles.length) : 0,
      colorways: styles.reduce((a, s) => a + s.colorways, 0),
      bomLines: styles.reduce((a, s) => a + s.bomLines, 0),
      inPackages: styles.filter((s) => s.packages > 0).length,
      sampling: styles.filter((s) => s.outcome === 'Sampling').length,
      ordered: styles.filter((s) => s.outcome === 'Ordered').length,
    },
    checks: CHECKS.map((c) => ({ check: c, styles: styles.filter((s) => s.missing.some((m) => m.check === c)).length })),
    styles,
    concepts: [
      { conceptId: 6, name: 'Packable recycled windbreaker', customerCode: 'ADI', seasonCode: '2028-SS', status: 'Ready', styles: 2, lastChangedUtc: day(1) },
      { conceptId: 7, name: 'Seamless running layer', customerCode: 'SKX', seasonCode: null, status: 'Draft', styles: 0, lastChangedUtc: day(3) },
    ],
    pitches: pitches.filter((p) => !season || facts[p.styleId - 9000][2] === season),
  };
}

/** Returns true when the request was answered. */
export function handleDashboards(req, url, json, userRoles) {
  const p = url.pathname;
  const reader = userRoles.some((r) => READERS.includes(r));
  if (p === '/api/v1/dashboards/mine') {
    if (!reader) return json(200, { defaultKey: null, available: [] }), true;
    const chosen = roles.filter((r) => userRoles.includes(r.name) && r.dashboardKey && r.dashboardKey !== 'library' && KEYS.includes(r.dashboardKey));
    const key = KEYS.find((k) => chosen.some((r) => r.dashboardKey === k)) ?? 'library';
    // Only Admins may switch; everyone else sees their role's dashboard alone.
    return json(200, { defaultKey: key, available: userRoles.includes('Admin') ? KEYS : [key] }), true;
  }
  if (p === '/api/v1/dashboards/designer') {
    if (!reader) return json(403, { title: 'Forbidden' }), true;
    return json(200, designer(url.searchParams.get('season'))), true;
  }
  return false;
}
