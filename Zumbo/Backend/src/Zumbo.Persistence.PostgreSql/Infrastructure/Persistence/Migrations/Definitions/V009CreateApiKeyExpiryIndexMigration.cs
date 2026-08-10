namespace Zumbo.Persistence.PostgreSql;

internal static class V009CreateApiKeyExpiryIndexMigration
{
        private const string UpSql = """
            CREATE INDEX IF NOT EXISTS ix_api_keys_expires_utc
                ON identity.api_keys (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAt']),
                    id);
            """;

        private const string DownSql = """
            DROP INDEX IF EXISTS identity.ix_api_keys_expires_utc;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        9,
        "create_api_key_expiry_index",
        UpSql,
        DownSql);
}
