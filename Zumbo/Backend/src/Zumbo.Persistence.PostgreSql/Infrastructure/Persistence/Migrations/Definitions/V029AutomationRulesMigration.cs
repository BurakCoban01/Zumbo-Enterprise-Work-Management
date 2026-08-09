namespace Zumbo.Persistence.PostgreSql;

internal static class V029AutomationRulesMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS workflows.automation_rules (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_automation_rules_tenant_project_state
                ON workflows.automation_rules (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_automation_rules_schedule
                ON workflows.automation_rules (
                    ((document ->> 'Active')::boolean),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'NextRunAtUtc'),
                    id);
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS workflows.automation_rules;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        29,
        "automation_rules",
        UpSql,
        DownSql);
}
