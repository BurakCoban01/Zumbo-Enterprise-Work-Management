import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, relative, resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';
import {
  normalizePath,
  selectMostSpecificOperations
} from './api-consumer-matching.mjs';

const routeInventoryPath = 'Backend/tests/Zumbo.ApiTests/RouteInventory.approved.txt';
const routeTestPath = 'Backend/tests/Zumbo.ApiTests/RouteInventoryCharacterizationTests.cs';
const openApiPath = 'contracts/openapi.v1.json';
const permissionCatalogPath =
  'Backend/src/Zumbo.BuildingBlocks.Application/Security/PermissionCatalog.cs';
const consumerOwnershipPath = 'docs/product/api-consumer-ownership.json';
const outputJsonPath = 'docs/product/api-ui-capability-matrix.json';
const outputMarkdownPath = 'docs/product/api-ui-capability-matrix.md';
const frontendRoots = ['Frontend/desktop-bulma', 'Frontend/mobile-ionic'];
const supportedMethods = new Set(['get', 'post', 'put', 'patch', 'delete']);

const operations = parseRouteInventory(read(routeInventoryPath));
const openApiOperations = parseOpenApi(JSON.parse(read(openApiPath)));
const permissionCatalog = parsePermissionCatalog(read(permissionCatalogPath));
const consumerOwnership = parseConsumerOwnership(
  JSON.parse(read(consumerOwnershipPath)));
const frontendCalls = frontendRoots.flatMap(root => listJavaScript(root).flatMap(path => extractApiCalls(path, read(path))));

const contractOperations = new Set(operations.filter(operation => operation.method !== '*').map(operation => operation.id));
assert.deepEqual([...openApiOperations].sort(compareCodePoints), [...contractOperations].sort(compareCodePoints),
  'OpenAPI operations must exactly match the non-transport route inventory.');

for (const call of frontendCalls) {
  const candidates = selectMostSpecificOperations(
    call.pattern,
    operations.filter(operation => operation.method === call.method));
  const ownership = consumerOwnership.get(consumerOwnershipKey(call));
  if (candidates.length > 1) {
    assert.ok(
      ownership,
      `Ambiguous frontend API call needs explicit ownership: ${call.source}:${call.line} ${call.method} ${call.pattern}`);
  }
  if (ownership) {
    assert.equal(
      ownership.used,
      false,
      `Consumer ownership policy is used twice: ${ownership.id}`);
    const candidateIds = new Set(candidates.map(operation => operation.id));
    assert.ok(
      ownership.operationIds.every(operationId => candidateIds.has(operationId)),
      `Consumer ownership policy selects a non-candidate operation: ${ownership.id}`);
    ownership.used = true;
    call.operationIds = [...ownership.operationIds].sort(compareCodePoints);
    call.ownershipPolicy = ownership.id;
  } else {
    call.operationIds = candidates
      .map(operation => operation.id)
      .sort(compareCodePoints);
    call.ownershipPolicy = null;
  }
  assert.ok(call.operationIds.length > 0, `Frontend API call does not match the route inventory: ${call.source}:${call.line} ${call.method} ${call.pattern}`);
}
assert.ok(
  [...consumerOwnership.values()].every(policy => policy.used),
  'Every explicit consumer ownership policy must match exactly one current frontend call.');

const matrixOperations = operations.map(operation => buildOperation(operation, frontendCalls));
assert.equal(new Set(matrixOperations.map(operation => operation.id)).size, matrixOperations.length, 'Duplicate operation IDs are forbidden.');
for (const operation of matrixOperations) validateOperation(operation);

const backgroundCapabilities = buildBackgroundCapabilities();
const informationArchitecture = buildInformationArchitecture();
const capabilityGaps = buildCapabilityGaps(matrixOperations);
const summary = buildSummary(matrixOperations, frontendCalls, backgroundCapabilities, capabilityGaps);
const checking = process.argv.includes('--check');
const generatedAtUtc = checking && exists(outputJsonPath)
  ? JSON.parse(read(outputJsonPath)).generatedAtUtc
  : new Date().toISOString();
