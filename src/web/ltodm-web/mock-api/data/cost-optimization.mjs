// Stateful cost-optimization mock: one session per qid, suggestions saved by batch, reviews.
const set = (columns, rows) => ({ columns, rows });
const sessions = new Map(); // qid -> session
let nextSession = 40, nextSuggestion = 900;

const sessionRow = (s) => set(
  ['sessionId', 'gqQid', 'styleName', 'targetFob', 'currentFob', 'fabricCost', 'trimCost', 'laborCost', 'overheadCost', 'marginCost', 'sessionStatus',
    'latestReviewStatus', 'latestSentToName', 'latestReviewerComment', 'resultStatus'],
  [[s.id, s.qid, s.styleName, s.target, s.current, s.costs.fabric, s.costs.trim, s.costs.labor, s.costs.overhead, s.costs.margin, s.status,
    s.review, s.sentTo, s.comment, 'Success']]);
const byId = (id) => [...sessions.values()].find((s) => s.id === Number(id));

// Rejected session for item 3 (Organic Pocket Tee) to show that banner.
sessions.set('item-3', { id: 39, qid: 'item-3', styleName: 'Organic Pocket Tee', target: 15.5, current: 6.4,
  costs: { fabric: 2.6, trim: 0.4, labor: 2.1, overhead: 0.1, margin: 1.2 }, status: 'rejected', review: 'rejected', sentTo: 'Cambodia Knit Co.',
  comment: 'Cannot switch yarn supplier this season.', suggestions: [{ id: 801, category: 'Fabric', title: 'Use 150 GSM jersey', detail: '', fromPrice: 2.6, toPrice: 2.3, saving: 0.3, applied: true }] });

export const coFixtures = {
  web_rd_co_load_session: (p) => {
    let s = sessions.get(p.gq_qid);
    if (!s) {
      s = { id: nextSession++, qid: p.gq_qid, styleName: p.style_name, target: p.target_fob ?? 0, current: p.current_fob ?? 0,
        costs: { fabric: p.fabric_cost ?? 0, trim: p.trim_cost ?? 0, labor: p.labor_cost ?? 0, overhead: p.overhead_cost ?? 0, margin: p.margin_cost ?? 0 },
        status: 'draft', review: '', sentTo: '', comment: '', suggestions: [] };
      sessions.set(p.gq_qid, s);
    }
    return [sessionRow(s)];
  },
  web_rd_co_get_suggestions: (p) => [set(['suggestionId', 'category', 'title', 'detail', 'fromPrice', 'toPrice', 'saving', 'isApplied'],
    (byId(p.session_id)?.suggestions ?? []).map((g) => [g.id, g.category, g.title, g.detail, g.fromPrice, g.toPrice, g.saving, g.applied ? 1 : 0]))],
  web_rd_co_clear_suggestions: (p) => {
    const s = byId(p.session_id);
    if (!s || s.status !== 'draft') return [set(['rowsDeleted', 'resultStatus'], [[0, 'Session not found or not in draft status']])];
    s.suggestions = [];
    return [set(['rowsDeleted', 'resultStatus'], [[1, 'Success']])];
  },
  web_rd_co_save_suggestion: (p) => {
    const s = byId(p.session_id);
    const id = nextSuggestion++;
    s?.suggestions.push({ id, category: p.category, title: p.title, detail: p.detail, fromPrice: p.from_price, toPrice: p.to_price, saving: p.saving, applied: false });
    return [set(['suggestionId', 'resultStatus'], [[id, 'Success']])];
  },
  web_rd_co_toggle_suggestion: (p) => {
    for (const s of sessions.values()) for (const g of s.suggestions) if (g.id === Number(p.suggestion_id)) g.applied = !!p.is_applied;
    return [set(['rowsAffected', 'resultStatus'], [[1, 'Success']])];
  },
  web_rd_co_submit_review: (p) => {
    const s = byId(p.session_id);
    if (!s || s.status !== 'draft') return [set(['reviewId', 'resultStatus'], [[0, 'Session not found or already submitted']])];
    Object.assign(s, { status: 'under_review', review: 'pending', sentTo: p.sent_to_name });
    return [set(['reviewId', 'sentToName', 'resultStatus'], [[5, p.sent_to_name, 'Success']])];
  },
  web_rd_co_ins_sbu_review: () => [set(['newRecid', 'resultStatus'], [[77, 'Success']])],
  web_rd_co_recall_review: (p) => {
    const s = byId(p.session_id);
    if (s) Object.assign(s, { status: 'draft', review: 'recalled', sentTo: '' });
    return [set(['rowsAffected', 'resultStatus'], [[1, 'Success']])];
  },
  web_rd_get_factory_list: [set(['Partner_ID', 'Full_name'], [['DG01', 'Guangzhou Apex Garments'], ['SH02', 'Shanghai Star Textiles'], ['KH03', 'Cambodia Knit Co.'], ['VN04', 'Saigon Denim Works']])],
};

export function handleCostOptimization(body, json) {
  console.log('suggestions', JSON.stringify(body));
  setTimeout(() => json(200, [
    { category: 'Fabric', title: 'Use 10oz recycled twill instead of 11oz', detail: 'Lighter weight keeps the hand feel and cuts yardage cost.', fromPrice: body.fabricCost, toPrice: +(body.fabricCost * 0.9).toFixed(2), saving: +(body.fabricCost * 0.1).toFixed(2) },
    { category: 'Trim', title: 'Switch metal shank to nylon snap', detail: 'Same function, lower unit cost.', fromPrice: body.trimCost, toPrice: +(body.trimCost * 0.75).toFixed(2), saving: +(body.trimCost * 0.25).toFixed(2) },
    { category: 'Labor', title: 'Simplify rear pocket construction', detail: 'Removes two operations (about 1.2 SMV).', fromPrice: body.laborCost, toPrice: +(body.laborCost * 0.92).toFixed(2), saving: +(body.laborCost * 0.08).toFixed(2) },
  ]), 500);
}
