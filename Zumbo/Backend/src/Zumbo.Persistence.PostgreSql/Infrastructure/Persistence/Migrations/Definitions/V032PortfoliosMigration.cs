namespace Zumbo.Persistence.PostgreSql;

internal static class V032PortfoliosMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS projects.portfolios (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_portfolios_tenant_owner_state
                ON projects.portfolios (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_portfolios_tenant_viewers
                ON projects.portfolios
                USING gin ((document -> 'ViewerUserIds'));
            CREATE INDEX IF NOT EXISTS ix_portfolios_tenant_initiatives
                ON projects.portfolios
                USING gin ((document -> 'Initiatives'));
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS projects.portfolios;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        32,
        "portfolios",
        UpSql,
        DownSql);
}
