// `npm run start:mock`: starts the mock API, then `ng serve` with proxy.mock.json (live reload at http://localhost:4200).
// Ctrl+C stops both.
import { spawn, spawnSync } from 'node:child_process';
import './server.mjs';

const ng = spawn('npx', ['ng', 'serve', '--proxy-config', 'proxy.mock.json', ...process.argv.slice(2)], {
  stdio: 'inherit',
  shell: process.platform === 'win32',
});
ng.on('exit', (code) => process.exit(code ?? 0));

let stopping = false;
function stop() {
  if (stopping) return;
  stopping = true;
  // On Windows ng.kill() only ends the cmd.exe wrapper and ng serve keeps running; end the whole process tree.
  if (process.platform === 'win32' && ng.pid) spawnSync('taskkill', ['/pid', String(ng.pid), '/T', '/F'], { stdio: 'ignore' });
  else ng.kill('SIGTERM');
  process.exit(0);
}
for (const signal of ['SIGINT', 'SIGTERM', 'SIGBREAK']) process.on(signal, stop);
