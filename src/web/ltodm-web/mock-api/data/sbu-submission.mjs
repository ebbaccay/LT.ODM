const set = (columns, rows) => ({ columns, rows });
const cols = ['id', 'recid', 'conceptRecid', 'conceptName', 'productRecid', 'productName', 'styleCode', 'sbu', 'imageUrl', 'fobPrice', 'costBreakdown',
  'targetFob', 'notes', 'status', 'statusReason', 'username', 'location', 'userGroup', 'createdDate', 'modifiedDate', 'submittedDate'];
const cb = (f, t, o, c, m) => JSON.stringify([{ item: 'Fabric', amount: f }, { item: 'Trims', amount: t }, { item: 'Other Cost', amount: o }, { item: 'Fty FOB', amount: c }, { item: 'FTY Margin', amount: m }]);
const costOpt = JSON.stringify({ costOptimization: true, originalFob: 17.2, originalCosts: [{ item: 'Fabric', amount: 8.1 }, { item: 'Trims', amount: 1.2 }, { item: 'CMT', amount: 5.4 }],
  aiSuggestions: [{ item: 'Switch to 10oz twill', amount: 0.9 }, { item: 'Consolidate labels', amount: 0.25 }] });
const row = (recid, name, style, sbu, fob, breakdown, status, user, loc, group, notes = '', reason = '') =>
  [String(recid), recid, 101, 'SS27 Eco Denim Capsule', 0, name, style, sbu, '', fob, breakdown, 15.5, notes, status, reason, user, loc, group, '2026-10-01 09:00:00', null, status === 'Draft' ? null : '2026-10-02 10:00:00'];

export const moFixtures = {
  web_rd_get_sbu_submissions: [set(cols, [
    row(1, 'Relaxed Taper Jean', 'DNM-2201', 'Guangzhou Apex Garments', 14.38, cb(7.11, 0.56, 0.09, 5.08, 1.54), 'Submitted', 'fty.dg01', 'DG01', 'FTY', 'Can hold price for 5k+ units.'),
    row(2, 'Denim Trucker Jacket', 'JK-3302', 'SBU China', 18.9, cb(9.2, 1.1, 0.4, 6.2, 2.0), 'Accepted', 'mchan', 'HKG', '', '', 'Approved for Collection Builder'),
    row(3, 'Utility Jogger', 'CP-4100', 'Shanghai Star Textiles', 16.1, costOpt, 'For Review', 'fty.sh02', 'SH02', 'FTY', '', 'Sent back to factory for revision'),
    row(4, 'Cargo Pant', 'CP-4101', 'SBU Vietnam', 16.4, cb(8, 1, 0.5, 5.5, 1.4), 'Draft', 'jdoe', '', '', 'Draft from me'),
    row(5, 'Chore Jacket', 'JK-3310', 'Guangzhou Apex Garments', 19.6, cb(9.6, 1.3, 0.3, 6.4, 2.0), 'Factory Submitted', 'fty.dg01', 'DG01', 'FTY'),
    row(6, 'Organic Tee', 'TS-2201', 'SBU Cambodia', 6.1, cb(2.6, 0.4, 0.1, 2.1, 0.9), 'Rejected', 'mchan', 'HKG', '', '', 'Rejected during concept review'),
  ])],
  web_rd_ins_sbu_submission: [set(['new_recid', 'result_status'], [[7, 'Success']])],
  web_rd_upd_sbu_submission: [set(['rows_affected', 'result_status'], [[1, 'Success']])],
  web_rd_submit_sbu_submission: [set(['rows_affected', 'result_status'], [[1, 'Success']])],
  web_rd_del_sbu_submission: [set(['rows_affected', 'result_status'], [[1, 'Success']])],
  web_rd_co_fty_submit_proposal: [set(['result_status'], [['Success']])],
};
