namespace Zumbo.Persistence.PostgreSql;

internal static class V037HighCardinalityIndexesMigration
{
        private const string UpSql = """
            CREATE INDEX IF NOT EXISTS ix_projects_organization_archived_key_cursor
                ON projects.projects (
                    (document #>> ARRAY['OrganizationId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    (document #>> ARRAY['Key']),
                    id COLLATE "C");
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_owner_last_seen
                ON identity.refresh_sessions (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    public.zumbo_parse_timestamptz(
                        document #>> ARRAY['LastSeenAt']) DESC NULLS LAST,
                    id COLLATE "C");
            """;

        private const string DownSql = """
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_owner_last_seen;
            DROP INDEX IF EXISTS projects.ix_projects_organization_archived_key_cursor;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        37,
        "high_cardinality_indexes",
        UpSql,
        DownSql);
}
