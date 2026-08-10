namespace Zumbo.Persistence.PostgreSql;

internal static class V026PrivacyWorkflowUtcIndexMigration
{
        private const string UpSql = """
            CREATE INDEX IF NOT EXISTS ix_privacy_workflows_retention_utc
                ON identity.privacy_workflows (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            """;

        private const string DownSql = """
            DROP INDEX IF EXISTS identity.ix_privacy_workflows_retention_utc;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        26,
        "privacy_workflow_utc_index",
        UpSql,
        DownSql);
}
