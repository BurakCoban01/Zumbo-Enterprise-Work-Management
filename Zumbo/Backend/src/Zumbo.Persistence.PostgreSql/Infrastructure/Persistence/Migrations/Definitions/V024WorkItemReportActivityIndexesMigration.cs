namespace Zumbo.Persistence.PostgreSql;

internal static class V024WorkItemReportActivityIndexesMigration
{
        private const string UpSql = """
            CREATE INDEX IF NOT EXISTS ix_work_item_work_logs_project_cursor
                ON work_items.work_item_work_logs (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['ProjectId']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_item_timeline_project_cursor
                ON work_items.work_item_timeline (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['ProjectId']),
                    id);
            """;

        private const string DownSql = """
            DROP INDEX IF EXISTS work_items.ix_work_item_timeline_project_cursor;
            DROP INDEX IF EXISTS work_items.ix_work_item_work_logs_project_cursor;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        24,
        "work_item_report_activity_indexes",
        UpSql,
        DownSql);
}
