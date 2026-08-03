using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropAuditTenantIndexes = """
            DROP INDEX IF EXISTS audit.ux_audit_logs_organization_chain_sequence;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_actor_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_entity_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_created;
            """;
}
