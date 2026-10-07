// Concept Studio mock: hub fixtures (flat result sets) + /api/v1/concept-studio endpoints.
const set = (columns, rows) => ({ columns, rows });

const concepts = [
  [101, 'SS27 Eco Denim Capsule', 'ADIDAS', 'P001', 'SS27', 'S27', 'EU', '15.50', '["Sustainable","Denim"]',
   'A relaxed, low-impact denim capsule built on recycled cotton twill and laser finishing, aimed at EU lifestyle stores.',
   '["Relaxed Taper Jean - $15.20","Denim Overshirt - $17.80","Utility Jogger - $13.90"]', '["Recycled cotton twill 11oz","Hemp blend chambray"]',
   '["OCS certified cotton","Laser finishing saves 60% water"]', '["/api/v1/concept-studio/images/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg"]',
   'jdoe', 'HKG', '', 'Active', '2026-09-28 10:15:00', null, true],
  [102, 'FW27 Athleisure Knits', 'LULULEMON', 'P002', 'FW27', 'F27', 'US', '22', '["Athleisure"]',
   'Soft-touch performance knits for studio-to-street wear.', '["Rib Knit Jogger - $21.00"]', '[]', '[]', '[]',
   'jdoe', 'HKG', '', 'Active', '2026-09-30 16:40:00', null, true],
  // A teammate's concept: listed for the team, but only its creator or an Admin may change it.
  [104, 'SS28 Resort Linen', 'H&M', 'P003', 'SS28', 'S28', 'Japan', '12', '["Quiet Luxury"]',
   'Breathable linen-blend resort pieces in a muted sand palette.', '["Camp Collar Shirt - $11.40"]', '[]', '[]', '[]',
   'mchan', 'HKG', '', 'Active', '2026-10-02 09:05:00', null, false],
];

export const csFixtures = {
  web_rd_get_customers: [set(['Partner_Id', 'Full_Name'], [['P001', 'ADIDAS'], ['P002', 'LULULEMON'], ['P003', 'H&M'], ['P004', 'UNIQLO']])],
  web_rd_get_seasons: [set(['Season_id', 'Description'], [['S27', 'SS27'], ['F27', 'FW27'], ['S28', 'SS28']])],
  web_rd_get_cs_trend_tags: [set(['tag_id', 'tag_name', 'sort_order'], [[1, 'Sustainable', 1], [2, 'Athleisure', 2], [3, 'Denim', 3], [4, 'Quiet Luxury', 4], [5, 'Gorpcore', 5], [6, 'Y2K', 6]])],
  web_rd_get_cs_target_markets: [set(['market_id', 'market_name', 'sort_order'], [[1, 'EU', 1], [2, 'US', 2], [3, 'Japan', 3]])],
  web_rd_get_sbu_products: [set(['recid', 'name', 'styleCode', 'sbu', 'fobPrice', 'category', 'image'], [
    [1, 'Slim Stretch Chino', 'CH-1001', 'SBU Vietnam', '14.20', 'Bottoms', ''],
    [2, 'Organic Tee', 'TS-2201', 'SBU Cambodia', '6.10', 'Tops', ''],
    [3, 'Denim Trucker Jacket', 'JK-3302', 'SBU China', '18.90', 'Outerwear', ''],
    [4, 'Cargo Pant', 'CP-4100', 'SBU Vietnam', '16.40', 'Bottoms', ''],
  ])],
  web_rd_get_gq_approved_products: [set(['qid', 'model_name', 'style_id', 'factory_name', 'fob_price', 'category', 'style_image', 'bom_json', 'cmt_json', 'rate_in_usd'], [
    ['Q1', 'Relaxed Taper Jean', 'DNM-2201', 'Guangzhou Apex Garments', '14.38', 'Bottoms', '', '[]', '[]', '7.18'],
  ])],
  web_rd_get_concept_studio_results: [set(['recid', 'concept_name', 'customer', 'customer_id', 'season', 'season_id', 'target_market', 'fob_price', 'active_tags',
    'concept_brief', 'suggested_products', 'fabric_direction', 'sustainability_notes', 'inspiration_images', 'createdBy', 'location', 'user_group', 'status', 'created_date', 'modified_date', 'can_edit'], concepts)],
  web_rd_ins_concept_studio_result: [set(['result_status', 'new_recid'], [['Success', 103]])],
  web_rd_upd_concept_studio_result: [set(['result_status'], [['Success']])],
  web_rd_del_concept_studio_result: [set(['result_status'], [['Success']])],
  web_rd_del_collection_builder: [set(['result_status'], [['No record found']])],
  web_rd_ins_cs_trend_tag: [set(['result_status'], [['Success']])],
};