const sourceHashes = {
  routeInventorySha256: sha256(read(routeInventoryPath)),
  openApiSha256: sha256(read(openApiPath)),
  permissionCatalogSha256: sha256(read(permissionCatalogPath)),
  consumerOwnershipSha256: sha256(read(consumerOwnershipPath)),
  frontendCallsSha256: sha256(Buffer.from(frontendCalls.map(call => `${call.source}:${call.line}|${call.method}|${call.pattern}`).join('\n'), 'utf8'))
};

const matrix = {
  schemaVersion: 2,
  task: 'V3-PRODUCT-001',
  driftGate: 'V3-HARDEN-007',
  generatedAtUtc,
  statusModel: {
    surfaced: 'The exact operation has both desktop and mobile UI consumers.',
    partial: 'The exact operation has one UI consumer and remains a deliberate parity opportunity.',
    absent: 'No current UI consumer exists; the operation is explicitly assigned to a target product surface.',
    intentional: 'The operation is deliberately non-UI and has an integration, transport or operator consumer.'
  },
  sources: {
    routeInventory: routeInventoryPath,
    routeCharacterizationTest: routeTestPath,
    openApi: openApiPath,
    permissionCatalog: permissionCatalogPath,
    consumerOwnership: consumerOwnershipPath,
    frontendRoots,
    frontendParity: 'docs/frontend-parity.json'
  },
  sourceHashes,
  summary,
  informationArchitecture,
  capabilityGaps,
  backgroundCapabilities,
  operations: matrixOperations
};

const json = `${JSON.stringify(matrix, null, 2)}\n`;
const markdown = renderMarkdown(matrix);
if (checking) {
  assert.equal(read(outputJsonPath), json, `${outputJsonPath} is stale; run this generator without --check.`);
  assert.equal(read(outputMarkdownPath), markdown, `${outputMarkdownPath} is stale; run this generator without --check.`);
  console.log(`V3-PRODUCT-001 matrix is current: ${summary.operations} operations, ${summary.frontendCalls} frontend calls, ${summary.gapCapabilities} scored capability gaps.`);
} else {
  write(outputJsonPath, json);
  write(outputMarkdownPath, markdown);
  console.log(`Generated ${outputJsonPath} and ${outputMarkdownPath}: ${summary.operations} operations.`);
}

function buildOperation(operation, calls) {
  const matchedCalls = calls.filter(call => call.operationIds.includes(operation.id));
  const shared = sharedUiConsumers(operation);
  const desktop = [...matchedCalls.filter(call => call.surface === 'desktop').map(toConsumer), ...shared.desktop];
  const mobile = [...matchedCalls.filter(call => call.surface === 'mobile').map(toConsumer), ...shared.mobile];
  const policy = intentionalPolicy(operation);
  const status = policy ? 'intentional' : desktop.length && mobile.length ? 'surfaced' : desktop.length || mobile.length ? 'partial' : 'absent';
  const targetSurface = status === 'absent' ? targetSurfaceFor(operation) : null;
  const documentation = operation.method === '*'
    ? documentationForTransport(operation.path)
    : [openApiPath];

  return {
    id: operation.id,
    method: operation.method,
    path: operation.path,
    capability: capabilityFor(operation),
    tag: operation.tag || 'Transport',
    auth: operation.auth,
    permission: operation.permission,
    rateLimit: operation.rate,
    status,
    consumers: {
      desktop,
      mobile,
      admin: desktop.filter(consumer => isAdministrative(operation, consumer)),
      integration: policy?.consumer === 'integration' ? [{ route: operation.path, source: openApiPath }] : [],
      background: policy?.consumer === 'background' ? [{ route: operation.path, source: policy.source }] : []
    },
    targetSurface,
    intentionalReason: policy?.reason ?? null,
    intentionalKind: policy?.consumer ?? null,
    test: routeTestPath,
    documentation,
    source: routeInventoryPath
  };
}

