import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-integration-001');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = new Date().toISOString();
const admin = {
  id: 'development-admin-1',
  username: 'selin',
  email: 'selin@zumbo.local',
  organizationId: 'org-development',
  roles: ['IntegrationOperator']
};
const ordinaryUser = {
  id: 'development-user-1',
  username: 'mert',
  email: 'mert@zumbo.local',
  organizationId: 'org-development',
  roles: ['User']
};
const roles = [
  { name: 'IntegrationOperator', permissions: ['IntegrationManage'] },
  { name: 'User', permissions: [] }
];
const project = {
  id: 'project-development-1',
  organizationId: 'org-development',
  key: 'PLAT',
  name: 'Platform Delivery',
  visibility: 'Private',
  teamIds: [],
  members: [{ userId: admin.id, role: 'Developer' }],
  components: [],
  versions: [],
  releases: [],
  milestones: []
};
const task = {
  id: 'task-development-1',
  projectId: project.id,
  boardId: 'board-development-1',
  columnId: 'column-development-1',
  key: 'PLAT-142',
  title: 'Provider event reconciliation',
  description: 'Synthetic integration acceptance task.',
  acceptanceCriteria: 'Signed events remain tenant scoped.',
  type: 'Task',
  status: 'In Progress',
  priority: 'High',
  labels: ['integration'],
  checklist: [],
  relations: [],
  comments: [],
  attachments: [],
  workLogs: [],
  approvals: [],
  statusHistory: [],
  customFields: [],
  version: 1
};
const checks = [];
const failures = [];
let sequence = 1;
let connections = [{
  id: 'development-connection-1',
  name: 'Platform source',
  provider: 'GitHub',
  baseUrl: 'https://api.github.com',
  credentialFingerprint: 'a1b2c3d4e5f60708',
  webhookSecretFingerprint: '1020304050607080',
  webhookSecretVersion: 1,
  isConnected: true,
  healthStatus: 'NotChecked',
  healthErrorCode: null,
  healthCheckedAtUtc: null,
  disconnectedAtUtc: null,
  requiredScopes: ['metadata:read', 'pull_requests:read', 'commit_statuses:read'],
  createdAtUtc: now,
  updatedAtUtc: now,
  version: 1
}];
let mappings = [];
let developmentLinks = [{
  id: 'development-link-auto-1',
  connectionId: connections[0].id,
  mappingId: 'development-mapping-seeded',
  projectId: project.id,
  workItemId: task.id,
  provider: 'GitHub',
  repositoryFullName: 'zumbo/platform',
  kind: 'PullRequest',
  externalId: 'pr:141',
  title: 'Signed provider ingress',
  url: 'https://github.com/zumbo/platform/pull/141',
  branch: 'feature/PLAT-142',
  commitSha: '0123456789abcdef',
  status: 'Open',
  source: 'Webhook',
  connectionActive: true,
  lastEventAtUtc: now,
  createdAtUtc: now,
  updatedAtUtc: now,
  version: 1
}];

function envelope(data) {
  return JSON.stringify({
    success: true,
    data,
    error: null,
    correlationId: 'v3-integration-001'
  });
}

function repositories() {
  return {
    items: [{
      externalRepositoryId: 'repo-42',
      name: 'platform',
      fullName: 'zumbo/platform',
      url: 'https://github.com/zumbo/platform',
      defaultBranch: 'main'
    }],
    sourceStatus: 'Complete'
  };
}

function streamPage() {
  return { items: [], page: 1, pageSize: 50, totalCount: 0 };
}

