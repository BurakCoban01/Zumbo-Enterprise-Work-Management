import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { buildFrontend } from './build-frontend.mjs';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';
import { startStaticServer } from './static-server.mjs';

const password = 'P@ssword123';
const stamp = Date.now().toString(36);
const email = `platform007-${stamp}@zumbo.local`;
const organizationId = `platform007-org-${stamp}`;
const outputDirectory = resolve(import.meta.dirname, '../../artifacts/runtime/platform007-browser');
await mkdir(outputDirectory, { recursive: true });
await buildFrontend();

const receiverStatuses = [503, 204, 500, 500, 204];
const receiverRequests = [];
const receiver = createServer((request, response) => {
  const chunks = [];
  request.on('data', chunk => chunks.push(chunk));
  request.on('end', () => {
    receiverRequests.push({
      headers: { ...request.headers },
      body: Buffer.concat(chunks).toString('utf8')
    });
    const status = receiverStatuses.shift() || 204;
    response.writeHead(status, { 'Content-Length': '0' });
    response.end();
  });
});
await new Promise((resolveListen, reject) => {
  receiver.once('error', reject);
  receiver.listen(0, '127.0.0.1', resolveListen);
});
const receiverPort = receiver.address().port;
const receiverUrl = `http://127.0.0.1:${receiverPort}/webhooks`;

async function api(path, method = 'GET', body, token, expectedStatus) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json();
  if (expectedStatus !== undefined) {
    assert.equal(response.status, expectedStatus, payload.error?.message);
    return payload;
  }
  assert.ok(response.ok, payload.error?.message || `${method} ${path} failed with ${response.status}`);
  return payload.data;
}

async function waitForReceiverCount(count) {
  for (let attempt = 0; attempt < 300; attempt++) {
    if (receiverRequests.length >= count) return;
    await new Promise(resolveWait => setTimeout(resolveWait, 100));
  }
  throw new Error(`Expected ${count} receiver requests, observed ${receiverRequests.length}.`);
}

async function waitForDelivery(page, subscriptionId, workItemId, expectedStatus) {
  for (let attempt = 0; attempt < 300; attempt++) {
    const result = await page.evaluate(async ({ apiBaseUrl, subscriptionId }) => {
      const response = await fetch(
        `${apiBaseUrl}/api/integrations/webhooks/${subscriptionId}/deliveries?pageSize=100`,
        { credentials: 'include' });
      return { status: response.status, payload: await response.json() };
    }, { apiBaseUrl, subscriptionId });
    assert.equal(result.status, 200, result.payload.error?.message);
    const delivery = result.payload.data.items.find(item => {
      const request = receiverRequests.find(candidate => candidate.headers['x-zumbo-webhook-id'] === item.id);
      return request && JSON.parse(request.body).data.workItemId === workItemId;
    });
    if (delivery?.status === expectedStatus) return delivery;
    await new Promise(resolveWait => setTimeout(resolveWait, 100));
  }
  throw new Error(`Delivery for ${workItemId} did not reach ${expectedStatus}.`);
}

const owner = await api('/api/auth/register', 'POST', {
  username: `platform007-${stamp}`,
  email,
  password,
  organizationId
});
await api('/api/organizations', 'POST', {
  name: `PLATFORM-007 browser organization ${stamp}`,
  tenantKey: organizationId
}, owner.accessToken);
const project = await api('/api/projects', 'POST', {
  organizationId,
  key: `W${stamp.slice(-6).toUpperCase()}`,
  name: `Webhook browser ${stamp}`,
  ownerUserId: owner.user.id
}, owner.accessToken);
const board = await api('/api/boards', 'POST', {
  projectId: project.id,
  name: 'Webhook browser board',
  type: 'Kanban'
}, owner.accessToken);

const frontendUrl = new URL(frontendBaseUrl);
const staticServer = await startStaticServer(resolve(import.meta.dirname, '../dist'), {
  host: frontendUrl.hostname,
  port: Number(frontendUrl.port)
});
const browser = await chromium.launch({
  headless: true,
  ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {})
});
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();
const failures = [];
page.on('pageerror', error => failures.push(`page: ${error.message}`));
page.on('console', message => {
  if (message.type() === 'error' && !message.text().includes('Failed to load resource')) {
    failures.push(`console: ${message.text()}`);
  }
});

