#!/usr/bin/env node
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  parseEnvironment,
  repositoryRoot,
  validateLocalEnvironment
} from './prepare-env.mjs';

const seedVersion = 'demo-readiness-v1';
const resetConfirmation = 'DEMO-READINESS-V1';
const labels = {
  portfolio: '[DEMO-V1] Teslimat Portfoyu',
  initiative: '[DEMO-V1] Guvenilir Yerel Teslimat',
  goal: '[DEMO-V1] Teslimat Akisini Iyilestir',
  keyResult: '[DEMO-V1] Zamaninda Tamamlama Orani',
  capacity: '[DEMO-V1] Ekip Kapasite Plani',
  dashboard: '[DEMO-V1] Teslimat Nabzi',
  knowledge: '[DEMO-V1] Yerel Demo Runbooku',
  intake: '[DEMO-V1] Dis Talep Formu'
};
const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const environmentPath = resolve(
  repositoryRoot,
  argumentValue('--environment') || 'Backend/.env'
);
const evidencePath = resolve(
  repositoryRoot,
  argumentValue('--evidence') || 'artifacts/demo-readiness/DEMO-003.json'
);
const environment = parseEnvironment(readFileSync(environmentPath, 'utf8')).values;
const apiBaseUrl = environment.ZUMBO_API_URL;
const origin = environment.ZUMBO_FRONTEND_URL;
const adminIdentity = environment.ZUMBO_IDENTITY_ADMIN_EMAIL;
const adminPassword = process.env.ZUMBO_DEMO_ADMIN_PASSWORD
  || process.env.ZUMBO_BOOTSTRAP_ADMIN_PASSWORD;
const mode = process.argv.includes('--reset')
  ? 'reset'
  : process.argv.includes('--verify-only')
    ? 'verify'
    : 'prepare';
const changes = [];
const checks = [];
const cookieJar = new Map();
let csrfToken = '';
let actor;

try {
  validateInputs();
  actor = await login();
  const baseline = await inspectBaseline();

  if (mode === 'reset') {
    await resetSeedOwnedRecords();
  } else if (mode === 'prepare') {
    await prepareStrategicRecords(baseline);
  }

  const verification = await verifyState(baseline);
  const result = buildEvidence(true, baseline, verification);
  writeEvidence(result);
  console.log(JSON.stringify({
    passed: true,
    task: result.task,
    seedVersion,
    mode,
    baseline: result.baseline,
    strategic: result.strategic,
    changes: result.changes,
    evidence: relativeEvidencePath()
  }, null, 2));
} catch (error) {
  const result = buildEvidence(false, undefined, undefined, sanitize(error?.message || String(error)));
  writeEvidence(result);
  console.error(result.blocker);
  process.exitCode = 1;
}

function validateInputs() {
  validateLocalEnvironment(environmentPath);
  if (!adminIdentity) throw new Error('The local environment does not define the demo administrator identity.');
  if (!adminPassword) {
    throw new Error(
      'Set ZUMBO_DEMO_ADMIN_PASSWORD or ZUMBO_BOOTSTRAP_ADMIN_PASSWORD in the process environment.'
    );
  }
  if (mode === 'reset' && argumentValue('--confirm') !== resetConfirmation) {
    throw new Error(`Scoped reset requires --confirm ${resetConfirmation}.`);
  }
}

async function login() {
  const result = await api('/api/browser-auth/login', {
    method: 'POST',
    body: {
      usernameOrEmail: adminIdentity,
      password: adminPassword
    }
  });
  csrfToken = result.csrfToken || '';
  if (!result.user?.id || !result.user?.organizationId) {
    throw new Error('Browser login did not return the expected local user context.');
  }
  checks.push('authenticated-browser-session');
  return {
    id: result.user.id,
    organizationId: result.user.organizationId
  };
}

