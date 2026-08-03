using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    public async Task<MongoMigrationRunReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        if (!string.IsNullOrWhiteSpace(_options.RollbackMigrationId))
        {
            var rollback = await RollbackAsync(_options.RollbackMigrationId.Trim(), cancellationToken);
            return new MongoMigrationRunReport(_options.DryRun, [rollback]);
        }

        var outcomes = new List<MongoMigrationOutcome>
        {
            await ApplyIndexesAsync(IndexMigrationId, MongoRequiredIndexes.All, cancellationToken),
            await ApplyIndexesAsync(
                DurableMessagingIndexMigrationId,
                MongoDurableMessagingIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                IdentityCredentialIndexMigrationId,
                MongoLegacyIdentityCredentialIndexes.All,
                cancellationToken),
            await ReplaceIdentityCredentialScalarUtcIndexesAsync(cancellationToken),
            await ApplyIndexesAsync(
                WorkItemActivityIndexMigrationId,
                MongoWorkItemActivityIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemGraphIndexMigrationId,
                MongoWorkItemGraphIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemCollaborationIndexMigrationId,
                MongoWorkItemCollaborationIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemBulkJobIndexMigrationId,
                MongoWorkItemBulkJobIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                IntakeIndexMigrationId,
                MongoIntakeIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                AutomationIndexMigrationId,
                MongoAutomationIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                AutomationRunIndexMigrationId,
                MongoAutomationRunIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                DashboardIndexMigrationId,
                MongoDashboardIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                PortfolioIndexMigrationId,
                MongoPortfolioIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                GoalIndexMigrationId,
                MongoGoalIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                CapacityPlanIndexMigrationId,
                MongoCapacityPlanIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                KnowledgeIndexMigrationId,
                MongoKnowledgeIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                DevelopmentIntegrationIndexMigrationId,
                MongoDevelopmentIntegrationIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                HighCardinalityIndexMigrationId,
                MongoHighCardinalityIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                AuditTenantIndexMigrationId,
                MongoAuditTenantIndexes.All,
                cancellationToken),
            await ReplaceNotificationDeliveryIndexesAsync(cancellationToken),
            await ApplyIndexesAsync(
                WorkItemReportingIndexMigrationId,
                MongoWorkItemReportingIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemReportActivityIndexMigrationId,
                MongoWorkItemReportActivityIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                PrivacyWorkflowIndexMigrationId,
                MongoPrivacyWorkflowIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                PrivacyWorkflowUtcIndexMigrationId,
                MongoPrivacyWorkflowUtcIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WebhookIndexMigrationId,
                MongoWebhookIndexes.All,
                cancellationToken),
            await BackfillUserVersionsAsync(cancellationToken),
            await CleanupLegacyMigrationMarkersAsync(cancellationToken)
        };
        if (_options.RunDataMigrations)
        {
            outcomes.Add(await BackfillRanksAsync(cancellationToken));
            outcomes.Add(await BackfillRefreshSessionsAsync(cancellationToken));
            outcomes.Add(await BackfillApiKeyVersionsAsync(cancellationToken));
            outcomes.Add(await BackfillWorkItemActivitiesAsync(cancellationToken));
            outcomes.Add(await BackfillOrganizationVersionsAsync(cancellationToken));
            outcomes.Add(await ExpireLegacyTeamInvitesAsync(cancellationToken));
            outcomes.Add(await BackfillProjectLifecycleAsync(cancellationToken));
            outcomes.Add(await BackfillWorkflowLifecycleAsync(cancellationToken));
            outcomes.Add(await BackfillSprintLifecycleAsync(cancellationToken));
            outcomes.Add(await BackfillWorkItemTypeSchemasAsync(cancellationToken));
            outcomes.Add(await BackfillWorkItemGraphAsync(cancellationToken));
        }

        return new MongoMigrationRunReport(_options.DryRun, outcomes);
    }
}
