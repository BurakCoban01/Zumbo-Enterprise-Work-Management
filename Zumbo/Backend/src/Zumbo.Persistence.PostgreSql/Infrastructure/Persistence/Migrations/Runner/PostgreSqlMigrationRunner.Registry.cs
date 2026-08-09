using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner
{
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

        var v001 = V001CreateModuleSchemasMigration.Create(createSchemas, dropSchemas);
        var v002 = V002CreateDocumentTablesMigration.Create(createTables, dropTables);
        var v003 = V003CreateJsonbIndexesMigration.Create(createIndexes, dropIndexes);

        return
        [
            Migration.Create(1, "create_module_schemas", v001.UpSql, v001.DownSql),
            Migration.Create(2, "create_document_tables", v002.UpSql, v002.DownSql),
            Migration.Create(3, "create_jsonb_indexes", v003.UpSql, v003.DownSql),
            Migration.Create(4, "create_access_pattern_indexes", V004CreateAccessPatternIndexesMigration.Definition.UpSql, V004CreateAccessPatternIndexesMigration.Definition.DownSql),
            Migration.Create(5, "create_durable_messaging", V005CreateDurableMessagingMigration.Definition.UpSql, V005CreateDurableMessagingMigration.Definition.DownSql),
            Migration.Create(6, "create_durable_consumer_deduplication", V006DurableConsumerDedupMigration.Definition.UpSql, V006DurableConsumerDedupMigration.Definition.DownSql),
            Migration.Create(7, "create_identity_credential_stores", V007CreateIdentityCredentialStoresMigration.Definition.UpSql, V007CreateIdentityCredentialStoresMigration.Definition.DownSql),
            Migration.Create(8, "backfill_api_key_versions", V008BackfillApiKeyVersionsMigration.Definition.UpSql, V008BackfillApiKeyVersionsMigration.Definition.DownSql),
            Migration.Create(9, "create_api_key_expiry_index", V009CreateApiKeyExpiryIndexMigration.Definition.UpSql, V009CreateApiKeyExpiryIndexMigration.Definition.DownSql),
            Migration.Create(10, "backfill_api_key_utc_fields", V010BackfillApiKeyUtcFieldsMigration.Definition.UpSql, V010BackfillApiKeyUtcFieldsMigration.Definition.DownSql),
            Migration.Create(11, "create_work_item_activity_stores", V011CreateWorkItemActivityStoresMigration.Definition.UpSql, V011CreateWorkItemActivityStoresMigration.Definition.DownSql),
            Migration.Create(12, "backfill_organization_versions", V012BackfillOrganizationVersionsMigration.Definition.UpSql, V012BackfillOrganizationVersionsMigration.Definition.DownSql),
            Migration.Create(13, "expire_legacy_team_invites", V013ExpireLegacyTeamInvitesMigration.Definition.UpSql, V013ExpireLegacyTeamInvitesMigration.Definition.DownSql),
            Migration.Create(14, "backfill_project_lifecycle", V014BackfillProjectLifecycleMigration.Definition.UpSql, V014BackfillProjectLifecycleMigration.Definition.DownSql),
            Migration.Create(15, "workflow_lifecycle_and_wip_projection", V015WorkflowLifecycleProjectionMigration.Definition.UpSql, V015WorkflowLifecycleProjectionMigration.Definition.DownSql),
            Migration.Create(16, "sprint_lifecycle_and_snapshots", V016SprintLifecycleAndSnapshotsMigration.Definition.UpSql, V016SprintLifecycleAndSnapshotsMigration.Definition.DownSql),
            Migration.Create(17, "work_item_type_schemas", V017WorkItemTypeSchemasMigration.Definition.UpSql, V017WorkItemTypeSchemasMigration.Definition.DownSql),
            Migration.Create(18, "work_item_relation_graph", V018WorkItemRelationGraphMigration.Definition.UpSql, V018WorkItemRelationGraphMigration.Definition.DownSql),
            Migration.Create(19, "work_item_collaboration_and_recurrence", V019WorkItemCollaborationRecurrenceMigration.Definition.UpSql, V019WorkItemCollaborationRecurrenceMigration.Definition.DownSql),
            Migration.Create(20, "work_item_bulk_jobs", V020WorkItemBulkJobsMigration.Definition.UpSql, V020WorkItemBulkJobsMigration.Definition.DownSql),
            Migration.Create(21, "audit_tenant_indexes", V021AuditTenantIndexesMigration.Definition.UpSql, V021AuditTenantIndexesMigration.Definition.DownSql),
            Migration.Create(22, "notification_delivery_indexes", V022NotificationDeliveryIndexesMigration.Definition.UpSql, V022NotificationDeliveryIndexesMigration.Definition.DownSql),
            Migration.Create(23, "work_item_reporting_indexes", V023WorkItemReportingIndexesMigration.Definition.UpSql, V023WorkItemReportingIndexesMigration.Definition.DownSql),
            Migration.Create(24, "work_item_report_activity_indexes", V024WorkItemReportActivityIndexesMigration.Definition.UpSql, V024WorkItemReportActivityIndexesMigration.Definition.DownSql),
            Migration.Create(25, "privacy_workflows", V025PrivacyWorkflowsMigration.Definition.UpSql, V025PrivacyWorkflowsMigration.Definition.DownSql),
            Migration.Create(26, "privacy_workflow_utc_index", V026PrivacyWorkflowUtcIndexMigration.Definition.UpSql, V026PrivacyWorkflowUtcIndexMigration.Definition.DownSql),
            Migration.Create(27, "webhook_subscriptions_and_deliveries", V027WebhookDeliveryMigration.Definition.UpSql, V027WebhookDeliveryMigration.Definition.DownSql),
            Migration.Create(28, "intake_forms_and_submissions", V028IntakeFormsAndSubmissionsMigration.Definition.UpSql, V028IntakeFormsAndSubmissionsMigration.Definition.DownSql),
            Migration.Create(29, "automation_rules", V029AutomationRulesMigration.Definition.UpSql, V029AutomationRulesMigration.Definition.DownSql),
            Migration.Create(30, "automation_runs", V030AutomationRunsMigration.Definition.UpSql, V030AutomationRunsMigration.Definition.DownSql),
            Migration.Create(31, "dashboards", V031DashboardsMigration.Definition.UpSql, V031DashboardsMigration.Definition.DownSql),
            Migration.Create(32, "portfolios", V032PortfoliosMigration.Definition.UpSql, V032PortfoliosMigration.Definition.DownSql),
            Migration.Create(33, "goals", V033GoalsMigration.Definition.UpSql, V033GoalsMigration.Definition.DownSql),
            Migration.Create(34, "capacity_plans", V034CapacityPlansMigration.Definition.UpSql, V034CapacityPlansMigration.Definition.DownSql),
            Migration.Create(35, "knowledge_documents", V035KnowledgeDocumentsMigration.Definition.UpSql, V035KnowledgeDocumentsMigration.Definition.DownSql),
            Migration.Create(36, "development_integrations", V036DevelopmentIntegrationsMigration.Definition.UpSql, V036DevelopmentIntegrationsMigration.Definition.DownSql),
            Migration.Create(37, "high_cardinality_indexes", V037HighCardinalityIndexesMigration.Definition.UpSql, V037HighCardinalityIndexesMigration.Definition.DownSql)
        ];
    }

    private sealed record Migration(long Version, string Name, string UpSql, string DownSql, string Checksum)
    {
        public PostgreSqlMigrationInfo Info => new(Version, Name, Checksum);

        public static Migration Create(long version, string name, string upSql, string downSql)
        {
            var content = $"{version}\n{name}\n{upSql}\n-- DOWN\n{downSql}";
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            return new Migration(version, name, upSql, downSql, checksum);
        }
    }
}
