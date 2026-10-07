// Market Trends sample data: collection 31 (not analyzed yet) and 32 (analyzed, with a declining region).
const set = (columns, rows) => ({ columns, rows });

const collections = {
  31: { conceptName: 'SS27 Eco Denim Capsule', customer: 'ADIDAS', season: 'SS27', targetMarket: 'EU', targetFob: 15.5,
    activeTags: '["Sustainable","Denim"]', fabricDirection: '["Recycled cotton twill 11oz","Hemp blend chambray"]', sustainabilityNotes: '["OCS certified cotton"]' },
  32: { conceptName: 'FW27 Athleisure Knits', customer: 'LULULEMON', season: 'FW27', targetMarket: 'US', targetFob: 22,
    activeTags: '["Athleisure"]', fabricDirection: '["Brushed recycled poly fleece"]', sustainabilityNotes: '[]' },
};
const styles = {
  31: [[1, 'Denim Trucker Jacket', 'JK-3302', 'Outerwear', 'SBU China', 'China', 18.9],
       [2, 'Relaxed Taper Jean', 'DNM-2201', 'Bottoms', 'Guangzhou Apex Garments', 'China', 16.1],
       [3, 'Organic Pocket Tee', 'TS-2205', 'Tops', 'SBU Cambodia', 'Cambodia', 6.4]],
  32: [[11, 'Rib Knit Jogger', 'KN-1101', 'Bottoms', 'SBU Vietnam', 'Vietnam', 21],
       [12, 'Half-Zip Fleece', 'KN-1105', 'Tops', 'SBU Vietnam', 'Vietnam', 24.5]],
};
const sessions = {
  31: { id: 61, status: 'draft', insights: [], at: '', by: '', regions: [], scores: [] },
  32: { id: 62, status: 'complete', at: '2026-10-02 14:20', by: 'mchan',
    insights: ['Lead the US range with the jogger; it scores highest on comfort-led demand.', 'Fleece is softening in Europe: keep EU buys shallow.', 'Recycled poly is a selling point; state it on hang tags.'],
    regions: [['na', 'Athleisure', 'Comfort-led', 'Strong', 42, '#3470c8'], ['as', 'Athleisure', 'Gym to street', 'Moderate', 18, '#2ab89a'],
      ['oce', 'Outdoor knit', '', 'Emerging', 9, '#8fc4d8'], ['eu', 'Fleece', 'Softening', 'Declining', -12, '#ef4444']],
    scores: [[11, 'Rib Knit Jogger', 84, 'Athleisure,Comfort', 'Strong fit for comfort-led US demand.', 'growing', 35],
      [12, 'Half-Zip Fleece', 58, 'Fleece', 'Half-Zip Fleece sells, but fleece demand is cooling in Europe.', 'declining', -6]] },
};
const byId = (id) => Object.entries(sessions).find(([, s]) => s.id === Number(id));

export const mtFixtures = {
  web_rd_ct_list_collections: [set(['collectionRecid', 'conceptName', 'customer', 'season', 'targetFob', 'styleCount'],
    Object.entries(collections).map(([id, c]) => [Number(id), c.conceptName, c.customer, c.season, c.targetFob, styles[id].length]))],
  web_rd_ct_load_session: (p) => {
    const c = collections[p.collection_recid];
    const s = sessions[p.collection_recid];
    if (!c || !s) return [set(['sessionId', 'status'], [[-1, 'error']])];
    return [set(['sessionId', 'status', 'aiInsights', 'analyzedAt', 'analyzedBy', 'collectionRecid', 'conceptName', 'customer', 'season', 'targetMarket',
      'targetFob', 'activeTags', 'fabricDirection', 'sustainabilityNotes'],
      [[s.id, s.status, JSON.stringify(s.insights), s.at, s.by, Number(p.collection_recid), c.conceptName, c.customer, c.season, c.targetMarket,
        c.targetFob, c.activeTags, c.fabricDirection, c.sustainabilityNotes]])];
  },
  web_rd_ct_get_collection_styles: (p) => [set(['itemRecid', 'productName', 'styleCode', 'category', 'sbu', 'country', 'fobPrice', 'imageUrl'],
    (styles[p.collection_recid] ?? []).map((s) => [...s, '']))],
  web_rd_ct_clear_scores: (p) => {
    const s = byId(p.session_id)?.[1];
    if (s) Object.assign(s, { status: 'draft', regions: [], scores: [] });
    return [set(['rowsAffected', 'resultStatus'], [[1, 'Success']])];
  },
  web_rd_ct_save_region_score: (p) => {
    byId(p.session_id)?.[1].regions.push([p.region_id, p.trend_label, p.sub_label ?? '', p.strength, p.growth_pct, p.color]);
    return [set(['rowsAffected', 'resultStatus'], [[1, 'Success']])];
  },
  web_rd_ct_save_style_score: (p) => {
    byId(p.session_id)?.[1].scores.push([p.item_recid, p.style_name, p.trend_score, p.trend_tags, p.insight, p.tag_type, p.growth_pct]);
    return [set(['rowsAffected', 'resultStatus'], [[1, 'Success']])];
  },
  web_rd_ct_mark_complete: (p) => {
    const s = byId(p.session_id)?.[1];
    if (s) Object.assign(s, { status: 'complete', insights: JSON.parse(p.ai_insights || '[]'), at: new Date().toISOString().slice(0, 16).replace('T', ' '), by: 'jdoe' });
    return [set(['rowsAffected', 'resultStatus'], [[1, 'Success']])];
  },
  web_rd_ct_get_region_scores: (p) => [set(['regionId', 'trendLabel', 'subLabel', 'strength', 'growthPct', 'color'], byId(p.session_id)?.[1].regions ?? [])],
  web_rd_ct_get_style_scores: (p) => [set(['itemRecid', 'styleName', 'trendScore', 'trendTags', 'insight', 'tagType', 'growthPct'], byId(p.session_id)?.[1].scores ?? [])],
};

/** POST /api/v1/market-trends/analysis */
export function handleMarketTrends(body, json) {
  const items = body.styles ?? [];
  setTimeout(() => json(200, {
    regions: [
      { id: 'eu', label: 'Eco Denim', subLabel: 'Revival', strength: 'Strong', growthPct: 38 },
      { id: 'na', label: 'Workwear', subLabel: 'Utility', strength: 'Moderate', growthPct: 21 },
      { id: 'as', label: 'Relaxed fits', subLabel: '', strength: 'Moderate', growthPct: 15 },
      { id: 'oce', label: 'Denim', subLabel: '', strength: 'Emerging', growthPct: 6 },
      { id: 'sa', label: 'Skinny denim', subLabel: 'Fading', strength: 'Declining', growthPct: -9 },
    ],
    styles: items.map((s, i) => ({
      itemRecid: s.itemRecid, trendScore: [86, 72, 48][i % 3], trendTags: [['Eco Denim', 'Workwear'], ['Relaxed fit'], ['Basics']][i % 3],
      insight: `${s.styleName} ${['matches the recycled denim revival.', 'rides the relaxed-fit trend.', 'is a steady basic with little trend lift.'][i % 3]}`,
      tagType: ['growing', 'growing', 'emerging'][i % 3], growthPct: [32, 18, 4][i % 3],
    })),
    insights: ['Lead the EU range with the trucker jacket and the taper jean.', 'Keep the tee as a price-entry basic rather than a hero piece.', 'Call out recycled cotton on hang tags; it carries the trend story.'],
  }), 700);
}