async function inspectBaseline() {
  const [usersPayload, teamsPayload, projectsPayload] = await Promise.all([
    api('/api/auth/users?search='),
    api(`/api/teams?organizationId=${encodeURIComponent(actor.organizationId)}&pageSize=100`),
    api(`/api/projects?organizationId=${encodeURIComponent(actor.organizationId)}&pageSize=100`)
  ]);
  const users = items(usersPayload);
  const teams = items(teamsPayload);
  const projects = items(projectsPayload).filter(item => !item.archived);
  const preferredProjects = projects
    .filter(project => ['ETC', 'FIN', 'DESIGN', 'OPS'].includes(project.key))
    .sort((left, right) => left.key.localeCompare(right.key));
  const projectCandidates = preferredProjects.length ? preferredProjects : projects;

  let selected;
  let selectedWorkItems = [];
  for (const project of projectCandidates) {
    const workItems = items(await api(
      `/api/work-items/?projectId=${encodeURIComponent(project.id)}&page=1&pageSize=100`
    ));
    const viewer = (project.members || []).find(member =>
      member.role === 'Viewer' && member.userId !== actor.id
    );
    if (workItems.length && viewer) {
      selected = { project, viewerId: viewer.userId };
      selectedWorkItems = workItems;
      break;
    }
  }

  if (!selected) {
    throw new Error(
      'The existing synthetic baseline must include a manageable project with work items and a Viewer member.'
    );
  }
  if (users.length < 2 || teams.length < 1 || projects.length < 1 || selectedWorkItems.length < 1) {
    throw new Error('The existing synthetic user, team, project and work baseline is incomplete.');
  }
  if (!users.some(user => user.id === selected.viewerId)) {
    throw new Error('The selected project Viewer is not present in the organization user directory.');
  }
  const boards = items(await api(`/api/boards/by-project/${encodeURIComponent(selected.project.id)}`));
  if (!boards.length) throw new Error('The selected demo project must include a board for intake routing.');

  checks.push(
    'synthetic-users-present',
    'synthetic-teams-present',
    'synthetic-projects-present',
    'synthetic-work-present',
    'viewer-membership-present'
  );
  return {
    users,
    teams,
    projects,
    project: selected.project,
    board: boards[0],
    viewerId: selected.viewerId,
    workItems: selectedWorkItems
  };
}