async function createContext(currentUser, viewport) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user: currentUser, csrfToken: 'csrf-integration-001' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') {
      return route.fulfill({ status: 204, body: '' });
    }
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    let data = [];

    if (path === '/api/browser-auth/session') {
      data = { user: currentUser, csrfToken: 'csrf-integration-001' };
    } else if (path === '/api/auth/roles') data = roles;
    else if (path === '/api/auth/users') data = [admin, ordinaryUser];
    else if (path === '/api/auth/sessions' || path === '/api/auth/api-keys') data = [];
    else if (path === '/api/auth/mfa') data = { enabled: false, remainingRecoveryCodes: 0 };
    else if (path === '/api/notifications/preferences/me') {
      data = { inAppEnabled: true, emailEnabled: false, mutedTypes: [] };
    } else if (path === '/api/organizations') {
      data = [{ id: 'org-development', tenantKey: 'org-development', name: 'Zumbo Engineering' }];
    } else if (path === '/api/projects') data = [project];
    else if (path === `/api/projects/${project.id}`) data = project;
    else if (path === '/api/teams') data = [];
    else if (path === '/api/integrations/webhooks') data = [];
    else if (path === '/api/integrations/webhooks/metrics') {
      data = {
        pending: 0,
        processing: 0,
        delivered: 0,
        deadLetter: 0,
        oldestPendingAtUtc: null,
        capturedAtUtc: now
      };
    } else if (path === '/api/integrations/development' && method === 'GET') {
      data = connections;
    } else if (path === '/api/integrations/development' && method === 'POST') {
      const body = request.postDataJSON();
      sequence += 1;
      const connection = {
        ...connections[0],
        id: `development-connection-${sequence}`,
        name: body.name,
        provider: body.provider,
        baseUrl: body.baseUrl,
        credentialFingerprint: `credential-${sequence}`,
        webhookSecretFingerprint: `webhook-${sequence}`,
        webhookSecretVersion: 1,
        healthStatus: 'NotChecked',
        healthCheckedAtUtc: null,
        version: 1
      };
      connections.unshift(connection);
      data = {
        connection,
        webhookSecret: `ghsec_once_${sequence}_synthetic_value`
      };
    } else if (/^\/api\/integrations\/development\/[^/]+$/.test(path)) {
      data = connections.find(item => path.endsWith(item.id)) || null;
    } else if (/\/integrations\/development\/[^/]+\/mappings$/.test(path)
        && method === 'GET') {
      const connectionId = path.split('/')[4];
      data = mappings.filter(item => item.connectionId === connectionId);
    } else if (/\/integrations\/development\/[^/]+\/mappings$/.test(path)
        && method === 'POST') {
      const connectionId = path.split('/')[4];
      const body = request.postDataJSON();
      sequence += 1;
      const mapping = {
        id: `development-mapping-${sequence}`,
        connectionId,
        projectId: body.projectId,
        projectKey: project.key,
        projectName: project.name,
        externalRepositoryId: body.externalRepositoryId,
        repositoryName: body.repositoryName,
        repositoryFullName: body.repositoryFullName,
        repositoryUrl: body.repositoryUrl,
        defaultBranch: body.defaultBranch,
        isActive: true,
        createdAtUtc: now,
        updatedAtUtc: now,
        version: 1
      };
      mappings.push(mapping);
      data = mapping;
    } else if (/\/integrations\/development\/[^/]+\/repositories$/.test(path)) {
      data = repositories();
    } else if (/\/integrations\/development\/[^/]+\/health$/.test(path)) {
      const connection = connections.find(item => path.includes(item.id));
      connection.healthStatus = 'Healthy';
      connection.healthCheckedAtUtc = now;
      connection.version += 1;
      data = { status: 'Healthy', errorCode: null, checkedAtUtc: now };
    } else if (/\/integrations\/development\/[^/]+\/rotate-webhook-secret$/.test(path)) {
      const connection = connections.find(item => path.includes(item.id));
      connection.webhookSecretVersion += 1;
      connection.webhookSecretFingerprint = `rotated-${sequence}`;
      connection.version += 1;
      data = {
        connection: { ...connection },
        webhookSecret: 'ghsec_rotated_once_synthetic'
      };
    } else if (path === `/api/work-items/${task.id}`) data = task;
    else if (path === `/api/workflows/${project.id}`) {
      data = { statuses: [], transitions: [], issueTypeSchemes: [] };
    } else if (path === `/api/work-item-schemas/${project.id}`) {
      data = { customFields: [], layouts: [] };
    } else if (path === `/api/projects/${project.id}/sprints`) data = streamPage();
    else if (path === `/api/projects/${project.id}/work-items`) {
      data = { items: [task], page: 1, pageSize: 100, totalCount: 1 };
    } else if (path === `/api/work-items/${task.id}/collaboration`) {
      data = { watcherCount: 2, voteCount: 1, watching: false, voted: false, version: 1 };
    } else if (new RegExp(`^/api/work-items/${task.id}/(comments|attachments|worklogs|approvals|timeline|activity)$`).test(path)) {
      data = streamPage();
    } else if (path === `/api/work-items/${task.id}/development-links/mappings`) {
      data = mappings.length ? mappings : [{
        id: 'development-mapping-seeded',
        connectionId: connections[0].id,
        projectId: project.id,
        projectKey: project.key,
        projectName: project.name,
        externalRepositoryId: 'repo-42',
        repositoryName: 'platform',
        repositoryFullName: 'zumbo/platform',
        repositoryUrl: 'https://github.com/zumbo/platform',
        defaultBranch: 'main',
        isActive: true,
        version: 1
      }];
    } else if (path === `/api/work-items/${task.id}/development-links`
        && method === 'GET') {
      data = developmentLinks;
    } else if (path === `/api/work-items/${task.id}/development-links`
        && method === 'POST') {
      const body = request.postDataJSON();
      sequence += 1;
      const link = {
        id: `development-link-${sequence}`,
        connectionId: connections[0].id,
        mappingId: body.mappingId,
        projectId: project.id,
        workItemId: task.id,
        provider: 'GitHub',
        repositoryFullName: 'zumbo/platform',
        ...body,
        source: 'Manual',
        connectionActive: true,
        lastEventAtUtc: null,
        createdAtUtc: now,
        updatedAtUtc: now,
        version: 1
      };
      developmentLinks.unshift(link);
      data = link;
    }

    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: envelope(data)
    });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error'
        && !/WebSocket|signalr|Failed to load resource/.test(message.text())) {
      failures.push(`${label}: ${message.text()}`);
    }
  });
}

