using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropProjectLifecycleMarker = """
            UPDATE projects.projects
            SET version = version + 1,
                document = (document - 'ProjectLifecycleMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'ProjectLifecycleMigratedBy' = '20260720_014';
            """;
}
