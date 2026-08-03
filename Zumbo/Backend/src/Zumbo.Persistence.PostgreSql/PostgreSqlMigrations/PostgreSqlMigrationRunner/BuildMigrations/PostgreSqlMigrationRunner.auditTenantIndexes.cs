using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string auditTenantIndexes = """
            CREATE INDEX IF NOT EXISTS ix_audit_logs_organization_created
                ON audit.audit_logs ((document ->> 'OrganizationId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC, id);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_organization_entity_created
                ON audit.audit_logs ((document ->> 'OrganizationId'), (document ->> 'EntityType'), (document ->> 'EntityId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC, id);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_organization_actor_created
                ON audit.audit_logs ((document ->> 'OrganizationId'), (document ->> 'ActorUserId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC, id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_logs_organization_chain_sequence
                ON audit.audit_logs ((document ->> 'OrganizationId'), ((document ->> 'ChainSequence')::bigint))
                WHERE (document ->> 'ChainSequence')::bigint > 0;
            """;
}
