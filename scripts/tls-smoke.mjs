import https from 'node:https';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
const ca = readFileSync('artifacts/smoke-root.crt');
const cookies = new Map();
async function request(path, body) {
  return new Promise((resolve, reject) => {
    const req = https.request({hostname: 'localhost', port: 5443, path, ca, method: body ? 'POST' : 'GET', headers: {
      Cookie: [...cookies].map(([k,v]) => `${k}=${v}`).join('; '),
      ...(body ? {'Content-Type': 'application/x-www-form-urlencoded', 'Content-Length': Buffer.byteLength(body)} : {})
    }}, res => {
      for (const header of res.headers['set-cookie'] ?? []) { const pair = header.split(';')[0]; const index = pair.indexOf('='); cookies.set(pair.slice(0,index), pair.slice(index+1)); }
      let text = ''; res.on('data', chunk => { text += chunk; }); res.on('end', () => resolve({status: res.statusCode, headers: res.headers, body: text}));
    });
    req.on('error', reject); req.end(body);
  });
}
const env = {...process.env, DOMAIN: 'localhost', ACME_EMAIL: 'operations@example.invalid', BOOTSTRAP_EMAIL: `tls-${randomBytes(6).toString('hex')}@example.invalid`, BOOTSTRAP_PASSWORD: randomBytes(24).toString('base64') + 'Aa1!'};
execFileSync('docker', ['compose', '-p', 'videoatlas-tls-smoke', '-f', 'compose.yaml', '-f', 'deploy/compose.smoke.yaml', 'exec', '-T', '-e', 'BOOTSTRAP_EMAIL', '-e', 'BOOTSTRAP_PASSWORD', 'app', 'dotnet', 'VideoAtlas.Web.dll', '--create-user'], {env, stdio: 'pipe'});
assert.equal((await request('/health/ready')).status, 200);
assert.equal((await request('/account/register')).status, 404);
const login = await request('/account/login'); assert.equal(login.status, 200);
const token = /name="csrf-token" content="([^"]+)"/.exec(login.body)?.[1]; assert.ok(token);
const signedIn = await request('/account/login', new URLSearchParams({Email: env.BOOTSTRAP_EMAIL, Password: env.BOOTSTRAP_PASSWORD, __RequestVerificationToken: token}).toString());
assert.equal(signedIn.status, 302);
assert.ok(signedIn.headers['set-cookie'].some(c => c.startsWith('VideoAtlas.Session=') && c.includes('secure')));
assert.equal((await request('/api/videos')).status, 200);
delete env.BOOTSTRAP_PASSWORD;
console.log('PASS: verified TLS certificate, Caddy trusted proxy, CSRF, production login and private API access.');
