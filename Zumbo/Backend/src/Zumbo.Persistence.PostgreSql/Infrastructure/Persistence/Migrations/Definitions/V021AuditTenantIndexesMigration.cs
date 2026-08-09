namespace Zumbo.Persistence.PostgreSql;

internal static class V021AuditTenantIndexesMigration
{
        private const string UpSql = """
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

        private const string DownSql = """
            DROP INDEX IF EXISTS audit.ux_audit_logs_organization_chain_sequence;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_actor_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_entity_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_created;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        21,
        "audit_tenant_indexes",
        UpSql,
        DownSql);
}
