using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string privacyWorkflowUtcIndex = """
            CREATE INDEX IF NOT EXISTS ix_privacy_workflows_retention_utc
                ON identity.privacy_workflows (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            """;
}
