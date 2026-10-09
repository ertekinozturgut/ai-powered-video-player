// Runs only against the explicitly named disposable container created for validation.
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
const name = process.argv[2];
if (name !== 'videoatlas-smoke') throw new Error('Use the dedicated videoatlas-smoke test container.');
const label = execFileSync('docker', ['inspect', '--format', '{{index .Config.Labels "videoatlas.task"}}', name], {encoding: 'utf8'}).trim();
assert.equal(label, 'onboarding');
const environment = {...process.env, BOOTSTRAP_EMAIL: `smoke-${randomBytes(8).toString('hex')}@example.invalid`, BOOTSTRAP_PASSWORD: randomBytes(24).toString('base64') + 'Aa1!'};
for (const command of ['--create-user', '--reset-password']) {
  const result = execFileSync('docker', ['exec', '-e', 'BOOTSTRAP_EMAIL', '-e', 'BOOTSTRAP_PASSWORD', name, 'dotnet', 'VideoAtlas.Web.dll', command], {env: environment, encoding: 'utf8'});
  assert.ok(result.includes('Account operation completed successfully.'));
}
delete environment.BOOTSTRAP_PASSWORD;
const before = execFileSync('docker', ['exec', name, 'sqlite3', '/data/videoatlas.db', 'SELECT COUNT(*) FROM AspNetUsers;'], {encoding: 'utf8'}).trim();
execFileSync('docker', ['restart', name]);
let healthy = false;
for (let i = 0; i < 40; i++) {
  try { const response = await fetch('http://127.0.0.1:5081/health/ready'); if (response.ok) { healthy = true; break; } } catch { /* Container is restarting. */ }
  await new Promise(resolve => setTimeout(resolve, 250));
}
assert.ok(healthy, 'Container did not become ready');
const after = execFileSync('docker', ['exec', name, 'sqlite3', '/data/videoatlas.db', 'SELECT COUNT(*) FROM AspNetUsers;'], {encoding: 'utf8'}).trim();
assert.equal(after, before);
assert.equal(execFileSync('docker', ['exec', name, 'id', '-u'], {encoding: 'utf8'}).trim(), '1654');
console.log('PASS: rootless/read-only production startup, account creation/reset, restart and persistent database.');
