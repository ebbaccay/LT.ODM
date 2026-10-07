// LT ODM mock API - for running the web app on a laptop without SQL Server, the .NET API or an AI key.
// DEVELOPMENT ONLY: invented sample data, no real sign-in check, listens on 127.0.0.1 only. Never deploy it.
//
//   npm run start:mock        app + mock together (http://localhost:4200)
//   npm run mock:api          the mock alone (http://127.0.0.1:4300), e.g. with `ng serve --proxy-config proxy.mock.json`
//
// Who you are signed in as (set before starting):
//   MOCK_ROLES=Admin            comma-separated roles: Admin, Merchandiser, Factory, Costing, Viewer
//   MOCK_GROUP=FTY MOCK_LOCATION=SH02   sign in as a factory user of factory SH02
//
// What is mocked: sign-in (any user name and password work), menu and Settings, the TMS procedure hub (/hubs/sp)
// for the ported screens, Concept Studio AI brief + image upload, Cost Optimization AI ideas, Market Trends AI analysis, SBU product photos, notifications (empty).
// Changes are kept in memory and reset when the mock restarts.
import fs from 'node:fs';
import http from 'node:http';
import { WebSocketServer } from 'ws';
import { handleAdmin } from './data/admin.mjs';
import { cbFixtures } from './data/collection-builder.mjs';
import { csFixtures, handleConceptStudio, handleImages } from './data/concept-studio.mjs';
import { coFixtures, handleCostOptimization } from './data/cost-optimization.mjs';
import { moFixtures } from './data/sbu-submission.mjs';
import { handleMarketTrends, mtFixtures } from './data/market-trends.mjs';
import { sbuFixtures } from './data/sbu-overview.mjs';

const host = '127.0.0.1';
const port = Number(process.env.MOCK_PORT ?? 4300);
const RS = '\x1e'; // SignalR JSON protocol record separator

/** Stored procedure name -> result sets ([{ columns, rows }]) or a function of the call parameters. */
const fixtures = {
  ...JSON.parse(fs.readFileSync(new URL('./data/garment-quotation.json', import.meta.url), 'utf8')),
  ...csFixtures,
  ...moFixtures,
  ...cbFixtures,
  ...coFixtures,
  ...mtFixtures,
  ...sbuFixtures,
};

const user = {
  userId: 1,
  userName: 'jdoe',
  email: 'jane.doe@example.test',
  displayName: 'Jane Doe',
  roles: (process.env.MOCK_ROLES ?? 'Admin').split(',').map((r) => r.trim()).filter(Boolean),
  mustChangePassword: false,
  userGroup: process.env.MOCK_GROUP || null,
  location: process.env.MOCK_LOCATION || null,
};
let signedIn = true;
const session = () => ({ accessToken: 'mock.access.token', accessTokenExpiresUtc: new Date(Date.now() + 15 * 60_000).toISOString(), user });

const readBody = (req) =>
  new Promise((resolve) => {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks)));
  });
