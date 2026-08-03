using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string organizationVersionBackfill = """
            UPDATE organizations.organizations
            SET version = 1,
                document = jsonb_set(
                    jsonb_set(document, ARRAY['Version'], '1'::jsonb, true),
                    ARRAY['Status'],
                    COALESCE(document -> 'Status', '"Active"'::jsonb),
                    true)
                    || jsonb_build_object('OrganizationVersionMigratedBy', '20260720_012'),
                updated_at = transaction_timestamp()
            WHERE version = 0;
            """;
}
