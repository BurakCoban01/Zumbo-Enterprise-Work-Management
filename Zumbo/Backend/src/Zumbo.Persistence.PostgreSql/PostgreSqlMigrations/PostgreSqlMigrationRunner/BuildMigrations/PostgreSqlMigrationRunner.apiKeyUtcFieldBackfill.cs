using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string apiKeyUtcFieldBackfill = """
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
}
