using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropApiKeyUtcFieldBackfill = """
            UPDATE identity.api_keys
            SET document = document - 'ExpiresAtUtc' - 'ExpiresAtUtcMigratedBy',
                updated_at = transaction_timestamp()
            WHERE document ->> 'ExpiresAtUtcMigratedBy' = '20260719_010';

            UPDATE identity.api_keys
            SET document = document - 'RevokedAtUtc' - 'RevokedAtUtcMigratedBy',
                updated_at = transaction_timestamp()
            WHERE document ->> 'RevokedAtUtcMigratedBy' = '20260719_010';
            """;
}
