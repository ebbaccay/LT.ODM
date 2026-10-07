// In-memory /api/v1/admin + /api/v1/navigation built from db/seed/nav.menu.sql values.
const G = [
  ['nav.home', 'lucideLayoutDashboard', 'main', 1], ['nav.styleLibrary', 'lucideImages', 'main', 2], ['nav.aiStudio', 'lucideSparkles', 'main', 3], ['nav.manageOfferings', 'lucideStore', 'main', 10],
  ['nav.sbuModule', 'lucideFactory', 'main', 20], ['nav.trimcardReports', 'lucideTable', 'main', 30], ['nav.developer', 'lucideComponent', 'main', 90],
  ['nav.settings', 'lucideSettings', 'bottom', 100],
];
const I = [
  ['nav.home', 'nav.dashboard', '', 'lucideLayoutDashboard', 10, ''], ['nav.home', 'nav.quotationDashboard', 'dashboard', 'lucideChartColumn', 20, 'Admin,Merchandiser,Factory'],
  ['nav.styleLibrary', 'nav.styles', 'styles', 'lucideImages', 10, ''], ['nav.styleLibrary', 'nav.materials', 'materials', 'lucideListTree', 20, 'Admin,Merchandiser,Costing,Viewer'],
  ['nav.styleLibrary', 'nav.workmanship', 'workmanship', 'lucideTimer', 30, ''], ['nav.styleLibrary', 'nav.costing', 'costing', 'lucideCalculator', 40, ''],
  ['nav.aiStudio', 'nav.aiSearch', 'ai/search', 'lucideScanSearch', 10, 'Admin,Merchandiser,Costing,Viewer'],
  ['nav.aiStudio', 'nav.aiCompare', 'ai/compare', 'lucideGitCompareArrows', 20, 'Admin,Merchandiser,Costing,Viewer'],
  ['nav.aiStudio', 'nav.aiBomCheck', 'ai/bom-check', 'lucideShieldAlert', 30, 'Admin,Merchandiser,Costing,Viewer'],
  ['nav.aiStudio', 'nav.aiRender', 'ai/render', 'lucideWandSparkles', 40, 'Admin,Merchandiser,Costing,Viewer'],
  ['nav.aiStudio', 'nav.aiLab', 'ai/lab', 'lucideFlaskConical', 50, 'Admin,Merchandiser,Costing,Viewer'],
  ['nav.manageOfferings', 'nav.garmentQuotation', 'garment-quotation', 'lucideReceipt', 10, 'Admin,Merchandiser,Factory'],
  ['nav.manageOfferings', 'nav.conceptStudio', 'concept-studio', 'lucideLightbulb', 20, 'Admin,Merchandiser'],
  ['nav.manageOfferings', 'nav.sbuSubmission', 'order-management', 'lucideSend', 30, 'Admin,Merchandiser'],
  ['nav.manageOfferings', 'nav.collectionBuilder', 'collection-builder', 'lucideLayers', 40, 'Admin,Merchandiser'],
  ['nav.manageOfferings', 'nav.productMatching', 'product-catalog', 'lucideShapes', 50, 'Admin,Merchandiser,Viewer'],
  ['nav.manageOfferings', 'nav.costOptimization', 'cost-optimization', 'lucideTrendingDown', 60, 'Admin,Merchandiser'],
  ['nav.manageOfferings', 'nav.customerProposal', 'collection', 'lucideFileText', 70, 'Admin,Merchandiser,Viewer'],
  ['nav.manageOfferings', 'nav.marketTrends', 'product-trends', 'lucideTrendingUp', 80, 'Admin,Merchandiser,Viewer'],
  ['nav.sbuModule', 'nav.overview', 'sbu-overview', 'lucideBuilding2', 10, 'Admin,Merchandiser'],
  ['nav.sbuModule', 'nav.performanceDashboard', 'sbu-performance', 'lucideChartColumn', 20, 'Admin,Merchandiser'],
  ['nav.trimcardReports', 'nav.trimcard', 'trimcard', 'lucideTable', 10, ''], ['nav.developer', 'nav.uiReference', 'ui-reference', 'lucideComponent', 10, 'Admin'],
  ['nav.settings', 'settings.nav.navConfig', 'settings/nav-config', 'lucidePanelsTopLeft', 10, 'Admin'],
  ['nav.settings', 'settings.nav.roles', 'settings/roles', 'lucideShieldCheck', 20, 'Admin'],
  ['nav.settings', 'settings.nav.userRoles', 'settings/user-roles', 'lucideUsers', 30, 'Admin'],
  ['nav.settings', 'settings.nav.refLists', 'settings/reference-lists', 'lucideListChecks', 50, 'Admin'], ['nav.settings', 'settings.nav.aiConnections', 'settings/ai', 'lucidePlug', 60, 'Admin'],
];
let id = 1;
const groups = G.map(([text, icon, slot, sortOrder]) => ({ groupId: id++, text, icon, slot, sortOrder, isVisible: true, items: [] }));
for (const [g, text, route, icon, sortOrder, roles] of I)
  groups.find((x) => x.text === g).items.push({ itemId: id++, text, route, icon, sortOrder, isVisible: true, allowedRoles: roles ? roles.split(',') : [] });
