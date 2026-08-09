namespace Zumbo.Persistence.PostgreSql;

internal static class V034CapacityPlansMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS work_items.capacity_plans (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_capacity_plans_tenant_owner_state
                ON work_items.capacity_plans (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_capacity_plans_tenant_viewers
                ON work_items.capacity_plans
                USING gin ((document -> 'ViewerUserIds'));
            CREATE INDEX IF NOT EXISTS ix_capacity_plans_tenant_projects
                ON work_items.capacity_plans
                USING gin ((document -> 'ProjectIds'));
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS work_items.capacity_plans;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        34,
        "capacity_plans",
        UpSql,
        DownSql);
}
