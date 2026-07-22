import assert from 'node:assert/strict';
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr';
import { frontendBaseUrl, requireLocalSecret } from './environment.mjs';

const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for the two-replica realtime probe');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for the two-replica realtime probe');
const password = 'P@ssword123';
const apiBaseUrl = (process.env.ZUMBO_SCALE_GATEWAY_URL || 'http://127.0.0.1:58089').replace(/\/$/, '');
const frontendOrigin = new URL(frontendBaseUrl).origin;

async function api(path, method = 'GET', body, token, headers = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json();
  return { response, payload, data: payload.data };
}

async function waitFor(predicate, timeoutMs = 30_000) {
  const expiresAt = Date.now() + timeoutMs;
  while (Date.now() < expiresAt) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error('Realtime condition was not satisfied within the bounded wait.');
}

async function authenticate() {
  let auth = await api('/api/auth/login', 'POST', {
    usernameOrEmail: adminEmail,
    password
  });
  if (auth.response.status === 401) {
    const stamp = Date.now().toString(36);
    auth = await api('/api/auth/register', 'POST', {
      username: `ops002-${stamp}`,
      email: adminEmail,
      password,
      organizationId: `ops002-org-${stamp}`,
      bootstrapToken
    });
  }
  assert.ok(auth.response.ok, auth.payload.error?.message || 'Scale-probe authentication failed');
  return auth.data;
}

function connection(token) {
  return new HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/work-items`, {
      accessTokenFactory: () => token,
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
      headers: { Origin: frontendOrigin }
    })
    .withStatefulReconnect({ bufferSize: 65_536 })
    .build();
}

function eventPromise(hub, projectId) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(
      () => reject(new Error(`Realtime event for project ${projectId} was not received within 30 seconds`)),
      30_000);
    hub.on('workItemChanged', change => {
      if (change.projectId === projectId && change.eventType === 'created') {
        globalThis.clearTimeout(timeout);
        resolve(change);
      }
    });
  });
}

const auth = await authenticate();
const stamp = Date.now().toString(36).toUpperCase();
const organizationResult = await api('/api/organizations', 'POST', {
  name: `PLATFORM-004 organization ${stamp}`,
  tenantKey: auth.user.organizationId
}, auth.accessToken);
assert.ok(organizationResult.response.ok || organizationResult.response.status === 409,
  organizationResult.payload.error?.message || 'Scale-probe organization provisioning failed');
const projectResult = await api('/api/projects', 'POST', {
  organizationId: auth.user.organizationId,
  key: `S${stamp.slice(-5)}`,
  name: `OPS-002 realtime ${stamp}`,
  ownerUserId: auth.user.id
}, auth.accessToken);
assert.ok(projectResult.response.ok, projectResult.payload.error?.message || 'Scale-probe project creation failed');

const boardResult = await api('/api/boards', 'POST', {
  projectId: projectResult.data.id,
  name: 'Scale probe board',
  type: 'Kanban'
}, auth.accessToken);
assert.ok(boardResult.response.ok, boardResult.payload.error?.message || 'Scale-probe board creation failed');

const first = connection(auth.accessToken);
const second = connection(auth.accessToken);
const firstEvents = [];
const secondEvents = [];
first.on('workItemChanged', change => firstEvents.push(change));
second.on('workItemChanged', change => secondEvents.push(change));
try {
  await first.start();
  await second.start();
  const firstInstance = await first.invoke('GetInstanceId');
  const secondInstance = await second.invoke('GetInstanceId');
  assert.notEqual(firstInstance, secondInstance, 'Realtime clients landed on the same API replica');
  assert.deepEqual(new Set([firstInstance, secondInstance]), new Set(['api-1', 'api-2']));

  await first.invoke('SubscribeProject', projectResult.data.id);
  const subscription = await second.invoke('SubscribeProject', projectResult.data.id);
  assert.equal(subscription.schemaVersion, 1);
  assert.equal(subscription.activeProjectSubscriptions, 1);
  await assert.rejects(
    second.invoke('SubscribeProject', `foreign-${stamp}`),
    /forbidden|not found|access|permission|project/i);

  const firstEventPromise = eventPromise(first, projectResult.data.id);
  const secondEventPromise = eventPromise(second, projectResult.data.id);

  const createResult = await api('/api/work-items', 'POST', {
    projectId: projectResult.data.id,
    boardId: boardResult.data.id,
    title: 'Verify Redis backplane delivery',
    type: 'Task',
    priority: 'High',
    assigneeUserId: auth.user.id,
    dueDate: new Date(Date.now() + 86_400_000).toISOString()
  }, auth.accessToken);
  assert.ok(createResult.response.ok, createResult.payload.error?.message || 'Scale-probe work item creation failed');

  const [firstEvent, secondEvent] = await Promise.all([
    firstEventPromise,
    secondEventPromise
  ]);
  assert.equal(firstEvent.workItemId, createResult.data.id);
  assert.equal(secondEvent.workItemId, createResult.data.id);
  assert.equal(firstEvent.schemaVersion, 1);
  assert.equal(secondEvent.schemaVersion, 1);
  assert.equal(firstEvent.resourceVersion, createResult.data.version);
  assert.equal(secondEvent.resourceVersion, createResult.data.version);
  assert.equal(firstEvent.workItem.version, createResult.data.version);
  const movedResult = await api(`/api/work-items/${createResult.data.id}/status`, 'PATCH', {
    status: 'In Progress'
  }, auth.accessToken, { 'If-Match': `"${createResult.data.version}"` });
  assert.ok(movedResult.response.ok, movedResult.payload.error?.message || 'Committed move failed');
  await waitFor(() => firstEvents.some(change => change.resourceVersion === movedResult.data.version)
    && secondEvents.some(change => change.resourceVersion === movedResult.data.version));

  const committedCounts = [firstEvents.length, secondEvents.length];
  const staleResult = await api(`/api/work-items/${createResult.data.id}/status`, 'PATCH', {
    status: 'Code Review'
  }, auth.accessToken, { 'If-Match': `"${createResult.data.version}"` });
  assert.equal(staleResult.response.status, 409);
  assert.equal(staleResult.payload.error?.code, 'CONCURRENCY_CONFLICT');
  await new Promise(resolve => setTimeout(resolve, 1500));
  assert.deepEqual([firstEvents.length, secondEvents.length], committedCounts,
    'A failed mutation emitted a realtime event');
  console.log(`Realtime backplane passed: ${firstInstance} and ${secondInstance} received ${createResult.data.id}.`);
} finally {
  await Promise.allSettled([first.stop(), second.stop()]);
}
