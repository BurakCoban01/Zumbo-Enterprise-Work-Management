using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropApiKeyVersionBackfill = """
            UPDATE identity.api_keys
            SET version = 0,
                document = document - 'Version' - 'VersionMigratedBy',
                updated_at = transaction_timestamp()
            WHERE version = 1
              AND document ->> 'VersionMigratedBy' = '20260719_008';
            """;
}
