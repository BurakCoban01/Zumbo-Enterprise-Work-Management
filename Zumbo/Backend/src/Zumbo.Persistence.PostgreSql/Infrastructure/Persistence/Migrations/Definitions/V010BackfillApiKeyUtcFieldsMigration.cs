namespace Zumbo.Persistence.PostgreSql;

internal static class V010BackfillApiKeyUtcFieldsMigration
{
        private const string UpSql = """
            UPDATE identity.api_keys
            SET document = jsonb_set(
                    jsonb_set(
                        document,
                        ARRAY['ExpiresAtUtc'],
                        document -> 'ExpiresAt',
                        true),
                    ARRAY['ExpiresAtUtcMigratedBy'],
                    '"20260719_010"'::jsonb,
                    true),
                updated_at = transaction_timestamp()
            WHERE NOT document ? 'ExpiresAtUtc'
              AND document ? 'ExpiresAt';

            UPDATE identity.api_keys
            SET document = jsonb_set(
                    jsonb_set(
                        document,
                        ARRAY['RevokedAtUtc'],
                        document -> 'RevokedAt',
                        true),
                    ARRAY['RevokedAtUtcMigratedBy'],
                    '"20260719_010"'::jsonb,
                    true),
                updated_at = transaction_timestamp()
            WHERE NOT document ? 'RevokedAtUtc'
              AND document ? 'RevokedAt';
            """;

        private const string DownSql = """
            UPDATE identity.api_keys
            SET document = document - 'ExpiresAtUtc' - 'ExpiresAtUtcMigratedBy',
                updated_at = transaction_timestamp()
            WHERE document ->> 'ExpiresAtUtcMigratedBy' = '20260719_010';

            UPDATE identity.api_keys
            SET document = document - 'RevokedAtUtc' - 'RevokedAtUtcMigratedBy',
                updated_at = transaction_timestamp()
            WHERE document ->> 'RevokedAtUtcMigratedBy' = '20260719_010';
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        10,
        "backfill_api_key_utc_fields",
        UpSql,
        DownSql);
}
