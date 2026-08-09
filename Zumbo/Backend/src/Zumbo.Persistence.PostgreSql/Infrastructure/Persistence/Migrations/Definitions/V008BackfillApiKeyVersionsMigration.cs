namespace Zumbo.Persistence.PostgreSql;

internal static class V008BackfillApiKeyVersionsMigration
{
        private const string UpSql = """
            UPDATE identity.api_keys
            SET version = 1,
                document = jsonb_set(
                    jsonb_set(document, ARRAY['Version'], '1'::jsonb, true),
                    ARRAY['VersionMigratedBy'],
                    '"20260719_008"'::jsonb,
                    true),
                updated_at = transaction_timestamp()
            WHERE version = 0;
            """;

        private const string DownSql = """
            UPDATE identity.api_keys
            SET version = 0,
                document = document - 'Version' - 'VersionMigratedBy',
                updated_at = transaction_timestamp()
            WHERE version = 1
              AND document ->> 'VersionMigratedBy' = '20260719_008';
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        8,
        "backfill_api_key_versions",
        UpSql,
        DownSql);
}