/** Placeholder for images that were not uploaded in this run (a soft grey-blue SVG swatch). */
const placeholder = Buffer.from(
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300"><rect width="400" height="300" fill="#c7d2e3"/>' +
    '<path d="M0 230 L120 150 L200 200 L290 120 L400 210 V300 H0Z" fill="#9fb0c9"/><circle cx="300" cy="80" r="28" fill="#e8edf5"/></svg>',
);
/** Images uploaded while the mock runs (kept in memory only). */
const uploads = new Map();
let n = 0;

/** The file part of a multipart/form-data body (enough for the screen's single "file" field). */
function filePart(raw, contentType) {
  const boundary = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(contentType ?? '');
  if (!boundary) return null;
  const marker = Buffer.from(`--${boundary[1] ?? boundary[2]}`);
  const start = raw.indexOf(Buffer.from('\r\n\r\n'), raw.indexOf(marker));
  const end = raw.indexOf(marker, start);
  return start < 0 || end < 0 ? null : raw.subarray(start + 4, end - 2);
}

export function handleConceptStudio(req, url, raw, res, json) {
  const p = url.pathname;
  if (!p.startsWith('/api/v1/concept-studio/')) return false;
  if (p.endsWith('/draft') && req.method === 'POST') {
    const body = JSON.parse(raw.toString() || '{}');
    console.log('draft', JSON.stringify(body));
    setTimeout(() => json(200, {
      summary: `${body.name || 'This'} pairs relaxed silhouettes with low-impact denim for ${body.targetMarket || 'global'} lifestyle retail, priced around a $${body.targetPrice || '15'} FOB.`,
      products: ['Relaxed Taper Jean - $15.20', 'Denim Overshirt - $17.80', 'Utility Jogger - $13.90', 'Chore Jacket - $19.50'],
      fabrics: ['Recycled cotton twill 11oz', 'Hemp blend chambray', 'Tencel denim 9oz'],
      notes: ['OCS certified cotton', 'Laser finishing instead of stone wash', 'Recycled polyester labels'],
    }), 600);
    return true;
  }
  return handleImages('/api/v1/concept-studio', req, url, raw, res, json);
}

/** POST {prefix}/images and GET {prefix}/images/{name}: in-memory uploads (Concept Studio, SBU products). */
export function handleImages(prefix, req, url, raw, res, json) {
  const p = url.pathname;
  if (p === `${prefix}/images` && req.method === 'POST') {
    const file = filePart(raw, req.headers['content-type']);
    if (!file?.length) return json(400, { title: 'Choose an image to upload.' }), true;
    const name = `${String(++n).padStart(32, 'b')}.jpg`;
    uploads.set(name, file);
    return json(200, { imageUrl: `${prefix}/images/${name}` }), true;
  }
  if (p.startsWith(`${prefix}/images/`) && req.method === 'GET') {
    const file = uploads.get(p.split('/').pop());
    res.writeHead(200, { 'content-type': file ? 'image/jpeg' : 'image/svg+xml' });
    res.end(file ?? placeholder);
    return true;
  }
  return json(404, {}), true;
}
