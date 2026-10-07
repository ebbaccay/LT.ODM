// SBU overview (Products Offered) and SBU performance sample data. Products are kept in memory while the mock runs.
const set = (columns, rows) => ({ columns, rows });
const ok = (rows = 1) => [set(['rows_affected', 'result_status'], [[rows, 'Success']])];
const cost = (fabric, trims, cmt, other = 0) =>
  JSON.stringify([{ item: 'Fabric', amount: fabric }, { item: 'Trims', amount: trims }, { item: 'CMT', amount: cmt }, ...(other ? [{ item: 'Other', amount: other }] : [])]);
const sum = (json) => +JSON.parse(json).reduce((s, l) => s + l.amount, 0).toFixed(2);

let nextRecid = 20;
const product = (recid, name, style, sbu, country, code, breakdown, lead, category, location, image = '') => ({
  recid, name, style, sbu, country, code, breakdown, fob: sum(breakdown), lead, category, image, location, status: 'Active',
  createdBy: 'jdoe', created: `2026-09-${String(10 + (recid % 18)).padStart(2, '0')} 09:00:00`,
});
const products = [
  product(1, 'Slim Stretch Chino', 'CH-1001', 'SBU Vietnam', 'Vietnam', 'VN', cost(7.2, 1.4, 4.8, 0.8), 35, 'Bottoms', 'VN01'),
  product(2, 'Organic Tee', 'TS-2201', 'SBU Cambodia', 'Cambodia', 'KH', cost(2.6, 0.5, 2.4, 0.6), 28, 'Tops', 'KH01'),
  product(3, 'Denim Trucker Jacket', 'JK-3302', 'SBU China', 'China', 'CN', cost(9.1, 2.2, 6.4, 1.2), 45, 'Outerwear', 'CN01'),
  product(4, 'Cargo Pant', 'CP-4100', 'SBU Vietnam', 'Vietnam', 'VN', cost(8.3, 1.9, 5.1, 1.1), 38, 'Bottoms', 'VN01'),
  product(5, 'Linen Camp Shirt', 'SH-5120', 'SBU Vietnam', 'Vietnam', 'VN', cost(6.8, 1.1, 4.2), 32, 'Tops', 'VN01'),
  product(6, 'Rib Knit Dress', 'DR-6010', 'SBU Cambodia', 'Cambodia', 'KH', cost(5.9, 0.9, 4.6), 30, 'Dresses', 'KH01'),
  product(7, 'Quilted Liner Jacket', 'JK-3410', 'SBU China', 'China', 'CN', cost(11.4, 2.8, 7.2, 1.6), 52, 'Outerwear', 'CN01'),
  product(8, 'Canvas Tote', 'BG-7001', 'SBU Bangladesh', 'Bangladesh', 'BD', cost(2.1, 0.8, 1.6), 40, 'Bags & Accessories', 'BD01'),
  product(9, 'Fleece Hoodie', 'KN-1105', 'SBU Bangladesh', 'Bangladesh', 'BD', cost(6.4, 1.2, 3.9), 42, 'Tops', 'BD01'),
];

const bom = (fabric, trims) => JSON.stringify([
  { mat_class: 'FABRICS', part_desc: 'Shell', consump: '1', curr_id: 'RMB', price: String(fabric) },
  { mat_class: 'TRIMS', part_desc: 'Trims', consump: '1', curr_id: 'RMB', price: String(trims) },
]);
const cmt = (rmb) => JSON.stringify([{ key: 'cmt', label: 'CMT', value: String(rmb), description: '', curr_id: 'RMB', isCustom: false }]);
// ttl_production is the total production cost (RMB), not a lead time.
const gq = [
  ['Q1', 'Relaxed Taper Jean', 'DNM-2201', 'Guangzhou Apex Garments', 'China', 'CN', '16.10', 'Bottoms', bom(52, 9), cmt(35), '7.18', '96'],
  ['Q2', 'Utility Overshirt', 'SH-8803', 'Dhaka Knit Works', 'Bangladesh', 'BD', '13.40', 'Tops', bom(41, 7), cmt(28), '7.18', '76'],
];

const categories = ['Tops', 'Bottoms', 'Dresses', 'Outerwear', 'Jackets', 'Bags & Accessories'];

const productRow = (p) => [String(p.recid), p.recid, p.name, p.style, p.sbu, p.country, p.code, p.fob, p.breakdown, p.lead, p.category, p.image,
  p.location, '', p.status, p.createdBy, null, p.created, null];
const active = () => products.filter((p) => p.status === 'Active');

