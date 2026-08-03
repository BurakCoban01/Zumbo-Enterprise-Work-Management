using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string durableConsumerDeduplication = """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_logs_deduplication_key
                ON audit.audit_logs ((document #>> ARRAY['DeduplicationKey']))
                WHERE document #>> ARRAY['DeduplicationKey'] IS NOT NULL;
            """;
}