async function prepareStrategicRecords(baseline) {
  const portfolioPage = await api('/api/portfolios?page=1&pageSize=100');
  let portfolio = uniqueNamed(items(portfolioPage), labels.portfolio, 'portfolio');
  if (!portfolio) {
    portfolio = await api('/api/portfolios', {
      method: 'POST',
      body: {
        name: labels.portfolio,
        description: 'Yerel ve sentetik urun teslimat gorunumu.',
        viewerUserIds: [baseline.viewerId]
      }
    });
    changes.push('portfolio-created');
  }

  let initiative = uniqueNamed(
    portfolio.initiatives || [],
    labels.initiative,
    'portfolio initiative'
  );
  if (!initiative) {
    portfolio = await api(`/api/portfolios/${portfolio.id}/initiatives`, {
      method: 'POST',
      body: {
        name: labels.initiative,
        summary: 'Mevcut proje akisini stratejik hedeflerle baglar.',
        parentInitiativeId: null,
        ownerUserId: actor.id,
        status: 'Active',
        health: 'OnTrack',
        confidence: 80,
        targetAt: '2026-09-30T00:00:00Z',
        projectIds: [baseline.project.id],
        milestoneLinks: []
      }
    });
    initiative = uniqueNamed(
      portfolio.initiatives || [],
      labels.initiative,
      'portfolio initiative'
    );
    changes.push('initiative-created');
  }
  if (!initiative) throw new Error('The seed-owned initiative was not returned after preparation.');

  const goalPage = await api('/api/goals?page=1&pageSize=100');
  let goal = uniqueNamed(items(goalPage), labels.goal, 'goal');
  if (!goal) {
    goal = await api('/api/goals', {
      method: 'POST',
      body: {
        name: labels.goal,
        description: 'Yerel demo icin olculebilir teslimat hedefi.',
        periodStart: '2026-07-01',
        periodEnd: '2026-09-30',
        viewerUserIds: [baseline.viewerId],
        initiativeLinks: [{
          portfolioId: portfolio.id,
          initiativeId: initiative.id
        }],
        projectIds: [baseline.project.id]
      }
    });
    changes.push('goal-created');
  }
  let keyResult = uniqueNamed(goal.keyResults || [], labels.keyResult, 'key result');
  if (!keyResult) {
    goal = await api(`/api/goals/${goal.id}/key-results`, {
      method: 'POST',
      body: {
        name: labels.keyResult,
        description: 'Tamamlanan islerin hedef tarih uyumu.',
        ownerUserId: baseline.viewerId,
        baselineValue: 55,
        targetValue: 90,
        initialValue: 72,
        unit: '%',
        direction: 'Increase'
      }
    });
    keyResult = uniqueNamed(goal.keyResults || [], labels.keyResult, 'key result');
    changes.push('key-result-created');
  }
  if (goal.status !== 'Active') {
    goal = await api(`/api/goals/${goal.id}/status-updates`, {
      method: 'POST',
      body: {
        status: 'Active',
        health: 'OnTrack',
        confidence: 78,
        note: 'Yerel demo verisi hazirlandi ve temel akislara baglandi.'
      }
    });
    changes.push('goal-status-activated');
  }

  const capacityPage = await api('/api/capacity-plans?page=1&pageSize=100');
  let capacity = uniqueNamed(items(capacityPage), labels.capacity, 'capacity plan');
  if (!capacity) {
    capacity = await api('/api/capacity-plans', {
      method: 'POST',
      body: {
        name: labels.capacity,
        description: 'Yerel demo icin iki haftalik sentetik kapasite.',
        periodStart: '2026-08-03',
        periodEnd: '2026-08-16',
        portfolioId: portfolio.id,
        projectIds: [baseline.project.id],
        members: [{
          userId: actor.id,
          teamId: null,
          weeklyCapacityHours: 40
        }, {
          userId: baseline.viewerId,
          teamId: null,
          weeklyCapacityHours: 36
        }],
        allocations: [{
          id: null,
          userId: actor.id,
          projectId: baseline.project.id,
          startDate: '2026-08-03',
          endDate: '2026-08-16',
          percent: 65
        }, {
          id: null,
          userId: baseline.viewerId,
          projectId: baseline.project.id,
          startDate: '2026-08-03',
          endDate: '2026-08-16',
          percent: 75
        }],
        viewerUserIds: [baseline.viewerId]
      }
    });
    changes.push('capacity-plan-created');
  }

  const dashboardPage = await api('/api/dashboards?page=1&pageSize=100');
  let dashboard = uniqueNamed(items(dashboardPage), labels.dashboard, 'dashboard');
  if (!dashboard) {
    dashboard = await api('/api/dashboards', {
      method: 'POST',
      body: {
        name: labels.dashboard,
        description: 'Yerel demo icin proje teslimat ve is yuku gorunumu.',
        scope: 'Project',
        projectIds: [baseline.project.id],
        widgets: [{
          id: 'demo-v1-summary',
          type: 'ProjectSummary',
          title: 'Proje Ozeti',
          column: 1,
          row: 1,
          width: 12,
          height: 2,
          projectId: baseline.project.id,
          filter: null
        }, {
          id: 'demo-v1-workload',
          type: 'UserWorkload',
          title: 'Ekip Is Yuku',
          column: 1,
          row: 3,
          width: 12,
          height: 2,
          projectId: baseline.project.id,
          filter: null
        }],
        filter: {
          rangeDays: 30,
          dueRiskDays: 14,
          assigneeUserId: null,
          teamId: null,
          statuses: []
        }
      }
    });
    changes.push('dashboard-created');
  }
  if (!(dashboard.viewerUserIds || []).includes(baseline.viewerId)) {
    dashboard = await api(`/api/dashboards/${dashboard.id}/sharing`, {
      method: 'PUT',
      headers: { 'If-Match': `"${dashboard.version}"` },
      body: { viewerUserIds: [baseline.viewerId] }
    });
    changes.push('dashboard-shared');
  }

  const knowledgePage = await api(
    `/api/knowledge-documents?query=${encodeURIComponent(labels.knowledge)}&page=1&pageSize=100`
  );
  let knowledge = uniqueNamed(items(knowledgePage), labels.knowledge, 'knowledge document', 'title');
  if (!knowledge) {
    knowledge = await api('/api/knowledge-documents', {
      method: 'POST',
      body: {
        scopeType: 'Project',
        scopeId: baseline.project.id,
        title: labels.knowledge,
        contentMarkdown: [
          '# Yerel demo runbooku',
          '',
          'Bu sentetik belge, temel teslimat ve raporlama akislarini birbirine baglar.',
          '',
          '- Proje islerini ve planlama gorunumlerini inceleyin.',
          '- Viewer rolunun salt okunur sinirlarini dogrulayin.',
          '- Dashboard ve hedef baglantilarindan kaynaga ilerleyin.'
        ].join('\n'),
        tags: ['demo-v1', 'runbook'],
        workItemIds: [baseline.workItems[0].id],
        userIds: [baseline.viewerId],
        changeSummary: 'Kararli yerel demo veri surumu olusturuldu.'
      }
    });
    changes.push('knowledge-document-created');
  }

  const intakeForms = items(await api(`/api/intake/forms?projectId=${encodeURIComponent(baseline.project.id)}`));
  let intake = uniqueNamed(intakeForms.filter(item => item.state !== 'Archived'), labels.intake, 'intake form');
  if (!intake) {
    intake = await api('/api/intake/forms', {
      method: 'POST',
      body: {
        projectId: baseline.project.id,
        name: labels.intake,
        description: 'Musteri ve is ortaklarinin operasyon taleplerini guvenli bicimde iletmesi icin yerel demo formu.',
        definition: {
          accessPolicy: 'Public',
          boardId: baseline.board.id,
          workItemType: 'Task',
          defaultPriority: 'Medium',
          confirmationMessage: 'Talebiniz alindi. Ekip inceleme sonrasinda sizinle iletisime gececek.',
          fields: [
            { key: 'summary', label: 'Talep basligi', type: 'Text', required: true, options: [] },
            { key: 'details', label: 'Talep ayrintisi', type: 'LongText', required: true, options: [] },
            { key: 'contact_email', label: 'Iletisim e-postasi', type: 'Email', required: true, options: [] },
            { key: 'urgency', label: 'Oncelik', type: 'Choice', required: true, options: ['Normal', 'Yuksek', 'Kritik'] },
            { key: 'needed_by', label: 'Ihtiyac tarihi', type: 'Date', required: false, options: [] }
          ],
          mapping: {
            titleFieldKey: 'summary',
            descriptionFieldKey: 'details',
            priorityFieldKey: null,
            dueDateFieldKey: 'needed_by',
            customFields: []
          }
        }
      }
    });
    changes.push('public-intake-created');
  }
  if (intake.state !== 'Published') {
    intake = await api(`/api/intake/forms/${intake.id}/publish`, { method: 'POST', body: {} });
    changes.push('public-intake-published');
  }

  checks.push(
    'portfolio-and-initiative-prepared',
    'goal-and-key-result-prepared',
    'capacity-plan-prepared',
    'dashboard-prepared',
    'knowledge-document-prepared',
    'public-intake-prepared'
  );
}