function validateOperation(operation) {
  assert.ok(operation.path !== '', `${operation.id} has an invalid route.`);
  assert.ok(['surfaced', 'partial', 'absent', 'intentional'].includes(operation.status), `${operation.id} is unclassified.`);
  assert.ok(operation.permission, `${operation.id} is missing permission metadata.`);
  validatePermission(operation);
  assert.ok(operation.test && exists(operation.test), `${operation.id} is missing test evidence.`);
  assert.ok(operation.documentation.length > 0 && operation.documentation.every(exists), `${operation.id} is missing documentation evidence.`);
  const consumerCount = Object.values(operation.consumers).reduce((total, values) => total + values.length, 0);
  if (operation.status === 'intentional') {
    assert.ok(operation.intentionalReason && consumerCount > 0, `${operation.id} needs a non-UI reason and consumer.`);
    assert.ok(
      ['integration', 'background'].includes(operation.intentionalKind),
      `${operation.id} needs a typed non-UI owner.`);
  } else if (operation.status === 'absent') {
    assert.ok(operation.targetSurface, `${operation.id} needs an owned target surface.`);
  } else {
    assert.ok(operation.consumers.desktop.length || operation.consumers.mobile.length, `${operation.id} needs a UI consumer.`);
  }
}

function parseRouteInventory(text) {
  return text.split(/\r?\n/).filter(Boolean).map((line, index) => {
    const cells = line.split('|');
    assert.equal(cells.length, 6, `Unexpected route inventory row ${index + 1}: ${line}`);
    const [method, path, auth, permission, rate, tag] = cells;
    const normalizedMethod = method.toUpperCase();
    const normalizedPath = normalizePath(path);
    return {
      id: `${normalizedMethod} ${normalizedPath}`,
      method: normalizedMethod,
      path: normalizedPath,
      auth: auth.replace(/^auth=/, ''),
      permission: permission.replace(/^permission=/, ''),
      rate: rate.replace(/^rate=/, ''),
      tag: tag.replace(/^tags=/, '')
    };
  });
}

function parseOpenApi(document) {
  const result = new Set();
  for (const [path, definition] of Object.entries(document.paths)) {
    for (const method of Object.keys(definition)) {
      if (supportedMethods.has(method)) result.add(`${method.toUpperCase()} ${normalizePath(path)}`);
    }
  }
  return result;
}

function parsePermissionCatalog(source) {
  return new Set(
    [...source.matchAll(/public const string \w+ = "([^"]+)";/g)]
      .map(match => match[1]));
}

