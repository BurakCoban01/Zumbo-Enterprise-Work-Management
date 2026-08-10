namespace Zumbo.Persistence.PostgreSql;

internal static class V023WorkItemReportingIndexesMigration
{
        private const string UpSql = """
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_id
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_created
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_completed
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CompletedAt']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_assignee
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    (document #>> ARRAY['AssigneeUserId']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_team_created
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    (document #>> ARRAY['TeamId']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']),
                    id);
            """;

        private const string DownSql = """
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_team_created;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_assignee;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_completed;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_created;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_id;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        23,
        "work_item_reporting_indexes",
        UpSql,
        DownSql);
}