async function resetSeedOwnedRecords() {
  const projects = items(await api(`/api/projects?organizationId=${encodeURIComponent(actor.organizationId)}&pageSize=100`));
  for (const project of projects) {
    const matches = named(items(await api(`/api/intake/forms?projectId=${encodeURIComponent(project.id)}`)).filter(item => item.state !== 'Archived'), labels.intake);
    if (matches.length > 1) throw new Error('Scoped reset found duplicate active seed-owned intake forms.');
    for (const form of matches) {
      await api(`/api/intake/forms/${form.id}/archive`, { method: 'POST', body: {} });
      changes.push('public-intake-archived');
    }
  }
  const resources = [
    {
      type: 'knowledge document',
      path: '/api/knowledge-documents',
      list: () => api(`/api/knowledge-documents?query=${encodeURIComponent(labels.knowledge)}&page=1&pageSize=100`),
      label: labels.knowledge,
      field: 'title'
    },
    {
      type: 'dashboard',
      path: '/api/dashboards',
      list: () => api('/api/dashboards?page=1&pageSize=100'),
      label: labels.dashboard
    },
    {
      type: 'capacity plan',
      path: '/api/capacity-plans',
      list: () => api('/api/capacity-plans?page=1&pageSize=100'),
      label: labels.capacity
    },
    {
      type: 'goal',
      path: '/api/goals',
      list: () => api('/api/goals?page=1&pageSize=100'),
      label: labels.goal
    },
    {
      type: 'portfolio',
      path: '/api/portfolios',
      list: () => api('/api/portfolios?page=1&pageSize=100'),
      label: labels.portfolio
    }
  ];

  for (const resource of resources) {
    const matches = named(items(await resource.list()), resource.label, resource.field);
    if (matches.length > 1) {
      throw new Error(`Scoped reset found duplicate active seed-owned ${resource.type} records.`);
    }
    for (const item of matches) {
      await api(`${resource.path}/${item.id}`, {
        method: 'DELETE',
        headers: { 'If-Match': `"${item.version}"` }
      });
      changes.push(`${resource.type.replaceAll(' ', '-')}-archived`);
    }
  }
  checks.push('scoped-reset-only-seed-owned-strategic-records');
}