const parseJson = (buf) => {
  try {
    return buf.length ? JSON.parse(buf.toString()) : {};
  } catch {
    return {};
  }
};

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${host}`);
  const p = url.pathname;
  const json = (status, body) => {
    res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' });
    res.end(body === undefined ? '' : JSON.stringify(body));
  };
  const raw = await readBody(req);
  log(req.method, p);

  // ── Sign-in ────────────────────────────────────────────────
  if (p === '/api/v1/auth/login') {
    signedIn = true;
    return json(200, session());
  }
  if (p === '/api/v1/auth/refresh') return signedIn ? json(200, session()) : json(401, { title: 'Unauthorized' });
  if (p === '/api/v1/auth/logout') {
    signedIn = false;
    return json(204);
  }
  if (p === '/api/v1/auth/me') return signedIn ? json(200, user) : json(401, { title: 'Unauthorized' });
  if (p === '/api/v1/auth/password-policy')
    return json(200, { minLength: 12, maxLength: 128, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true, historyCount: 5 });
  if (p === '/api/v1/auth/forgot-password') return json(202);
  if (p === '/api/v1/auth/reset-password/validate') return json(200, { valid: true });
  if (p === '/api/v1/auth/reset-password' || p === '/api/v1/auth/change-password') return json(204);

  if (!signedIn && p.startsWith('/api/')) return json(401, { title: 'Unauthorized' });

  // ── App ────────────────────────────────────────────────────
  if (p === '/api/v1/client-config') return json(200, { agGridLicenseKey: process.env.MOCK_AGGRID_KEY ?? '' });
  if (p === '/health') return json(200, { status: 'Healthy', checks: [{ name: 'sqlserver', status: 'Healthy' }] });
  if (p.startsWith('/api/v1/navigation') || p.startsWith('/api/v1/admin/')) return void handleAdmin(req, url, parseJson(raw), json, user.roles);
  if (p === '/api/v1/cost-optimization/suggestions') return void handleCostOptimization(parseJson(raw), json);
  if (p === '/api/v1/market-trends/analysis') return void handleMarketTrends(parseJson(raw), json);
  if (p.startsWith('/api/v1/sbu-products/')) return void handleImages('/api/v1/sbu-products', req, url, raw, res, json);
  if (p.startsWith('/api/v1/concept-studio/')) return void handleConceptStudio(req, url, raw, res, json);

  // ── SignalR negotiate (the hubs themselves are WebSockets, below) ──
  if (p.startsWith('/hubs/') && p.endsWith('/negotiate'))
    return json(200, { negotiateVersion: 1, connectionId: 'mock', connectionToken: 'mock', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] });

  json(404, { title: `The mock has no ${req.method} ${p}` });
});

// ── TMS procedure hub (/hubs/sp) and notifications hub, SignalR JSON protocol ──
const wss = new WebSocketServer({ noServer: true });
server.on('upgrade', (req, socket, head) => {
  if (!req.url.startsWith('/hubs/')) return socket.destroy();
  wss.handleUpgrade(req, socket, head, (ws) => wss.emit('connection', ws));
});

function execution(proc, params) {
  const f = fixtures[proc];
  const sets = (typeof f === 'function' ? f(params ?? {}) : f) ?? [];
  return { status: 'Success', data: sets.map((s) => ({ columns: s.columns, rows: s.rows, rowCount: s.rows.length })) };
}

wss.on('connection', (ws) => {
  let handshaken = false;
  const send = (msg) => ws.send(JSON.stringify(msg) + RS);
  ws.on('message', (data) => {
    for (const frame of data.toString().split(RS).filter(Boolean)) {
      const msg = JSON.parse(frame);
      if (!handshaken) {
        handshaken = true;
        ws.send('{}' + RS);
        continue;
      }
      if (msg.type === 6) continue; // ping
      if (msg.type !== 1) continue;
      if (msg.target === 'ExecuteStoredProcFlat') {
        const call = msg.arguments[0] ?? {};
        const name = String(call.SpName ?? call.spName ?? '');
        const proc = name.split('.').pop();
        const params = call.Parameters;
        log('hub', proc, params ? JSON.stringify(params).slice(0, 200) : '');
        if (!(proc in fixtures)) log('hub', `  (no sample data for ${proc}: empty result)`);
        if (msg.invocationId) send({ type: 3, invocationId: msg.invocationId, result: null });
        // Batch calls (Parameters is an array) answer one execution per row, like the real hub.
        const result = Array.isArray(params) ? params.map((row) => execution(proc, row)) : execution(proc, params);
        setTimeout(() => send({ type: 1, target: 'StoredProcResultFlat', arguments: [{ procedure: name, data: result }] }), 150);
      } else if (msg.target === 'LoadNotificationHistory') {
        send({ type: 1, target: 'NotificationHistory', arguments: ['[]'] });
        if (msg.invocationId) send({ type: 3, invocationId: msg.invocationId, result: null });
      } else if (msg.invocationId) {
        send({ type: 3, invocationId: msg.invocationId, result: null });
      }
    }
  });
});

function log(...parts) {
  if (process.env.MOCK_QUIET) return;
  console.log('[mock]', ...parts);
}

server.listen(port, host, () => {
  console.log(`[mock] LT ODM mock API on http://${host}:${port}  (signed in as ${user.userName}, roles: ${user.roles.join(', ') || 'none'}` +
    (user.userGroup ? `, group ${user.userGroup}/${user.location ?? '-'}` : '') + ')');
});
