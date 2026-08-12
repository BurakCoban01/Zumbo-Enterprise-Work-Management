import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const read = relativePath => fs.readFileSync(path.join(root, relativePath), 'utf8');

const workflow = read('Backend/src/Zumbo.Modules.Workflows/Application/Compatibility/WorkflowDefinitions/WorkflowService.cs');
const workflowPolicy = read('Backend/src/Zumbo.Modules.Workflows/Application/Policies/WorkflowDefinitions/WorkflowRetentionPolicy.cs');
const workflowContracts = read('Backend/src/Zumbo.Modules.Workflows/Application/Features/WorkflowDefinitions/WorkflowResponse.cs');
const goals = read('Backend/src/Zumbo.Modules.Projects/Application/Compatibility/Goals/GoalService/MappingSupport.cs');
const portfolios = read('Backend/src/Zumbo.Modules.Projects/Application/Compatibility/Portfolio/PortfolioService/MappingSupport.cs');
const goalContracts = [
  read('Backend/src/Zumbo.Modules.Projects/Application/Features/Goals/GoalResponse.cs'),
  read('Backend/src/Zumbo.Modules.Projects/Application/Features/Goals/KeyResultResponse.cs')
].join('\n');
const portfolioContracts = read('Backend/src/Zumbo.Modules.Projects/Application/Features/Portfolio/InitiativeResponse.cs');
const developmentDocuments = read(
  'Backend/src/Zumbo.Modules.WorkItems/Domain/DevelopmentIntegrations/DevelopmentIntegrationLimits.cs');
const developmentSecurity = read(
  'Backend/src/Zumbo.Modules.WorkItems/Application/Features/Webhooks/DevelopmentWebhookSecurity.cs');
const developmentService = read(
  'Backend/src/Zumbo.Modules.WorkItems/Application/Compatibility/DevelopmentIntegrations/DevelopmentIntegrationService/DevelopmentIntegrationService.ResolveReferencedWorkItemsAsync.cs');
const developmentReferencePolicy = [
  read('Backend/src/Zumbo.Modules.WorkItems/Application/Features/Webhooks/DevelopmentWebhookReferencePolicy.cs'),
  read('Backend/src/Zumbo.Modules.WorkItems/Application/Features/Webhooks/DevelopmentWebhookReferenceLimitException.cs')
].join('\n');
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
  assert.match(goalContracts, /StatusUpdateRetentionLimit/);
  assert.match(goalContracts, /ProgressUpdateRetentionLimit/);
  assert.match(portfolioContracts, /StatusUpdateRetentionLimit/);
  assert.match(desktop, /publishedVersionRetentionLimit/);
  assert.match(desktop, /statusUpdateRetentionLimit/);
  assert.match(desktop, /progressUpdateRetentionLimit/);
  assert.match(mobile, /statusUpdateRetentionLimit/);
  assert.match(mobile, /progressUpdateRetentionLimit/);
});
