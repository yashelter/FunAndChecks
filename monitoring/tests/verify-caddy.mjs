// Runtime test of the production Caddyfile against temporary local mocks.
// node monitoring/tests/verify-caddy.mjs /path/to/caddy
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { spawn } from 'node:child_process';

const executable = process.argv[2];
assert(executable, 'Pass the official Caddy binary path');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'fc-caddy-check-'));
const proxyKey = 'runtime-test-only-proxy-key';
const guards = [];
const proxied = [];
let available = true;
const backend = http.createServer((request, response) => {
  if (request.url.split('?')[0] === '/internal/monitoring/authorize') {
    guards.push({ url: request.url, headers: request.headers, method: request.method });
    assert.equal(request.method, 'GET');
    assert.equal(request.headers['x-monitoring-proxy-key'], proxyKey);
    assert(!request.headers['x-webauth-user']);
    assert(!request.headers['x-monitoring-user']);
    if (!available) {
      response.writeHead(503);
      response.end('unavailable');
    } else if ((request.headers.cookie ?? '').includes('session=admin')) {
      response.writeHead(403);
      response.end('forbidden');
    } else if ((request.headers.cookie ?? '').includes('session=super')) {
      response.writeHead(200, { 'X-Monitoring-User': 'account-42' });
      response.end();
    } else {
      response.writeHead(302, {
        Location: `/metrics?returnUrl=${encodeURIComponent(request.headers['x-monitoring-original-uri'])}`,
      });
      response.end();
    }
  } else {
    response.writeHead(200, { 'Content-Type': 'text/plain' });
    response.end(`site:${request.url}`);
  }
});
const grafana = http.createServer((request, response) => {
  proxied.push({ url: request.url, headers: request.headers });
  response.writeHead(200, { 'Content-Type': 'text/plain' });
  response.end(`grafana:${request.url}`);
});
const reserve = http.createServer();
let caddy;
let output = '';
try {
  for (const server of [backend, grafana, reserve]) {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  }
  const address = `http://127.0.0.1:${reserve.address().port}`;
  await new Promise(resolve => reserve.close(resolve));
  const configuration = '{ admin off\n auto_https off\n}\n' +
    fs.readFileSync('Caddyfile', 'utf8')
      .replaceAll('funandchecks-api:{$API_INTERNAL_PORT:8080}', `127.0.0.1:${backend.address().port}`)
      .replace('grafana:3000', `127.0.0.1:${grafana.address().port}`);
  const configurationPath = path.join(temporary, 'Caddyfile');
  fs.writeFileSync(configurationPath, configuration);
  caddy = spawn(executable, ['run', '--config', configurationPath, '--adapter', 'caddyfile'], {
    windowsHide: true, cwd: temporary,
    env: { ...process.env, APP_DOMAIN: address, MONITORING_PROXY_KEY: proxyKey },
  });
  caddy.stderr.on('data', chunk => { output += chunk; });
  caddy.stdout.on('data', () => {});
  for (let attempt = 0; ; attempt++) {
    try { await fetch(address + '/health'); break; }
    catch {
      assert(attempt < 40, `Caddy failed to start: ${output}`);
      await new Promise(resolve => setTimeout(resolve, 100));
    }
  }
  let response = await fetch(address + '/metrics', { redirect: 'manual' });
  assert.equal(response.status, 200);
  assert.equal(await response.text(), 'site:/metrics');
  assert.equal(guards.length, 0, 'Exact bridge URL must bypass the Grafana guard');
  for (const url of ['/internal', '/internal/metrics', '/internal/monitoring/authorize']) {
    response = await fetch(address + url);
    assert.equal(response.status, 404, `${url} leaked externally`);
  }
  const deepLink = '/metrics/d/fc-audit/audit?from=now-1h&to=now';
  response = await fetch(address + deepLink, {
    headers: {
      Cookie: 'session=super', Authorization: 'Bearer browser-token',
      'X-WEBAUTH-USER': 'attacker', 'X-WEBAUTH-ROLE': 'Admin',
      'X-Monitoring-User': 'attacker', 'X-Monitoring-Proxy-Key': 'attacker',
      'X-Monitoring-Original-Uri': '/attacker',
    }, redirect: 'manual',
  });
  assert.equal(response.status, 200);
  assert.equal(await response.text(), `grafana:${deepLink}`);
  assert.equal(guards.at(-1).headers['x-monitoring-original-uri'], deepLink);
  assert.equal(proxied.at(-1).headers['x-webauth-user'], 'account-42');
  assert(!proxied.at(-1).headers['x-webauth-role']);
  assert(!proxied.at(-1).headers.authorization);
  assert(!Object.keys(proxied.at(-1).headers).some(key => key.startsWith('x-monitoring-')));
  const beforeDenied = proxied.length;
  response = await fetch(address + deepLink, { redirect: 'manual' });
  assert.equal(response.status, 302);
  assert.equal(response.headers.get('location'), `/metrics?returnUrl=${encodeURIComponent(deepLink)}`);
  response = await fetch(address + deepLink, { headers: { Cookie: 'session=admin' }, redirect: 'manual' });
  assert.equal(response.status, 403);
  available = false;
  response = await fetch(address + deepLink, { headers: { Cookie: 'session=super' }, redirect: 'manual' });
  assert.equal(response.status, 503);
  assert.equal(proxied.length, beforeDenied, 'A denied or unavailable guard must never proxy to Grafana');
  backend.closeAllConnections();
  await new Promise(resolve => backend.close(resolve));
  response = await fetch(address + deepLink, { headers: { Cookie: 'session=super' }, redirect: 'manual' });
  assert.equal(response.status, 502, 'An unreachable authorization upstream must fail closed');
  assert.equal(proxied.length, beforeDenied);
  console.log('Caddy fixture passed: bridge, internal block, cookie/key/URI, identity spoof stripping, redirects and fail closed.');
} finally {
  if (caddy) {
    const exited = new Promise(resolve => caddy.once('exit', resolve));
    caddy.kill();
    await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 3000))]);
  }
  for (const server of [backend, grafana]) await new Promise(resolve => server.close(resolve));
  assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
  assert(path.basename(temporary).startsWith('fc-caddy-check-'));
  fs.rmSync(temporary, { recursive: true, force: true });
}
