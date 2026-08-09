namespace Zumbo.Persistence.PostgreSql;

internal static class V006DurableConsumerDedupMigration
{
        private const string UpSql = """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_logs_deduplication_key
                ON audit.audit_logs ((document #>> ARRAY['DeduplicationKey']))
                WHERE document #>> ARRAY['DeduplicationKey'] IS NOT NULL;
            """;

        private const string DownSql = """
            DROP INDEX IF EXISTS audit.ux_audit_logs_deduplication_key;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        6,
        "create_durable_consumer_deduplication",
        UpSql,
        DownSql);
}
