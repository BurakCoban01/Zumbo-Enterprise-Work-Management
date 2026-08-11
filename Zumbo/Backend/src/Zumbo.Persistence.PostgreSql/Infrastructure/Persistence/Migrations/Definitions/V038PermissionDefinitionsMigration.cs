namespace Zumbo.Persistence.PostgreSql;

internal static class V038PermissionDefinitionsMigration
{
    private const string UpSql = """
        UPDATE identity.identity_roles
        SET version = 1,
            document = jsonb_set(document, '{Version}', '1'::jsonb, true),
            updated_at = transaction_timestamp()
        WHERE version = 0;
        CREATE TABLE IF NOT EXISTS identity.permission_definitions (
            id text PRIMARY KEY,
            version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
            document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
            created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
            updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
            CHECK (document ->> 'Id' = id),
            CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_permission_definitions_key_ci
            ON identity.permission_definitions (lower(document #>> ARRAY['Key']));
        CREATE INDEX IF NOT EXISTS ix_permission_definitions_active_order
            ON identity.permission_definitions (
                ((document #>> ARRAY['IsActive'])::boolean),
                ((document #>> ARRAY['DisplayOrder'])::integer),
                id COLLATE "C");
        CREATE INDEX IF NOT EXISTS ix_permission_definitions_document_gin
            ON identity.permission_definitions USING GIN (document jsonb_path_ops);
        """;

    private const string DownSql = """
        DROP TABLE IF EXISTS identity.permission_definitions;
        """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        38,
        "permission_definitions",
        UpSql,
        DownSql);
}
