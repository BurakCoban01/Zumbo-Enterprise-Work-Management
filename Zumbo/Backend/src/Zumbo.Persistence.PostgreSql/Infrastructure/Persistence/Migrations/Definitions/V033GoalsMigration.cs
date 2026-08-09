namespace Zumbo.Persistence.PostgreSql;

internal static class V033GoalsMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS projects.goals (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_goals_tenant_owner_state
                ON projects.goals (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_goals_tenant_viewers
                ON projects.goals
                USING gin ((document -> 'ViewerUserIds'));
            CREATE INDEX IF NOT EXISTS ix_goals_tenant_key_results
                ON projects.goals
                USING gin ((document -> 'KeyResults'));
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS projects.goals;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        33,
        "goals",
        UpSql,
        DownSql);
}