function parseConsumerOwnership(document) {
  assert.equal(document.schemaVersion, 1);
  assert.ok(Array.isArray(document.policies) && document.policies.length > 0);
  const policies = new Map();
  for (const policy of document.policies) {
    assert.match(policy.id, /^[a-z0-9-]+$/);
    assert.ok(exists(policy.source));
    assert.ok(['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(policy.method));
    assert.ok(policy.pattern.startsWith('/api/'));
    assert.ok(Array.isArray(policy.operationIds) && policy.operationIds.length > 1);
    assert.ok(policy.reason && policy.reason.length >= 20);
    const key = consumerOwnershipKey(policy);
    assert.equal(policies.has(key), false, `Duplicate consumer ownership key: ${key}`);
    policies.set(key, { ...policy, used: false });
  }
  return policies;
}

function consumerOwnershipKey(value) {
  return `${value.source}|${value.method}|${normalizePath(value.pattern)}`;
}

function validatePermission(operation) {
  if (operation.permission === 'none') return;
  const match = operation.permission.match(/^(.+):(global|resource)$/);
  assert.ok(match, `${operation.id} has malformed permission metadata.`);
  assert.ok(
    permissionCatalog.has(match[1]),
    `${operation.id} references an unknown permission: ${match[1]}`);
}

function extractApiCalls(path, source) {
  const calls = [];
  const pattern = /apiClient\.(get|post|put|patch|delete|upload|download)\s*\(/g;
  for (const match of source.matchAll(pattern)) {
    const expression = readFirstArgument(source, match.index + match[0].length);
    const routePattern = routeExpressionPattern(expression);
    const clientMethod = match[1];
    calls.push({
      source: path,
      line: source.slice(0, match.index).split('\n').length,
      surface: path.includes('/desktop-bulma/') ? 'desktop' : 'mobile',
      method: clientMethod === 'upload' ? 'POST' : clientMethod === 'download' ? 'GET' : clientMethod.toUpperCase(),
      clientMethod,
      expression: expression.replace(/\s+/g, ' ').trim(),
      pattern: routePattern.display,
      matcher: routePattern.matcher
    });
  }
  return calls;
}

function readFirstArgument(source, start) {
  let quote = null;
  let escaped = false;
  const stack = [];
  for (let index = start; index < source.length; index += 1) {
    const char = source[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (char === '\\') escaped = true;
      else if (char === quote) quote = null;
      continue;
    }
    if (char === '\'' || char === '"' || char === '`') { quote = char; continue; }
    if (char === '(' || char === '[' || char === '{') { stack.push(char); continue; }
    if (char === ')' || char === ']' || char === '}') {
      if (!stack.length && char === ')') return source.slice(start, index).trim();
      stack.pop();
      continue;
    }
    if (char === ',' && !stack.length) return source.slice(start, index).trim();
  }
  throw new Error('Unterminated apiClient call.');
}

function routeExpressionPattern(expression) {
  const tokens = splitTopLevel(expression, '+');
  const pieces = [];
  for (const token of tokens) {
    const literal = parseLiteral(token.trim());
    if (literal === null) pieces.push({ dynamic: true });
    else pieces.push({ literal });
  }

  let display = '';
  let regex = '';
  let stoppedAtQuery = false;
  for (const piece of pieces) {
    if (stoppedAtQuery) break;
    if (piece.dynamic) {
      display += '{*}';
      regex += '[^/]*';
      continue;
    }
    const queryIndex = piece.literal.indexOf('?');
    const value = queryIndex >= 0 ? piece.literal.slice(0, queryIndex) : piece.literal;
    display += value;
    regex += escapeRegExp(value);
    stoppedAtQuery = queryIndex >= 0;
  }
  assert.ok(display.startsWith('/api/'), `Only API paths may be mapped: ${expression}`);
  display = normalizePath(display).replaceAll('{*}{*}', '{*}');
  regex = regex.replace(/\/$/, '');
  return { display, matcher: new RegExp(`^${regex}/?$`) };
}

function splitTopLevel(value, delimiter) {
  const result = [];
  let start = 0;
  let quote = null;
  let escaped = false;
  const stack = [];
  for (let index = 0; index < value.length; index += 1) {
    const char = value[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (char === '\\') escaped = true;
      else if (char === quote) quote = null;
      continue;
    }
    if (char === '\'' || char === '"' || char === '`') { quote = char; continue; }
    if (char === '(' || char === '[' || char === '{') stack.push(char);
    else if (char === ')' || char === ']' || char === '}') stack.pop();
    else if (char === delimiter && !stack.length) { result.push(value.slice(start, index)); start = index + 1; }
  }
  result.push(value.slice(start));
  return result;
}

function parseLiteral(token) {
  if (token.length < 2 || !['\'', '"'].includes(token[0]) || token.at(-1) !== token[0]) return null;
  const body = token.slice(1, -1);
  return body.replace(/\\(['"\\])/g, '$1').replace(/\\n/g, '\n').replace(/\\r/g, '\r');
}

function toConsumer(call) {
  return {
    route: surfaceRouteFor(call),
    source: `${call.source}:${call.line}`,
    requestPattern: `${call.method} ${call.pattern}`,
    ownershipPolicy: call.ownershipPolicy
  };
}

function surfaceRouteFor(call) {
  if (call.surface === 'mobile') {
    if (call.pattern.startsWith('/api/integrations/')) return '/profile/integrations';
    if (call.source.endsWith('/goal-center.js')) return '/goals';
    if (call.source.endsWith('/capacity-center.js')) return '/capacity';
    if (call.source.endsWith('/knowledge-center.js')) return '/knowledge';
    if (call.source.endsWith('/auth.js')) return call.pattern.includes('forgot') ? '/forgot-password' : call.pattern.includes('reset') ? '/reset-password' : '/login';
    if (call.pattern.includes('/notifications')) return '/app/notifications';
    if (call.pattern.includes('/teams/')) return '/teams/:teamId';
    if (call.pattern.includes('/projects/') && !call.pattern.includes('/work-items')) return '/projects/:projectId';
    return call.pattern.includes('/work-items/') ? '/tasks/:taskId' : '/app/tasks';
  }
  if (call.source.endsWith('/integration-center.js')) return '/settings';
  if (call.source.endsWith('/goal-center.js')) return '/goals';
  if (call.source.endsWith('/capacity-center.js')) return '/capacity';
  if (call.source.endsWith('/knowledge-center.js')) return '/knowledge';
  if (call.source.endsWith('/settings.js')) return '/settings';
  if (call.source.endsWith('/management.js')) return call.pattern.includes('/teams') ? '/teams' : call.pattern.includes('/projects') ? '/projects' : '/board';
  if (call.source.endsWith('/planning.js')) return '/board?view=planning';
  if (call.source.endsWith('/work-items.js')) return '/board?panel=work-item';
  return '/board';
}

function intentionalPolicy(operation) {
  if (operation.path === '/') return { consumer: 'integration', source: openApiPath, reason: 'API discovery response for clients and operators; it is not an authenticated product destination.' };
  if (operation.path.startsWith('/health/')) return { consumer: 'background', source: 'docs/runbooks/daily-use.md', reason: 'Operator and orchestrator health probe; it is not an authenticated product action.' };
  if (operation.path.startsWith('/hubs/work-items')) return { consumer: 'background', source: 'Frontend/shared/realtime-client.js', reason: 'SignalR transport endpoint consumed by the realtime client rather than direct navigation.' };
  if (operation.method === 'POST'
      && operation.path === '/api/integrations/development/{connectionId}/webhook') {
    return {
      consumer: 'integration',
      source: openApiPath,
      reason: 'Signed Git provider ingress consumed by configured GitHub or GitLab webhooks; it is not an authenticated product action.'
    };
  }
  if (['/api/auth/register', '/api/auth/login', '/api/auth/refresh', '/api/auth/logout'].includes(operation.path)) {
    return { consumer: 'integration', source: openApiPath, reason: 'Bearer-client identity contract; browser surfaces deliberately use the browser-auth BFF endpoints.' };
  }
  return null;
}

function sharedUiConsumers(operation) {
  if (operation.id !== 'POST /api/browser-auth/refresh') return { desktop: [], mobile: [] };
  const sourcePath = 'Frontend/shared/api-client.js';
  const source = `${sourcePath}:${sourceLine(
    sourcePath,
    "'/api/browser-auth/refresh'")}`;
  const requestPattern = 'POST /api/browser-auth/refresh';
  return {
    desktop: [{
      route: '/board',
      source,
      requestPattern,
      ownershipPolicy: 'shared-browser-refresh'
    }],
    mobile: [{
      route: '/app/dashboard',
      source,
      requestPattern,
      ownershipPolicy: 'shared-browser-refresh'
    }]
  };
}

function sourceLine(path, marker) {
  const line = read(path).split('\n').findIndex(value => value.includes(marker)) + 1;
  assert.ok(line > 0, `Shared consumer marker is missing: ${path} ${marker}`);
  return line;
}

function documentationForTransport(path) {
  return path.startsWith('/health/') ? ['docs/runbooks/daily-use.md'] : ['docs/security/authorization.md'];
}

function targetSurfaceFor(operation) {
  const path = operation.path;
  if (operation.permission.includes(':global') || path.includes('/operations/') || path.includes('/durable-messaging/') || path.includes('/search/rebuild') || path.includes('/search/reconcile')) return 'desktop-admin/operations';
  if (path.startsWith('/api/integrations/')) return 'desktop-admin/integrations';
  if (path.includes('/privacy/')) return 'desktop-settings/privacy';
  if (path.includes('/sessions')) return 'desktop-settings/sessions';
  if (/\/projects\/\{projectId\}\/(versions|milestones|components|templates)/.test(path)) return 'desktop-project/catalogs';
  if (path.includes('/notifications/deliveries')) return 'desktop-admin/notifications';
  if (path.includes('/recurrences') || path.includes('/work-items/templates')) return 'desktop-project/automation';
  return 'desktop-product/backlog';
}

function capabilityFor(operation) {
  const path = operation.path;
  const rules = [
    ['project-catalogs', /\/projects\/\{projectId\}\/(versions|milestones|components|templates)/],
    ['development-integrations', /\/integrations\/development|\/development-links/],
    ['webhook-integrations', /\/integrations\/webhooks/],
    ['search-operations', /\/search\/(rebuild|reconcile)/],
    ['durable-messaging-operations', /durable-messaging/],
    ['privacy-jobs', /\/privacy\//],
    ['session-security', /\/sessions/],
    ['recurring-work', /\/recurrences/],
    ['work-item-templates', /\/work-items\/templates/],
    ['work-item-collaboration', /\/(approvals|watch|vote|relations)/],
    ['notification-delivery', /\/notifications\/deliveries/],
    ['health-and-realtime-transport', /^\/(health|hubs)\//]
  ];
  return rules.find(([, pattern]) => pattern.test(path))?.[0] ?? (operation.tag || 'transport').toLowerCase();
}

function isAdministrative(operation, consumer) {
  return operation.permission.includes(':global') || consumer.route === '/settings' || operation.path.startsWith('/api/organizations/');
}

function buildBackgroundCapabilities() {
  const items = [
    ['search-index-initializer', 'SearchIndexInitializer', 'Backend/src/Zumbo.Api/Hosting/ApiHostRegistration.cs', 'Backend/tests/Zumbo.UnitTests/WorkItemSearchTests.cs'],
    ['mongo-index-initializer', 'MongoIndexInitializer', 'Backend/src/Zumbo.Api/Hosting/ApiHostRegistration.cs', 'Backend/tests/Zumbo.ApiTests/RouteInventoryCharacterizationTests.cs'],
    ['durable-event-worker', 'DurableEventWorker', 'Backend/src/Zumbo.BuildingBlocks.Infrastructure/Messaging/DurableEventProcessing.cs', 'Backend/tests/Zumbo.UnitTests/DurableMessagingTests.cs'],
    ['attachment-security-maintenance', 'AttachmentSecurityMaintenanceHostedService', 'Backend/src/Zumbo.Api/AttachmentSecurityMaintenanceHostedService.cs', 'Backend/tests/Zumbo.ApiTests/AttachmentSecurityTests.cs'],
    ['due-date-reminders', 'DueDateReminderHostedService', 'Backend/src/Zumbo.Api/WorkItemDueDateReminderHostedService.cs', 'Backend/tests/Zumbo.ApiTests/WorkItemCollaborationRecurrenceApiTests.cs'],
    ['recurrence-scheduler', 'WorkItemRecurrenceSchedulerHostedService', 'Backend/src/Zumbo.Api/WorkItemRecurrenceSchedulerHostedService.cs', 'Backend/tests/Zumbo.ApiTests/WorkItemCollaborationRecurrenceApiTests.cs'],
    ['webhook-dispatcher', 'WebhookDispatcherHostedService', 'Backend/src/Zumbo.Api/WebhookAdapters.cs', 'Backend/tests/Zumbo.ApiTests/WebhookApiTests.cs'],
    ['notification-email-dispatcher', 'NotificationEmailDispatcherHostedService', 'Backend/src/Zumbo.Api/NotificationAdapters.cs', 'Backend/tests/Zumbo.PersistenceIntegrationTests/MailpitNotificationDeliveryTests.cs'],
    ['development-webhook-receipt-retention', 'DevelopmentWebhookReceiptRetentionHostedService', 'Backend/src/Zumbo.Api/DevelopmentWebhookReceiptRetentionHostedService.cs', 'Backend/tests/Zumbo.UnitTests/DevelopmentIntegrationServiceTests.cs']
  ].map(([id, service, source, test]) => ({ id, service, consumer: 'background', status: 'intentional', source, test, documentation: 'readme.md' }));
  for (const item of items) {
    assert.ok(exists(item.source) && read(item.source).includes(item.service), `Background source missing for ${item.service}.`);
    assert.ok(exists(item.test), `Background test missing for ${item.service}.`);
    assert.ok(exists(item.documentation), `Background documentation missing for ${item.service}.`);
  }
  return items;
}

function buildInformationArchitecture() {
  return {
    desktop: {
      shell: 'Frontend/desktop-bulma/index.html',
      routes: ['/board', '/projects', '/portfolios', '/goals', '/capacity', '/knowledge', '/teams', '/reports', '/audit', '/archive', '/settings'],
      projectViews: ['overview', 'board', 'list', 'backlog', 'sprint', 'calendar', 'timeline', 'roadmap', 'catalog', 'intake', 'automation', 'jobs', 'workload', 'reports', 'dashboards']
    },
    mobile: {
      router: 'Frontend/mobile-ionic/app.js',
      routes: ['/login', '/forgot-password', '/reset-password', '/intake/:publicId', '/projects/:projectId', '/projects/:projectId/catalog', '/projects/:projectId/intake', '/projects/:projectId/automation', '/projects/:projectId/jobs', '/profile/integrations', '/portfolios', '/goals', '/capacity', '/knowledge', '/teams/:teamId', '/tasks/:taskId', '/app/dashboard', '/app/projects', '/app/tasks', '/app/notifications', '/app/profile'],
      taskViews: ['my', 'backlog', 'sprint', 'board', 'list']
    },
    planned: {
      global: ['/home', '/my-work', '/inbox', '/search', '/admin'],
      project: [],
      administration: ['/admin/integrations', '/admin/operations', '/admin/notification-delivery']
    }
  };
}

function buildCapabilityGaps(items) {
  const definitions = [
    ['project-catalogs', 'Project releases, milestones, components and templates', 'desktop-project/catalogs', { frequency: 4, impact: 5, backendReadiness: 5, differentiation: 4, riskReduction: 3, implementationCost: 4, operationalCost: 5 }],
    ['work-item-collaboration', 'Watch, vote, approvals and relationship completion', 'desktop-product/work-item', { frequency: 5, impact: 5, backendReadiness: 5, differentiation: 4, riskReduction: 4, implementationCost: 4, operationalCost: 5 }],
    ['webhook-integrations', 'Webhook subscriptions, delivery health and replay', 'desktop-admin/integrations', { frequency: 3, impact: 5, backendReadiness: 5, differentiation: 4, riskReduction: 5, implementationCost: 3, operationalCost: 3 }],
    ['session-security', 'Active session inspection and revocation', 'desktop-settings/sessions', { frequency: 3, impact: 4, backendReadiness: 5, differentiation: 3, riskReduction: 5, implementationCost: 5, operationalCost: 5 }],
    ['privacy-jobs', 'Privacy job progress, reconciliation and cancellation', 'desktop-settings/privacy', { frequency: 2, impact: 5, backendReadiness: 5, differentiation: 3, riskReduction: 5, implementationCost: 4, operationalCost: 4 }],
    ['recurring-work', 'Recurring work and reusable work-item templates', 'desktop-project/automation', { frequency: 4, impact: 4, backendReadiness: 5, differentiation: 4, riskReduction: 3, implementationCost: 4, operationalCost: 4 }],
    ['search-operations', 'Search rebuild and reconciliation controls', 'desktop-admin/operations', { frequency: 2, impact: 4, backendReadiness: 5, differentiation: 3, riskReduction: 5, implementationCost: 4, operationalCost: 3 }],
    ['durable-messaging-operations', 'Outbox health and dead-letter recovery', 'desktop-admin/operations', { frequency: 2, impact: 5, backendReadiness: 5, differentiation: 3, riskReduction: 5, implementationCost: 4, operationalCost: 3 }],
    ['notification-delivery', 'Notification delivery operations', 'desktop-admin/notifications', { frequency: 2, impact: 4, backendReadiness: 5, differentiation: 3, riskReduction: 5, implementationCost: 4, operationalCost: 3 }]
  ];
  return definitions.map(([id, label, targetSurface, score]) => {
    const operationIds = items.filter(item => item.capability === id && item.status === 'absent').map(item => item.id);
    const total = Object.values(score).reduce((sum, value) => sum + value, 0);
    return { id, label, targetSurface, priority: total >= 30 ? 'Now' : 'Next', score: { ...score, total, maximum: 35 }, operationIds };
  }).filter(gap => gap.operationIds.length > 0).sort((left, right) => right.score.total - left.score.total || compareCodePoints(left.id, right.id));
}

function buildSummary(items, calls, background, gaps) {
  const byStatus = Object.fromEntries(['surfaced', 'partial', 'absent', 'intentional'].map(status => [status, items.filter(item => item.status === status).length]));
  return {
    operations: items.length,
    openApiOperations: openApiOperations.size,
    frontendCalls: calls.length,
    desktopCalls: calls.filter(call => call.surface === 'desktop').length,
    mobileCalls: calls.filter(call => call.surface === 'mobile').length,
    backgroundCapabilities: background.length,
    gapCapabilities: gaps.length,
    byStatus,
    duplicateOperations: 0,
    unmatchedFrontendCalls: 0,
    unownedOperations: 0,
    ambiguousFrontendCalls: 0,
    explicitMultiOperationCalls: consumerOwnership.size
  };
}

function renderMarkdown(matrix) {
  const rows = matrix.operations.map(operation => {
    const consumers = ['desktop', 'mobile', 'admin', 'integration', 'background']
      .filter(kind => operation.consumers[kind].length)
      .join(', ') || operation.targetSurface;
    return `| \`${operation.method}\` | \`${operation.path}\` | ${operation.capability} | ${operation.permission} | **${operation.status}** | ${consumers} |`;
  }).join('\n');
  const gaps = matrix.capabilityGaps.map((gap, index) => `${index + 1}. **${gap.label}** - ${gap.score.total}/35, ${gap.operationIds.length} absent operation, target: \`${gap.targetSurface}\`.`).join('\n');
  return `# API-to-UI Product Capability Matrix\n\nGenerated: ${matrix.generatedAtUtc}\n\nThis document is generated by \`scripts/product/Build-ProductCapabilityMatrix.mjs\`. Edit sources or generator policy, not this file.\n\n## Coverage\n\n- Route operations: ${matrix.summary.operations}; OpenAPI operations: ${matrix.summary.openApiOperations}.\n- Frontend calls: ${matrix.summary.frontendCalls} (${matrix.summary.desktopCalls} desktop, ${matrix.summary.mobileCalls} mobile).\n- Status: ${matrix.summary.byStatus.surfaced} surfaced, ${matrix.summary.byStatus.partial} partial, ${matrix.summary.byStatus.absent} absent, ${matrix.summary.byStatus.intentional} intentional non-UI.\n- Ownership checks: 0 duplicates, 0 unmatched calls, 0 unowned operations and 0 ambiguous calls; ${matrix.summary.explicitMultiOperationCalls} dynamic calls have explicit ownership policies.\n- Background consumers: ${matrix.summary.backgroundCapabilities}.\n\n## Highest-Value Gaps\n\n${gaps}\n\n## Information Architecture\n\nDesktop currently exposes \`${matrix.informationArchitecture.desktop.routes.join('`, `')}\` with project views \`${matrix.informationArchitecture.desktop.projectViews.join('`, `')}\`. Mobile exposes ${matrix.informationArchitecture.mobile.routes.length} router states and task modes \`${matrix.informationArchitecture.mobile.taskViews.join('`, `')}\`. Planned destinations are owned explicitly in the machine-readable matrix.\n\n## Operation Matrix\n\n| Method | Route | Capability | Permission | Status | Consumer or target |\n|---|---|---|---|---|---|\n${rows}\n`;
}

function listJavaScript(root) {
  const result = [];
  const visit = directory => {
    for (const entry of readdirSync(resolve(applicationRoot, directory), { withFileTypes: true })) {
      const path = `${directory}/${entry.name}`;
      if (entry.isDirectory()) visit(path);
      else if (entry.name.endsWith('.js') && entry.name !== 'service-worker.js') result.push(path);
    }
  };
  visit(root);
  return result.sort(compareCodePoints);
}

function exists(path) { return existsSync(resolve(applicationRoot, path)); }
function read(path) { return readFileSync(resolve(applicationRoot, path), 'utf8').replaceAll('\r\n', '\n'); }
function write(path, value) { const absolute = resolve(applicationRoot, path); mkdirSync(dirname(absolute), { recursive: true }); writeFileSync(absolute, value, 'utf8'); }
function sha256(value) { return createHash('sha256').update(value).digest('hex'); }
function escapeRegExp(value) { return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }
function compareCodePoints(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