/** What web_rd_get_sbu_performance returns, including its quirk: quotation rows report ttl_production as avg_lead_days. */
function performance() {
  const groups = new Map();
  for (const p of active()) {
    const key = `${p.country}|${p.sbu}`;
    groups.set(key, [...(groups.get(key) ?? []), p]);
  }
  const avg = (xs) => xs.reduce((s, x) => s + x, 0) / xs.length;
  const rows = [...groups.values()].map((list) => ({
    country: list[0].country, code: list[0].code, name: list[0].sbu, count: list.length,
    fob: avg(list.map((p) => p.fob)), lead: avg(list.map((p) => p.lead ?? 0)), image: list.find((p) => p.image)?.image ?? '',
    orders: 0, quality: null, status: 'Draft',
  }));
  rows.push(
    { country: 'China', code: 'CN', name: 'Guangzhou Apex Garments', count: 0, fob: 16.1, lead: 96, image: '', orders: 4, quality: 7.5, status: 'Approved' },
    { country: 'Bangladesh', code: 'BD', name: 'Dhaka Knit Works', count: 0, fob: 13.4, lead: 76, image: '', orders: 3, quality: 10, status: 'Approved' },
    { country: 'Vietnam', code: 'VN', name: 'Saigon Stitch Co.', count: 0, fob: 0, lead: 0, image: '', orders: 2, quality: null, status: 'Pending' },
  );
  const withBoth = rows.filter((r) => r.fob > 0 && r.lead > 0);
  const [minF, maxF] = [Math.min(...withBoth.map((r) => r.fob)), Math.max(...withBoth.map((r) => r.fob))];
  const [minL, maxL] = [Math.min(...withBoth.map((r) => r.lead)), Math.max(...withBoth.map((r) => r.lead))];
  const globalLead = avg(withBoth.map((r) => r.lead));
  const scale = (v, lo, hi) => (!v ? null : hi === lo ? 8 : +(10 - ((v - lo) / (hi - lo)) * 4).toFixed(2));
  return [set(['country', 'country_code', 'sbu_name', 'product_count', 'avg_fob', 'avg_lead_days', 'latest_image', 'global_avg_lead',
    'cost_score', 'delivery_score', 'quality_score', 'order_count', 'gq_status'],
    rows.map((r) => [r.country, r.code, r.name, r.count, +r.fob.toFixed(2), +r.lead.toFixed(2), r.image, +globalLead.toFixed(2),
      scale(r.fob, minF, maxF), scale(r.lead, minL, maxL), r.quality, r.orders, r.status]))];
}

export const sbuFixtures = {
  web_rd_get_sbu_products: () => [set(['id', 'recid', 'name', 'styleCode', 'sbu', 'country', 'countryCode', 'fobPrice', 'costBreakdown', 'leadTimeDays',
    'category', 'image', 'location', 'userGroup', 'status', 'createdBy', 'modifiedBy', 'createdDate', 'modifiedDate'],
    active().sort((a, b) => b.created.localeCompare(a.created)).map(productRow))],
  web_rd_get_gq_approved_products: [set(['qid', 'model_name', 'style_id', 'factory_name', 'country', 'country_code', 'fob_price', 'category', 'bom_json',
    'cmt_json', 'rate_in_usd', 'ttl_production', 'style_image'], gq.map((g) => [...g, '']))],
  web_rd_get_prod_categories: [set(['Description'], categories.map((c) => [c]))],
  web_rd_ins_sbu_product: (p) => {
    const recid = nextRecid++;
    products.push({
      recid, name: p.product_name, style: p.style_code, sbu: p.sbu ?? '', country: p.country ?? '', code: p.country_code ?? '',
      breakdown: p.cost_breakdown ?? '[]', fob: Number(p.fob_price) || 0, lead: p.lead_time_days ?? null, category: p.category ?? '',
      image: p.image_url ?? '', location: '', status: 'Active', createdBy: 'jdoe', created: new Date().toISOString().replace('T', ' ').slice(0, 19),
    });
    return [set(['new_recid', 'result_status'], [[recid, 'Success']])];
  },
  web_rd_upd_sbu_product: (p) => {
    const row = products.find((x) => x.recid === Number(p.recid));
    if (!row) return ok(0);
    Object.assign(row, {
      name: p.product_name, style: p.style_code, sbu: p.sbu ?? '', country: p.country ?? '', code: p.country_code ?? '',
      fob: Number(p.fob_price) || 0, lead: p.lead_time_days ?? null, category: p.category ?? '',
      ...(p.cost_breakdown != null ? { breakdown: p.cost_breakdown } : {}),
      ...(p.image_url != null ? { image: p.image_url } : {}),
    });
    return ok();
  },
  web_rd_del_sbu_products_bulk: (p) => {
    const ids = String(p.recids ?? '').split(',').map(Number);
    let n = 0;
    for (const row of products) if (ids.includes(row.recid) && row.status === 'Active') (row.status = 'Deleted'), n++;
    return ok(n);
  },
  web_rd_get_sbu_performance: () => performance(),
};
