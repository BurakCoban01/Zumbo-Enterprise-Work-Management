using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner(
    IMongoDbService mongo,
    IOptions<MongoMigrationOptions> configuredOptions,
    ILogger<MongoMigrationRunner> logger)
{
    public const string IndexMigrationId = "20260719_001_required_indexes";
    public const string RankMigrationId = "20260719_002_workitem_rank_backfill";
    public const string DurableMessagingIndexMigrationId = "20260719_003_durable_messaging_indexes";
    public const string IdentityCredentialIndexMigrationId = "20260719_004_identity_credential_indexes";
    public const string RefreshSessionMigrationId = "20260719_005_refresh_session_backfill";
    public const string ApiKeyVersionMigrationId = "20260719_006_api_key_version_backfill";
    public const string IdentityCredentialScalarUtcIndexMigrationId =
        "20260719_007_identity_credential_scalar_utc_indexes";
    public const string WorkItemActivityIndexMigrationId =
        "20260719_008_workitem_activity_indexes";
    public const string WorkItemActivityMigrationId =
        "20260719_009_workitem_activity_backfill";
    public const string OrganizationVersionMigrationId =
        "20260720_010_organization_version_backfill";
    public const string TeamInviteTokenMigrationId =
        "20260720_011_team_invite_token_backfill";
    public const string ProjectLifecycleMigrationId =
        "20260720_012_project_lifecycle_backfill";
    public const string WorkflowLifecycleMigrationId =
        "20260720_013_workflow_lifecycle_backfill";
    public const string SprintLifecycleMigrationId =
        "20260720_014_sprint_lifecycle_backfill";
    public const string WorkItemTypeSchemaMigrationId =
        "20260720_015_workitem_type_schema_backfill";
    public const string WorkItemGraphIndexMigrationId =
        "20260720_016_workitem_graph_indexes";
    public const string WorkItemGraphMigrationId =
        "20260720_017_workitem_graph_edge_backfill";
    public const string WorkItemCollaborationIndexMigrationId =
        "20260720_018_workitem_collaboration_indexes";
    public const string WorkItemBulkJobIndexMigrationId =
        "20260720_019_workitem_bulk_job_indexes";
    public const string AuditTenantIndexMigrationId =
        "20260720_020_audit_tenant_indexes";
    public const string NotificationDeliveryIndexMigrationId =
        "20260720_021_notification_delivery_indexes";
    public const string WorkItemReportingIndexMigrationId =
        "20260720_022_workitem_reporting_indexes";
    public const string WorkItemReportActivityIndexMigrationId =
        "20260720_023_workitem_report_activity_indexes";
    public const string PrivacyWorkflowIndexMigrationId =
        "20260720_024_privacy_workflow_indexes";
    public const string PrivacyWorkflowUtcIndexMigrationId =
        "20260720_025_privacy_workflow_utc_index";
    public const string WebhookIndexMigrationId =
        "20260720_026_webhook_indexes";
    public const string IntakeIndexMigrationId =
        "20260724_027_intake_indexes";
    public const string AutomationIndexMigrationId =
        "20260728_028_automation_indexes";
    public const string AutomationRunIndexMigrationId =
        "20260728_029_automation_run_indexes";
    public const string DashboardIndexMigrationId =
        "20260728_030_dashboard_indexes";
    public const string PortfolioIndexMigrationId =
        "20260728_031_portfolio_indexes";
    public const string GoalIndexMigrationId =
        "20260729_032_goal_indexes";
    public const string CapacityPlanIndexMigrationId =
        "20260729_033_capacity_plan_indexes";
    public const string KnowledgeIndexMigrationId =
        "20260729_034_knowledge_indexes";
    public const string DevelopmentIntegrationIndexMigrationId =
        "20260729_035_development_integration_indexes";
    public const string HighCardinalityIndexMigrationId =
        "20260729_036_high_cardinality_indexes";
    public const string UserVersionMigrationId =
        "20260803_037_user_version_backfill";
    public const string LegacyMigrationMarkerCleanupId =
        "20260803_038_legacy_migration_marker_cleanup";
    public const string IdentityAuthorizationIndexMigrationId =
        "20260811_039_identity_authorization_indexes";

    private const string LedgerCollection = "__zumbo_migrations";
    private const string BackupCollection = "__zumbo_migration_rank_backups";
    private const string ControlModule = "Default";
    private const string WorkItemsModule = "WorkItems";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly MongoMigrationOptions _options = configuredOptions.Value;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    private IMongoCollection<MongoMigrationLedgerDocument> Ledgers =>
        mongo.GetCollection<MongoMigrationLedgerDocument>(LedgerCollection, ControlModule);

    private int BatchSize => Math.Clamp(_options.BatchSize, 1, 10_000);
    private int MaxBatches => Math.Clamp(_options.MaxBatchesPerRun, 1, 10_000);
    private static string RankChecksum => Checksum("rank-v2:missing-or-numeric-zero:datetime-offset-array-or-date-or-ticks");
    private static string RefreshSessionChecksum =>
        Checksum("refresh-session-v1:additive:set-on-insert:user-checkpoint:retain-30-days");
    private static string ApiKeyVersionChecksum =>
        Checksum("api-key-version-v2:version-and-scalar-utc-fields:id-checkpoint");
    private static string WorkItemActivityChecksum =>
        Checksum("workitem-activity-v2:six-owned-stores:project-tenant-when-populated:cas-clear:id-checkpoint");
    private static string OrganizationVersionChecksum =>
        Checksum("organization-version-v1:version-one:active-default:id-checkpoint");
    private static string TeamInviteTokenChecksum =>
        Checksum("team-invite-token-v1:expire-hashless-pending:versioned:id-checkpoint");
    private static string ProjectLifecycleChecksum =>
        Checksum("project-lifecycle-v1:version-visibility-catalog-retention-defaults:id-checkpoint");
    private static string WorkflowLifecycleChecksum =>
        Checksum("workflow-lifecycle-v1:published-version-draft-history-issue-scheme:id-checkpoint");
    private static string SprintLifecycleChecksum =>
        Checksum("sprint-lifecycle-v2:project-label-to-planned-aggregate:md5-provider-parity:versioned:id-checkpoint");
    private static string WorkItemTypeSchemaChecksum =>
        Checksum("workitem-type-schema-v1:project-defaults:typed-fields:legacy-types:versioned:id-checkpoint");
    private static string WorkItemGraphChecksum =>
        Checksum("workitem-graph-v1:embedded-relations:canonical-direction:md5-provider-parity:id-checkpoint");
    private static string UserVersionChecksum =>
        Checksum("user-version-v1:missing-null-or-non-positive-to-one:id-checkpoint");
    private static string LegacyMigrationMarkerCleanupChecksum =>
        Checksum("legacy-migration-marker-cleanup-v1:project-workflow-sprint-schema-team");

    private static string[] DefaultIssueTypeKeys => ["Epic", "Story", "Task", "Bug", "Subtask"];

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
                IdentityAuthorizationIndexMigrationId,
                MongoIdentityAuthorizationIndexes.All,
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

    private void ValidateOptions()
    {
        if (_options.BatchSize <= 0) throw new InvalidOperationException("MongoMigrations:BatchSize must be positive.");
        if (_options.MaxBatchesPerRun <= 0) throw new InvalidOperationException("MongoMigrations:MaxBatchesPerRun must be positive.");
        if (_options.RunDataMigrations && !_options.DryRun && _options.BatchSize > 10_000)
        {
            logger.LogWarning("Mongo migration batch size was capped at 10000 from {ConfiguredBatchSize}", _options.BatchSize);
        }
    }
}
