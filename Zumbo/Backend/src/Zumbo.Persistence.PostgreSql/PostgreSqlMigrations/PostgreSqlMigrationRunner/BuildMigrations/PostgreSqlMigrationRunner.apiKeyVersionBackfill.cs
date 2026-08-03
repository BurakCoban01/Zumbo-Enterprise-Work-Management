using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string apiKeyVersionBackfill = """
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
}