try {
  await page.goto(`${staticServer.origin}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await page.locator('input[autocomplete="username"]').fill(email);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.locator('form').getByRole('button', { name: /giri/i }).click();
  await page.locator('.side-nav').waitFor({ state: 'visible' });

  const receipt = await page.evaluate(async ({ apiBaseUrl, receiverUrl }) => {
    const response = await fetch(`${apiBaseUrl}/api/integrations/webhooks`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-CSRF-Token': sessionStorage.getItem('zumbo.csrfToken') || ''
      },
      body: JSON.stringify({
        name: 'PLATFORM-007 Chromium receiver',
        targetUrl: receiverUrl,
        eventScopes: ['work-item.created']
      })
    });
    return { status: response.status, payload: await response.json() };
  }, { apiBaseUrl, receiverUrl });
  assert.equal(receipt.status, 201, receipt.payload.error?.message);
  const subscription = receipt.payload.data.subscription;
  const originalSecret = receipt.payload.data.secret;
  assert.match(originalSecret, /^whsec_/);

  const firstItem = await api('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Browser webhook retry then success',
    type: 'Task',
    priority: 'High'
  }, owner.accessToken);
  await waitForReceiverCount(2);
  const firstDelivery = await waitForDelivery(page, subscription.id, firstItem.id, 'Delivered');

  const rotation = await page.evaluate(async ({ apiBaseUrl, id, expectedVersion }) => {
    const response = await fetch(`${apiBaseUrl}/api/integrations/webhooks/${id}/rotate-secret`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-CSRF-Token': sessionStorage.getItem('zumbo.csrfToken') || ''
      },
      body: JSON.stringify({ expectedVersion })
    });
    return { status: response.status, payload: await response.json() };
  }, { apiBaseUrl, id: subscription.id, expectedVersion: subscription.version });
  assert.equal(rotation.status, 200, rotation.payload.error?.message);
  const rotatedSecret = rotation.payload.data.secret;
  assert.notEqual(rotatedSecret, originalSecret);
  assert.equal(rotation.payload.data.subscription.secretVersion, 2);

  const secondItem = await api('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Browser webhook dead letter then replay',
    type: 'Task',
    priority: 'High'
  }, owner.accessToken);
  await waitForReceiverCount(4);
  const deadLetter = await waitForDelivery(page, subscription.id, secondItem.id, 'DeadLetter');
  const replay = await page.evaluate(async ({ apiBaseUrl, deliveryId }) => {
    const response = await fetch(
      `${apiBaseUrl}/api/integrations/webhooks/deliveries/${deliveryId}/replay`,
      {
        method: 'POST',
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          'X-CSRF-Token': sessionStorage.getItem('zumbo.csrfToken') || ''
        }
      });
    return { status: response.status, payload: await response.json() };
  }, { apiBaseUrl, deliveryId: deadLetter.id });
  assert.equal(replay.status, 200, replay.payload.error?.message);
  await waitForReceiverCount(5);
  const replayed = await waitForDelivery(page, subscription.id, secondItem.id, 'Delivered');

  for (const [index, request] of receiverRequests.entries()) {
    const timestamp = request.headers['x-zumbo-webhook-timestamp'];
    const secret = index < 2 ? originalSecret : rotatedSecret;
    const expected = createHmac('sha256', secret)
      .update(`${timestamp}.${request.body}`)
      .digest('hex');
    assert.equal(request.headers['x-zumbo-webhook-signature'], `v1=${expected}`);
  }
  const replayBodies = receiverRequests
    .filter(request => request.headers['x-zumbo-webhook-id'] === deadLetter.id)
    .map(request => request.body);
  assert.equal(replayBodies.length, 3);
  assert.equal(new Set(replayBodies).size, 1);
  assert.equal(receiverRequests[2].headers['x-zumbo-webhook-previous-secret-version'], '1');

  const listBody = await page.evaluate(async apiBaseUrl => {
    const response = await fetch(`${apiBaseUrl}/api/integrations/webhooks`, { credentials: 'include' });
    return await response.text();
  }, apiBaseUrl);
  assert.ok(!listBody.includes(originalSecret));
  assert.ok(!listBody.includes(rotatedSecret));

  const foreign = await api('/api/auth/register', 'POST', {
    username: `platform007-foreign-${stamp}`,
    email: `platform007-foreign-${stamp}@zumbo.local`,
    password,
    organizationId: `platform007-foreign-org-${stamp}`
  });
  await api('/api/organizations', 'POST', {
    name: `PLATFORM-007 foreign organization ${stamp}`,
    tenantKey: `platform007-foreign-org-${stamp}`
  }, foreign.accessToken);
  await api(
    `/api/integrations/webhooks/${subscription.id}`,
    'GET',
    undefined,
    foreign.accessToken,
    404);

  assert.equal(firstDelivery.attempts, 1);
  assert.equal(deadLetter.attempts, 2);
  assert.equal(replayed.status, 'Delivered');
  assert.deepEqual(failures, []);
  const result = {
    passed: true,
    browser: 'chromium',
    subscriptionId: subscription.id,
    secretVersion: 2,
    firstDeliveryStatus: firstDelivery.status,
    firstDeliveryAttempts: firstDelivery.attempts,
    deadLetterAttempts: deadLetter.attempts,
    replayStatus: replayed.status,
    receiverRequests: receiverRequests.length,
    immutableReplayPayload: new Set(replayBodies).size === 1,
    foreignTenantStatus: 404,
    plaintextSecretInList: false
  };
  await writeFile(resolve(outputDirectory, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
  console.log('PLATFORM-007 Chromium webhook workflow passed.');
} catch (error) {
  await page.screenshot({ path: resolve(outputDirectory, 'failure.png'), fullPage: true }).catch(() => {});
  await writeFile(resolve(outputDirectory, 'result.json'), `${JSON.stringify({
    passed: false,
    error: error.stack || error.message,
    failures,
    receiverRequests: receiverRequests.length
  }, null, 2)}\n`);
  throw error;
} finally {
  await context.close();
  await browser.close();
  await staticServer.close();
  await new Promise(resolveClose => receiver.close(resolveClose));
}