const roles = [
  { roleId: 1, name: 'Admin', displayName: 'Administrator', description: 'Full access, including Settings.', userCount: 2 },
  { roleId: 2, name: 'Merchandiser', displayName: 'Merchandiser', description: 'Quotations, concepts and offerings.', userCount: 14 },
  { roleId: 3, name: 'Factory', displayName: 'Factory', description: 'Factory users: quotations for their factory.', userCount: 9 },
  { roleId: 4, name: 'Costing', displayName: 'Costing', description: null, userCount: 3 },
  { roleId: 5, name: 'Viewer', displayName: 'Viewer', description: 'Read-only access.', userCount: 5 },
];
const users = [
  { userId: 1, userName: 'jdoe', displayName: 'Jane Doe', email: 'jane.doe@example.test', isActive: true, userGroup: null, location: null, lastLoginUtc: '2026-10-05T07:12:00Z', lockoutEndUtc: null, roles: ['Admin'] },
  { userId: 2, userName: 'mchan', displayName: 'Mei Chan', email: 'mei.chan@example.test', isActive: true, userGroup: null, location: 'HKG', lastLoginUtc: '2026-10-04T09:30:00Z', lockoutEndUtc: null, roles: ['Merchandiser', 'Costing'] },
  { userId: 3, userName: 'fty.dg01', displayName: 'Dongguan Factory 01', email: 'dg01@factory.example', isActive: true, userGroup: 'FTY', location: 'DG01', lastLoginUtc: '2026-10-03T02:00:00Z', lockoutEndUtc: null, roles: ['Factory'] },
  { userId: 4, userName: 'rlopez', displayName: 'Ricardo Lopez', email: 'r.lopez@example.test', isActive: true, userGroup: null, location: null, lastLoginUtc: null, lockoutEndUtc: '2099-01-01T00:00:00Z', roles: [] },
  { userId: 5, userName: 'old.user', displayName: 'Former Employee', email: 'old@example.test', isActive: false, userGroup: null, location: null, lastLoginUtc: '2025-01-10T00:00:00Z', lockoutEndUtc: null, roles: ['Viewer'] },
];
// Same keys and rules as ReferenceLists.cs; rows: [code, name, sortOrder, isActive, usageCount].
const refInfo = (key, codeMaxLength, codeCase, codePattern, nameMaxLength, nameRequired, hasSortOrder, hasIsActive) =>
  ({ key, codeMaxLength, codeCase, codePattern, nameMaxLength, nameRequired, hasSortOrder, hasIsActive });
