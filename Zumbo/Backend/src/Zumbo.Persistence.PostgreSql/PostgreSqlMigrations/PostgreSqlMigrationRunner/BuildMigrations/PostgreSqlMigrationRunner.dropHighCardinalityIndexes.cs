using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropHighCardinalityIndexes = """
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_owner_last_seen;
            DROP INDEX IF EXISTS projects.ix_projects_organization_archived_key_cursor;
            """;
}
