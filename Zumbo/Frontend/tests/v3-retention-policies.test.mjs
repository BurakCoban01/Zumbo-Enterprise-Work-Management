import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const read = relativePath => fs.readFileSync(path.join(root, relativePath), 'utf8');

const workflow = read('Backend/src/Zumbo.Modules.Workflows/WorkflowsModule.cs');
const workflowPolicy = read('Backend/src/Zumbo.Modules.Workflows/WorkflowRetentionPolicy.cs');
const workflowContracts = read('Backend/src/Zumbo.Modules.Workflows/Features/RepresentativeWorkflowSlices.cs');
const goals = read('Backend/src/Zumbo.Modules.Projects/GoalService.cs');
const portfolios = read('Backend/src/Zumbo.Modules.Projects/PortfolioService.cs');
const developmentDocuments = read(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationDocuments.cs');
const developmentSecurity = read(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookSecurity.cs');
const developmentService = read(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationService.cs');
const developmentReferencePolicy = read(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookReferencePolicy.cs');
const desktop = read('Frontend/desktop-bulma/index.html');
const mobile = read('Frontend/mobile-ionic/index.html');

test('capped product histories use named policies instead of silent literal truncation', () => {
  assert.doesNotMatch(workflow, /\.Take\(25\)/);
  assert.match(workflowPolicy, /MaximumPublishedVersions = 25/);
  assert.match(workflow, /WorkflowRetentionPolicy\.RetainPublishedVersions/);
  assert.match(goals, /ProjectHistoryRetentionPolicy\.MaximumGoalStatusUpdates/);
  assert.match(goals, /ProjectHistoryRetentionPolicy\.MaximumKeyResultProgressUpdates/);
  assert.match(portfolios, /ProjectHistoryRetentionPolicy\.MaximumInitiativeStatusUpdates/);
  assert.doesNotMatch(developmentSecurity, /\.Take\(10\)/);
  assert.doesNotMatch(developmentService, /\.Take\(10\)/);
  assert.match(developmentDocuments, /MaximumWorkItemReferencesPerEvent = 10/);
  assert.match(
    developmentReferencePolicy,
    /DEVELOPMENT_WEBHOOK_REFERENCE_LIMIT_EXCEEDED/);
  assert.match(
    developmentSecurity,
    /DevelopmentWebhookReferencePolicy\.ExtractWithinLimit/);
  assert.match(
    developmentService,
    /DevelopmentWebhookReferencePolicy[\s\S]*\.ExtractWithinLimit/);
});

test('retention limits are explicit in API contracts and desktop/mobile history surfaces', () => {
  assert.match(workflowContracts, /PublishedVersionRetentionLimit/);
  assert.match(goals, /StatusUpdateRetentionLimit/);
  assert.match(goals, /ProgressUpdateRetentionLimit/);
  assert.match(portfolios, /StatusUpdateRetentionLimit/);
  assert.match(desktop, /publishedVersionRetentionLimit/);
  assert.match(desktop, /statusUpdateRetentionLimit/);
  assert.match(desktop, /progressUpdateRetentionLimit/);
  assert.match(mobile, /statusUpdateRetentionLimit/);
  assert.match(mobile, /progressUpdateRetentionLimit/);
});
