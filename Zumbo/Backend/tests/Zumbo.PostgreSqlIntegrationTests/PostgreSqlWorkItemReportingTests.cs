using System.Data.Common;
using System.Text.Json;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlWorkItemReportingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ReportingMigrationAndLargeProjectCursorMeetPlanAndLoadBudget()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var reportingIndexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*)
            FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                'ix_work_items_project_archived_id',
                'ix_work_items_project_archived_created',
                'ix_work_items_project_archived_completed',
                'ix_work_items_project_archived_assignee',
                'ix_work_items_project_archived_team_created',
                'ix_work_item_work_logs_project_cursor',
                'ix_work_item_timeline_project_cursor');
            """);
        Assert.Equal(7, reportingIndexes);
        var migrations = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Contains("23:work_item_reporting_indexes", migrations);
        Assert.Contains("24:work_item_report_activity_indexes", migrations);

        await PostgreSqlFixture.ExecuteAsync(connection, """
            INSERT INTO work_items.work_items (id, version, document)
            SELECT
                'report-plan-' || lpad(value::text, 5, '0'),
                1,
                jsonb_build_object(
                    'Id', 'report-plan-' || lpad(value::text, 5, '0'),
                    'Version', 1,
                    'ProjectId', CASE WHEN value <= 1000 THEN 'report-target' ELSE 'report-other' END,
                    'Archived', false,
                    'TeamId', CASE WHEN value % 2 = 0 THEN 'team-a' ELSE 'team-b' END,
                    'CreatedAt', to_jsonb(to_char(
                        timestamptz '2026-07-01T00:00:00Z' + value * interval '1 minute',
                        'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')))
            FROM generate_series(1, 5000) AS value;
            INSERT INTO work_items.work_item_work_logs (id, version, document)
            SELECT
                'report-log-' || lpad(value::text, 5, '0'),
                1,
                jsonb_build_object(
                    'Id', 'report-log-' || lpad(value::text, 5, '0'),
                    'Version', 1,
                    'OrganizationId', 'report-org',
                    'ProjectId', CASE WHEN value <= 1000 THEN 'report-target' ELSE 'report-other' END,
                    'WorkItemId', 'report-plan-' || lpad(value::text, 5, '0'),
                    'Hours', 1,
                    'CreatedAt', '2026-07-01T00:00:00Z')
            FROM generate_series(1, 5000) AS value;
            ANALYZE work_items.work_items;
            ANALYZE work_items.work_item_work_logs;
            """);

        try
        {
            var planJson = await ScalarStringAsync(connection, """
                EXPLAIN (ANALYZE, COSTS OFF, FORMAT JSON)
                SELECT document
                FROM work_items.work_items
                WHERE (document #>> ARRAY['ProjectId']) = 'report-target'
                  AND ((document #>> ARRAY['Archived'])::boolean) = false
                  AND id COLLATE "C" > 'report-plan-00199' COLLATE "C"
                ORDER BY id COLLATE "C"
                LIMIT 200;
                """);
            using var document = JsonDocument.Parse(planJson);
            var root = document.RootElement[0];

            Assert.Contains("ix_work_items_project_archived_id", planJson, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan", planJson, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(root.GetProperty("Execution Time").GetDouble(), 0, 250);

            var activityPlan = await ScalarStringAsync(connection, """
                EXPLAIN (ANALYZE, COSTS OFF, FORMAT JSON)
                SELECT document
                FROM work_items.work_item_work_logs
                WHERE (document #>> ARRAY['OrganizationId']) = 'report-org'
                  AND (document #>> ARRAY['ProjectId']) = 'report-target'
                  AND id COLLATE "C" > 'report-log-00199' COLLATE "C"
                ORDER BY id COLLATE "C"
                LIMIT 200;
                """);
            using var activityDocument = JsonDocument.Parse(activityPlan);
            Assert.Contains("ix_work_item_work_logs_project_cursor", activityPlan, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan", activityPlan, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(
                activityDocument.RootElement[0].GetProperty("Execution Time").GetDouble(),
                0,
                250);
        }
        finally
        {
            await PostgreSqlFixture.ExecuteAsync(
                connection,
                "DELETE FROM work_items.work_item_work_logs WHERE id LIKE 'report-log-%';");
            await PostgreSqlFixture.ExecuteAsync(
                connection,
                "DELETE FROM work_items.work_items WHERE id LIKE 'report-plan-%';");
        }
    }

    private static async Task<string> ScalarStringAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
