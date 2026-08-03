using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string apiKeyExpiryIndex = """
            CREATE INDEX IF NOT EXISTS ix_api_keys_expires_utc
                ON identity.api_keys (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAt']),
                    id);
            """;
}
