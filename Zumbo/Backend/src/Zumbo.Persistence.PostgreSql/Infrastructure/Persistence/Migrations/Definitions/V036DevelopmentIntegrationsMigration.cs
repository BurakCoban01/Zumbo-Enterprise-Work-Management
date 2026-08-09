namespace Zumbo.Persistence.PostgreSql;

internal static class V036DevelopmentIntegrationsMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS work_items.development_connections (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_development_connections_tenant_updated
                ON work_items.development_connections (
                    (document ->> 'OrganizationId'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAtUtc') DESC,
                    id);

            CREATE TABLE IF NOT EXISTS work_items.development_repository_mappings (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_development_mappings_tenant_connection_repository
                ON work_items.development_repository_mappings (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ConnectionId'),
                    (document ->> 'ExternalRepositoryId'));
            CREATE INDEX IF NOT EXISTS ix_development_mappings_tenant_project_active
                ON work_items.development_repository_mappings (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    ((document ->> 'IsActive')::boolean),
                    id);

            CREATE TABLE IF NOT EXISTS work_items.work_item_development_links (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_development_links_tenant_work_item_updated
                ON work_items.work_item_development_links (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAtUtc') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_development_links_tenant_mapping_commit
                ON work_items.work_item_development_links (
                    (document ->> 'OrganizationId'),
                    (document ->> 'MappingId'),
                    (document ->> 'CommitSha'),
                    (document ->> 'ExternalId'),
                    id);

            CREATE TABLE IF NOT EXISTS work_items.development_webhook_receipts (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_development_receipts_connection_expiry
                ON work_items.development_webhook_receipts (
                    (document ->> 'ConnectionId'),
                    public.zumbo_parse_timestamptz(document ->> 'ExpiresAtUtc'),
                    id);
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS work_items.development_webhook_receipts;
            DROP TABLE IF EXISTS work_items.work_item_development_links;
            DROP TABLE IF EXISTS work_items.development_repository_mappings;
            DROP TABLE IF EXISTS work_items.development_connections;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        36,
        "development_integrations",
        UpSql,
        DownSql);
}