async function verifyState(baseline) {
  const [
    portfolioPage,
    goalPage,
    capacityPage,
    dashboardPage,
    knowledgePage,
    intakeForms
  ] = await Promise.all([
    api('/api/portfolios?page=1&pageSize=100'),
    api('/api/goals?page=1&pageSize=100'),
    api('/api/capacity-plans?page=1&pageSize=100'),
    api('/api/dashboards?page=1&pageSize=100'),
    api(`/api/knowledge-documents?query=${encodeURIComponent(labels.knowledge)}&page=1&pageSize=100`),
    api(`/api/intake/forms?projectId=${encodeURIComponent(baseline.project.id)}`)
  ]);
  const activeCounts = {
    portfolios: named(items(portfolioPage), labels.portfolio).length,
    goals: named(items(goalPage), labels.goal).length,
    capacityPlans: named(items(capacityPage), labels.capacity).length,
    dashboards: named(items(dashboardPage), labels.dashboard).length,
    knowledgeDocuments: named(items(knowledgePage), labels.knowledge, 'title').length,
    publicIntakeForms: named(items(intakeForms).filter(item => item.state !== 'Archived'), labels.intake).length
  };
  const expected = mode === 'reset' ? 0 : 1;
  for (const [name, count] of Object.entries(activeCounts)) {
    if (count !== expected) {
      throw new Error(`Expected ${expected} active seed-owned ${name}, found ${count}.`);
    }
  }

  if (mode !== 'reset') {
    const portfolio = uniqueNamed(items(portfolioPage), labels.portfolio, 'portfolio');
    const goal = uniqueNamed(items(goalPage), labels.goal, 'goal');
    const capacity = uniqueNamed(items(capacityPage), labels.capacity, 'capacity plan');
    const dashboard = uniqueNamed(items(dashboardPage), labels.dashboard, 'dashboard');
    const knowledge = uniqueNamed(
      items(knowledgePage),
      labels.knowledge,
      'knowledge document',
      'title'
    );
    const intake = uniqueNamed(items(intakeForms).filter(item => item.state !== 'Archived'), labels.intake, 'intake form');
    if (named(portfolio.initiatives || [], labels.initiative).length !== 1
      || named(goal.keyResults || [], labels.keyResult).length !== 1
      || !goal.initiativeLinks?.length
      || !goal.projectIds?.includes(baseline.project.id)
      || capacity.members?.length < 2
      || capacity.allocations?.length < 2
      || dashboard.widgets?.length < 2
      || !dashboard.viewerUserIds?.includes(baseline.viewerId)
      || knowledge.currentContentVersion !== 1
      || intake.state !== 'Published'
      || intake.draft?.accessPolicy !== 'Public'
      || !intake.publicId) {
      throw new Error('One or more seed-owned demo records are incomplete.');
    }
    await api(`/api/portfolios/${portfolio.id}/roadmap`);
    await api(`/api/goals/${goal.id}/rollup`);
    await api(`/api/capacity-plans/${capacity.id}/snapshot`);
    await api(`/api/dashboards/${dashboard.id}/render`);
    await api(`/api/knowledge-documents/${knowledge.id}`);
    await api(`/api/intake/public/forms/${encodeURIComponent(intake.publicId)}`);
    checks.push('demo-read-models-ready');
  }

  checks.push('active-seed-records-unique');
  return { activeCounts };
}

