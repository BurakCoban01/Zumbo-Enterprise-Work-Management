namespace Zumbo.Persistence.PostgreSql;

internal static class V030AutomationRunsMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS workflows.automation_runs (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_automation_runs_tenant_project_created
                ON workflows.automation_runs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAtUtc') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_automation_runs_rule_created
                ON workflows.automation_runs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'RuleId'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAtUtc') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_automation_runs_retry
                ON workflows.automation_runs (
                    (document ->> 'Status'),
                    public.zumbo_parse_timestamptz(document ->> 'NextAttemptAtUtc'),
                    id);
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS workflows.automation_runs;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        30,
        "automation_runs",
        UpSql,
        DownSql);
}