try {
  const desktopContext = await createContext(admin, { width: 1440, height: 1000 });
  const desktopPage = await desktopContext.newPage();
  diagnostics(desktopPage, 'desktop');
  desktopPage.on('dialog', dialog => dialog.accept());
  await desktopPage.goto(`${server.origin}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await desktopPage.getByRole('tab', { name: 'Entegrasyonlar' }).click();
  await desktopPage.getByRole('tab', { name: 'Geliştirme' }).click();
  await desktopPage.getByRole('heading', { name: 'Geliştirme bağlantıları' }).waitFor();
  await desktopPage.getByRole('heading', { name: 'Platform source' }).waitFor();
  checks.push('desktop-provider-center-role-gated');

  await desktopPage.getByRole('button', { name: 'Sağlayıcı bağla' }).click();
  await desktopPage.getByLabel('Bağlantı adı').fill('Delivery source');
  await desktopPage.getByLabel('Erişim anahtarı').fill('synthetic-provider-token-123456');
  await desktopPage.getByRole('button', { name: 'Bağla', exact: true }).click();
  await desktopPage.getByText('Webhook sırrı yalnız şimdi gösterilir', { exact: true }).waitFor();
  const secret = await desktopPage.locator('.development-layout .integration-secret > div code').last().innerText();
  assert.match(secret, /^ghsec_/);
  const storage = await desktopPage.evaluate(() => JSON.stringify({
    local: { ...localStorage },
    session: { ...sessionStorage }
  }));
  assert.doesNotMatch(storage, new RegExp(secret));
  assert.doesNotMatch(storage, /synthetic-provider-token-123456/);
  await desktopPage.getByRole('button', { name: 'Geliştirme webhook sırrını kapat' }).click();
  checks.push('desktop-create-secret-once-and-credential-memory-only');

  await desktopPage.getByRole('button', { name: 'Repository’leri getir' }).click();
  await desktopPage.getByLabel('Zumbo projesi').selectOption({ label: project.name });
  await desktopPage.locator('.development-mapping-form select').nth(1)
    .selectOption({ label: 'zumbo/platform' });
  await desktopPage.getByRole('button', { name: 'Eşleştir' }).click();
  await desktopPage.locator('.development-mapping-row')
    .getByText('zumbo/platform', { exact: true }).waitFor();
  await desktopPage.getByRole('button', { name: 'Sağlığı denetle' }).click();
  await desktopPage.getByText('Sağlıklı', { exact: true }).waitFor();
  checks.push('desktop-repository-mapping-and-health');
  assert.equal(
    await desktopPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    true
  );
  await desktopPage.screenshot({
    path: resolve(output, 'desktop-development-center.png'),
    fullPage: true
  });
  await desktopContext.close();

  const deniedContext = await createContext(ordinaryUser, { width: 1280, height: 820 });
  const deniedPage = await deniedContext.newPage();
  diagnostics(deniedPage, 'desktop-denied');
  await deniedPage.goto(`${server.origin}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await deniedPage.getByRole('heading', { name: 'Ayarlar' }).waitFor();
  assert.equal(await deniedPage.getByRole('tab', { name: 'Entegrasyonlar' }).count(), 0);
  checks.push('desktop-integration-permission-denied');
  await deniedContext.close();

  const mobileContext = await createContext(admin, { width: 390, height: 844 });
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile');
  mobilePage.on('dialog', dialog => dialog.accept());
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/profile/integrations`, {
    waitUntil: 'domcontentloaded'
  });
  await mobilePage.getByRole('tab', { name: 'Geliştirme' }).click();
  await mobilePage.getByRole('heading', { name: 'Geliştirme bağlantıları' }).waitFor();
  await mobilePage.locator('.mobile-development-row').first().click();
  await mobilePage.locator('.mobile-development-mapping').first()
    .getByText('zumbo/platform', { exact: true }).waitFor();
  assert.equal(
    await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true
  );
  await mobilePage.screenshot({
    path: resolve(output, 'mobile-development-center.png'),
    fullPage: true
  });
  checks.push('mobile-provider-management-parity');

  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/tasks/${task.id}`, {
    waitUntil: 'domcontentloaded'
  });
  await mobilePage.getByRole('heading', { name: 'Geliştirme' }).waitFor();
  await mobilePage.getByText('Signed provider ingress', { exact: true }).waitFor();
  await mobilePage.getByRole('button', { name: 'Geliştirme bağlantısı ekle' }).click();
  const developmentForm = mobilePage.locator('.mobile-task-development-form');
  await developmentForm.locator('select').first()
    .selectOption({ index: 1 });
  await developmentForm.getByLabel('Harici kimlik').fill('pr:142');
  await developmentForm.getByLabel('Başlık', { exact: true }).fill('Provider UI acceptance');
  await developmentForm.getByLabel('HTTPS bağlantısı').fill(
    'https://github.com/zumbo/platform/pull/142'
  );
  await developmentForm.getByRole('button', { name: 'Bağlantıyı ekle' }).click();
  await mobilePage.getByText('Provider UI acceptance', { exact: true }).waitFor();
  assert.equal(
    await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true
  );
  await mobilePage.screenshot({
    path: resolve(output, 'mobile-work-item-development.png'),
    fullPage: true
  });
  checks.push('mobile-work-item-manual-and-automatic-links');
  await mobileContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-INTEGRATION-001',
  passed: failures.length === 0,
  checks,
  failures
};
await writeFile(
  resolve(output, 'result.json'),
  `${JSON.stringify(result, null, 2)}\n`
);
assert.deepEqual(failures, []);
assert.equal(checks.length, 6);
console.log(
  'V3-INTEGRATION-001 browser passed: provider lifecycle, secret safety, mapping, permissions and mobile work-item parity.'
);
