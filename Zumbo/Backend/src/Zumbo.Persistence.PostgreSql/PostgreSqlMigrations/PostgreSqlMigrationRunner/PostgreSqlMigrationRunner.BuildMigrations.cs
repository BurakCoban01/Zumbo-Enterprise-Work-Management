using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private IReadOnlyList<Migration> BuildMigrations()
    {
        var storages = PostgreSqlDocumentCatalog.BuiltInStorages
            .OrderBy(storage => storage.Schema, StringComparer.Ordinal)
            .ThenBy(storage => storage.Table, StringComparer.Ordinal)
            .ToList();
        var schemas = storages.Select(storage => storage.Schema)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var createSchemas = string.Join('\n', schemas.Select(schema =>
            $"CREATE SCHEMA IF NOT EXISTS {SqlIdentifier.Quote(schema)};"));
        var dropSchemas = "DROP TABLE IF EXISTS identity.refresh_sessions;\n" +
            string.Join('\n', schemas.OrderDescending(StringComparer.Ordinal).Select(schema =>
                $"DROP SCHEMA IF EXISTS {SqlIdentifier.Quote(schema)};"));
        var createTables = string.Join("\n\n", storages.Select(CreateTableSql));
        var dropTables = string.Join('\n', storages.AsEnumerable().Reverse().Select(storage =>
            $"DROP TABLE IF EXISTS {Qualified(storage)};"));
        var createIndexes = string.Join('\n', storages.Select(storage =>
            $"CREATE INDEX IF NOT EXISTS {SqlIdentifier.Quote(IndexName(storage))} " +
            $"ON {Qualified(storage)} USING GIN (document jsonb_path_ops);"));
        var dropIndexes = string.Join('\n', storages.AsEnumerable().Reverse().Select(storage =>
            $"DROP INDEX IF EXISTS {SqlIdentifier.Quote(storage.Schema)}.{SqlIdentifier.Quote(IndexName(storage))};"));

        return
        [
            Migration.Create(1, "create_module_schemas", createSchemas, dropSchemas),
            Migration.Create(2, "create_document_tables", createTables, dropTables),
            Migration.Create(3, "create_jsonb_indexes", createIndexes, dropIndexes),
            Migration.Create(4, "create_access_pattern_indexes", accessIndexes, dropAccessIndexes),
            Migration.Create(5, "create_durable_messaging", durableMessaging, dropDurableMessaging),
            Migration.Create(6, "create_durable_consumer_deduplication", durableConsumerDeduplication, dropDurableConsumerDeduplication),
            Migration.Create(7, "create_identity_credential_stores", identityCredentialStores, dropIdentityCredentialStores),
            Migration.Create(8, "backfill_api_key_versions", apiKeyVersionBackfill, dropApiKeyVersionBackfill),
            Migration.Create(9, "create_api_key_expiry_index", apiKeyExpiryIndex, dropApiKeyExpiryIndex),
            Migration.Create(10, "backfill_api_key_utc_fields", apiKeyUtcFieldBackfill, dropApiKeyUtcFieldBackfill),
            Migration.Create(11, "create_work_item_activity_stores", workItemActivityStores, dropWorkItemActivityStores),
            Migration.Create(12, "backfill_organization_versions", organizationVersionBackfill, dropOrganizationVersionBackfill),
            Migration.Create(13, "expire_legacy_team_invites", expireLegacyTeamInvites, dropLegacyTeamInviteMarker),
            Migration.Create(14, "backfill_project_lifecycle", projectLifecycleBackfill, dropProjectLifecycleMarker),
            Migration.Create(15, "workflow_lifecycle_and_wip_projection", workflowLifecycleAndWipProjection, dropWorkflowLifecycleMarker),
            Migration.Create(16, "sprint_lifecycle_and_snapshots", sprintLifecycle, dropSprintLifecycleMarker),
            Migration.Create(17, "work_item_type_schemas", workItemTypeSchemas, dropWorkItemTypeSchemas),
            Migration.Create(18, "work_item_relation_graph", workItemRelationGraph, dropWorkItemRelationGraph),
            Migration.Create(19, "work_item_collaboration_and_recurrence", workItemCollaborationAndRecurrence, dropWorkItemCollaborationAndRecurrence),
            Migration.Create(20, "work_item_bulk_jobs", workItemBulkJobs, dropWorkItemBulkJobs),
            Migration.Create(21, "audit_tenant_indexes", auditTenantIndexes, dropAuditTenantIndexes),
            Migration.Create(22, "notification_delivery_indexes", notificationDeliveryIndexes, dropNotificationDeliveryIndexes),
            Migration.Create(23, "work_item_reporting_indexes", workItemReportingIndexes, dropWorkItemReportingIndexes),
            Migration.Create(24, "work_item_report_activity_indexes", workItemReportActivityIndexes, dropWorkItemReportActivityIndexes),
            Migration.Create(25, "privacy_workflows", privacyWorkflows, dropPrivacyWorkflows),
            Migration.Create(26, "privacy_workflow_utc_index", privacyWorkflowUtcIndex, dropPrivacyWorkflowUtcIndex),
            Migration.Create(27, "webhook_subscriptions_and_deliveries", webhooks, dropWebhooks),
            Migration.Create(28, "intake_forms_and_submissions", intakeForms, dropIntakeForms),
            Migration.Create(29, "automation_rules", automationRules, dropAutomationRules),
            Migration.Create(30, "automation_runs", automationRuns, dropAutomationRuns),
            Migration.Create(31, "dashboards", dashboards, dropDashboards),
            Migration.Create(32, "portfolios", portfolios, dropPortfolios),
            Migration.Create(33, "goals", goals, dropGoals),
            Migration.Create(34, "capacity_plans", capacityPlans, dropCapacityPlans),
            Migration.Create(35, "knowledge_documents", knowledgeDocuments, dropKnowledgeDocuments),
            Migration.Create(36, "development_integrations", developmentIntegrations, dropDevelopmentIntegrations),
            Migration.Create(37, "high_cardinality_indexes", highCardinalityIndexes, dropHighCardinalityIndexes)
        ];
    }
}
