using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropOrganizationVersionBackfill = """
            UPDATE organizations.organizations
            SET version = 0,
                document = document - 'Version' - 'OrganizationVersionMigratedBy',
                updated_at = transaction_timestamp()
            WHERE version = 1
              AND document ->> 'OrganizationVersionMigratedBy' = '20260720_012';
            """;
}