const refLists = [
  refInfo('customers', 32, '', null, 150, true, false, true), refInfo('seasons', 16, 'upper', '^[0-9]{4}-[A-Z]{2,4}$', 64, false, false, true),
  refInfo('seasonTerms', 8, 'upper', '^[A-Z]{2,4}$', 32, true, true, false), refInfo('businessUnits', 16, '', null, 100, true, false, true),
  refInfo('productTypes', 40, '', null, 100, true, false, true), refInfo('weaveTypes', 8, 'upper', null, 50, true, false, false),
  refInfo('contentClasses', 8, 'upper', '^[A-Z0-9_]{1,8}$', 50, true, true, false), refInfo('materialTypes', 16, 'upper', null, 100, true, false, false),
  refInfo('uoms', 8, 'lower', null, 50, true, false, false), refInfo('suppliers', 32, '', null, 150, true, false, true),
];
const refRows = {
  customers: [['ADI', 'adidas', null, true, 420], ['SKE', 'Skechers', null, true, 320], ['TMS', 'TMS', null, true, 263]],
  seasons: [['2028-SS', null, null, true, 52], ['2027-FW', null, null, true, 140], ['2027-SS', null, null, true, 301], ['2026-FW', null, null, false, 0]],
  seasonTerms: [['SP', 'Spring', 1, null, 1], ['SS', 'Spring/Summer', 2, null, 3], ['SU', 'Summer', 3, null, 0], ['FA', 'Fall', 4, null, 1], ['FW', 'Fall/Winter', 5, null, 3], ['WI', 'Winter', 6, null, 0]],
  businessUnits: [['ALO', 'ALO Yoga', null, true, 2], ['FTB', 'Football', null, true, 25], ['RUN', 'Running', null, true, 61]],
  productTypes: [['APPARELO', 'APPAREL OTHERS', null, true, 5], ['CROP', 'CROP', null, true, 1], ['JACKET', 'Jacket', null, true, 88]],
  weaveTypes: [['KNT', 'Knit', null, null, 531], ['WVN', 'Woven', null, null, 472]],
  contentClasses: [['FAB', 'Fabric', 1, null, 3822], ['TRI', 'Trims', 2, null, 3069], ['ACC', 'Accessories', 3, null, 7911], ['LNP', 'Labels & packaging', 4, null, 16705], ['ART', 'Artwork', 5, null, 241]],
  materialTypes: [['BADG', 'BADGE', null, null, 93], ['BOND', 'BONDING(TAPE)', null, null, 26], ['KNIT', 'KNIT', null, null, 812], ['ZIPP', 'ZIPPER', null, null, 0]],
  uoms: [['each', 'each', null, null, 38], ['kg', 'kg', null, null, 104], ['m', 'metre', null, null, 2210], ['pc', 'piece', null, null, 15480]],
  suppliers: [['1100131', 'AVERY DENNISON HONG KONG B.V.', null, true, 6], ['1100384', 'COATS HONG KONG LIMITED (APPAREL THREAD DIVISION)', null, true, 458], ['299002', 'HUAFENG (VNM)', null, true, 0]],
};
const refItem = ([code, name, sortOrder, isActive, usageCount]) => ({ code, name, sortOrder, isActive, usageCount });
const navigation = (userRoles) => ({
  groups: groups
    .filter((g) => g.isVisible)
    .map(({ isVisible, items, ...g }) => ({
      ...g,
      items: items
        .filter((i) => i.isVisible && (!i.allowedRoles.length || i.allowedRoles.some((r) => userRoles.includes(r))))
        .map(({ isVisible, allowedRoles, ...i }) => i),
    }))
    .filter((g) => g.items.length),
});