async function api(path, options = {}) {
  const headers = {
    accept: 'application/json',
    origin,
    ...options.headers
  };
  if (options.body !== undefined) headers['content-type'] = 'application/json';
  if (cookieJar.size) {
    headers.cookie = [...cookieJar.entries()].map(([name, value]) => `${name}=${value}`).join('; ');
  }
  if (csrfToken && options.method && options.method !== 'GET') {
    headers['x-csrf-token'] = csrfToken;
  }

  const response = await fetch(new URL(path, apiBaseUrl), {
    method: options.method || 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    redirect: 'manual',
    signal: AbortSignal.timeout(30_000)
  });
  updateCookies(response);
  const text = await response.text();
  let payload;
  try {
    payload = text ? JSON.parse(text) : undefined;
  } catch {
    throw new Error(`${options.method || 'GET'} ${new URL(path, apiBaseUrl).pathname} returned invalid JSON.`);
  }
  if (!response.ok) {
    const code = payload?.error?.code || `HTTP_${response.status}`;
    const message = payload?.error?.message || response.statusText;
    throw new Error(`${options.method || 'GET'} ${new URL(path, apiBaseUrl).pathname} failed: ${code}: ${message}`);
  }
  return payload?.data === undefined ? payload : payload.data;
}

function updateCookies(response) {
  const values = response.headers.getSetCookie?.() || [];
  for (const value of values) {
    const [pair] = value.split(';', 1);
    const separator = pair.indexOf('=');
    if (separator > 0) {
      cookieJar.set(pair.slice(0, separator).trim(), pair.slice(separator + 1).trim());
    }
  }
}

function items(payload) {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.items)) return payload.items;
  return [];
}

function named(collection, label, field = 'name') {
  return collection.filter(item => item?.[field] === label);
}

function uniqueNamed(collection, label, type, field = 'name') {
  const matches = named(collection, label, field);
  if (matches.length > 1) throw new Error(`Duplicate active seed-owned ${type} records were found.`);
  return matches[0];
}

function buildEvidence(passed, baseline, verification, blocker = null) {
  return {
    schemaVersion: 1,
    task: 'DEMO-003',
    seedVersion,
    mode,
    generatedAtUtc: new Date().toISOString(),
    passed,
    decision: passed ? (mode === 'reset' ? 'reset' : 'ready') : 'blocked',
    baseline: baseline ? {
      users: baseline.users.length,
      teams: baseline.teams.length,
      projects: baseline.projects.length,
      selectedProjectWorkItems: baseline.workItems.length,
      selectedProjectHasViewer: true
    } : null,
    strategic: verification?.activeCounts || null,
    changes,
    checks,
    blocker,
    resetScope: 'seed-owned strategic and intake records in the authenticated synthetic organization',
    directDatabaseWrites: false,
    secretsRecorded: false,
    noDeployment: true,
    noPublicExposure: true,
    noVolumeDeletion: true,
    noGlobalCleanup: true
  };
}

function writeEvidence(result) {
  mkdirSync(dirname(evidencePath), { recursive: true });
  writeFileSync(evidencePath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
}

function relativeEvidencePath() {
  return evidencePath.slice(repositoryRoot.length + 1).replaceAll('\\', '/');
}

function sanitize(value) {
  let result = String(value);
  for (const secret of [adminPassword, adminIdentity].filter(Boolean)) {
    result = result.replaceAll(secret, '[redacted]');
  }
  return result;
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

if (scriptDirectory !== resolve(repositoryRoot, 'scripts/operations')) {
  throw new Error('The demo preparation script must run from the repository operations directory.');
}
