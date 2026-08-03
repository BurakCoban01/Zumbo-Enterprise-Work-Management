using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropApiKeyExpiryIndex = """
            DROP INDEX IF EXISTS identity.ix_api_keys_expires_utc;
            """;
}