export function handleAdmin(req, url, body, json, userRoles) {
  const p = url.pathname;
  const m = req.method;
  if (p === '/api/v1/navigation') return json(200, navigation(userRoles)), true;
  if (!p.startsWith('/api/v1/admin/')) return false;
  if (!userRoles.includes('Admin')) return json(403, { title: 'Forbidden' }), true;
  if (p.endsWith('/roles') && m === 'GET') return json(200, roles), true;
  if (p.endsWith('/roles') && m === 'POST') {
    if (!body.roleId && !/^[A-Za-z][A-Za-z0-9_]{1,63}$/.test(body.name))
      return json(400, { title: 'One or more validation errors occurred.', errors: { name: ['Use 2-64 letters, digits or underscores, starting with a letter (e.g. Merchandiser).'] } }), true;
    const r = roles.find((x) => x.roleId === body.roleId);
    if (r) Object.assign(r, { displayName: body.displayName, description: body.description });
    else roles.push({ ...body, roleId: roles.length + 10, userCount: 0 });
    return json(200, { roleId: 1 }), true;
  }
  if (p.includes('/roles/') && m === 'DELETE') {
    const name = decodeURIComponent(p.split('/').pop());
    if (name === 'Admin') return json(400, { title: 'The Admin role cannot be deleted.' }), true;
    roles.splice(roles.findIndex((x) => x.name === name), 1);
    return json(204), true;
  }
  if (p.endsWith('/users') && m === 'POST') {
    if (users.some((u) => u.userName.toLowerCase() === body.userName.toLowerCase()))
      return json(400, { title: 'A user with this user name already exists.' }), true;
    users.push({ userId: users.length + 1, userName: body.userName, displayName: body.displayName, email: body.email, isActive: true, userGroup: body.userGroup, location: body.location, lastLoginUtc: null, lockoutEndUtc: null, roles: body.roles });
    console.log('invite', JSON.stringify(body));
    return json(200, { userId: users.length }), true;
  }
  if (p.endsWith('/password-link') && m === 'POST') return json(204), true;
  if (p.endsWith('/users')) return json(200, users), true;
  if (p.endsWith('/access') && m === 'PUT') {
    const uid = Number(p.split('/')[5]);
    if (uid === 1 && !body.roles.includes('Admin')) return json(400, { title: 'One or more validation errors occurred.', errors: { roles: ['You cannot remove your own Admin role. Ask another administrator.'] } }), true;
    Object.assign(users.find((x) => x.userId === uid), { roles: body.roles, userGroup: body.userGroup, location: body.location });
    return json(204), true;
  }
  if (p.endsWith('/admin/ref-lists') && m === 'GET') return json(200, refLists), true;
  if (p.includes('/admin/ref-lists/')) {
    const info = refLists.find((l) => l.key === p.split('/').pop());
    if (!info) return json(404, {}), true;
    const rows = refRows[info.key];
    if (m === 'GET') return json(200, rows.map(refItem)), true;
    if (m === 'POST') {
      let code = String(body.code ?? '').trim();
      code = info.codeCase === 'upper' ? code.toUpperCase() : info.codeCase === 'lower' ? code.toLowerCase() : code;
      if (!code || code.length > info.codeMaxLength || (info.codePattern && !new RegExp(info.codePattern).test(code)))
        return json(400, { title: 'One or more validation errors occurred.', errors: { code: ['Check the code format.'] } }), true;
      const row = rows.find((r) => r[0] === code);
      if (body.isNew && row) return json(400, { title: 'This code already exists in the list.' }), true;
      const values = [code, body.name?.trim() || null, info.hasSortOrder ? body.sortOrder : null, info.hasIsActive ? body.isActive ?? true : null];
      if (row) row.splice(0, 4, ...values);
      else rows.push([...values, 0]);
      return json(200, { code }), true;
    }
    if (m === 'DELETE') {
      const i = rows.findIndex((r) => r[0] === url.searchParams.get('code'));
      if (i >= 0 && rows[i][4] > 0)
        return json(400, { title: 'This code is still in use (see the Used by column). Change what uses it first, or make it inactive where the list allows.' }), true;
      if (i >= 0) rows.splice(i, 1);
      return json(204), true;
    }
  }
  if (p.endsWith('/menu')) return json(200, groups), true;
  if (p.endsWith('/menu/groups') && m === 'POST') {
    const g = groups.find((x) => x.groupId === body.groupId);
    if (g) Object.assign(g, body);
    else groups.push({ ...body, groupId: id++, items: [] });
    return json(200, { groupId: 1 }), true;
  }
  if (p.endsWith('/menu/items') && m === 'POST') {
    const route = body.route.replace(/^\/+|\/+$/g, '');
    if (groups.flatMap((g) => g.items).some((i) => i.route === route && i.itemId !== body.itemId))
      return json(400, { title: 'Another menu item already uses this route.' }), true;
    for (const g of groups) g.items = g.items.filter((i) => i.itemId !== body.itemId);
    groups.find((g) => g.groupId === body.groupId).items.push({
      itemId: body.itemId || id++, text: body.text, route, icon: body.icon, sortOrder: body.sortOrder, isVisible: body.isVisible, allowedRoles: body.roles,
    });
    return json(200, { itemId: 1 }), true;
  }
  if (p.includes('/menu/groups/') && m === 'DELETE') {
    groups.splice(groups.findIndex((g) => g.groupId === Number(p.split('/').pop())), 1);
    return json(204), true;
  }
  if (p.includes('/menu/items/') && m === 'DELETE') {
    for (const g of groups) g.items = g.items.filter((i) => i.itemId !== Number(p.split('/').pop()));
    return json(204), true;
  }
  return json(404, {}), true;
}
