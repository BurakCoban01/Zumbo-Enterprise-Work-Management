using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropDurableConsumerDeduplication = """
            DROP INDEX IF EXISTS audit.ux_audit_logs_deduplication_key;
            """;
}
