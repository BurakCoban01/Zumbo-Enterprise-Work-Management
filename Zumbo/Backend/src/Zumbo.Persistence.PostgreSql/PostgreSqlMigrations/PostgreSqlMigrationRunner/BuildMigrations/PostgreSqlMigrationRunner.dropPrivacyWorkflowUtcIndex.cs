using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropPrivacyWorkflowUtcIndex = """
            DROP INDEX IF EXISTS identity.ix_privacy_workflows_retention_utc;
            """;
}
