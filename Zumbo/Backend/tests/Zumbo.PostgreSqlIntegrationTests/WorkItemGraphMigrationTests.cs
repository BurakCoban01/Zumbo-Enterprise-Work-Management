using Zumbo.Modules.WorkItems;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class WorkItemGraphMigrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationBackfill_IsProviderDeterministicAndDependencyQueryUsesIndex()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var projectId = "graph-project-" + suffix;
        var sourceId = "graph-source-" + suffix;
        var targetId = "graph-target-" + suffix;
        var expectedEdgeId = WorkItemGraphService.EdgeId(projectId, sourceId, targetId, "Blocks");
        var migrationApplied = true;

        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        try
        {
            await fixture.Api.RollbackAsync("18:work_item_relation_graph", CancellationToken.None);
            migrationApplied = false;
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO work_items.work_items (id, version, document)
                VALUES
                    ('{sourceId}', 0, jsonb_build_object(
                        'Id', '{sourceId}',
                        'ProjectId', '{projectId}',
                        'Relations', jsonb_build_array(jsonb_build_object(
                            'RelatedWorkItemId', '{targetId}',
                            'RelationType', 'Blocks')),
                        'Version', 0)),
                    ('{targetId}', 0, jsonb_build_object(
                        'Id', '{targetId}',
                        'ProjectId', '{projectId}',
                        'Relations', '[]'::jsonb,
                        'Version', 0));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            migrationApplied = true;

            var edgeCount = await PostgreSqlFixture.ScalarAsync<long>(connection, $"""
                SELECT count(*)
                FROM work_items.work_item_relation_edges
                WHERE id = '{expectedEdgeId}'
                  AND document ->> 'DependencyFromWorkItemId' = '{sourceId}'
                  AND document ->> 'DependencyToWorkItemId' = '{targetId}';
                """);
            Assert.Equal(1, edgeCount);

            await PostgreSqlFixture.ExecuteAsync(connection, """
                INSERT INTO work_items.work_item_relation_edges (id, version, document)
                SELECT
                    md5(value::text),
                    0,
                    jsonb_build_object(
                        'Id', md5(value::text),
                        'ProjectId', 'plan-project',
                        'SourceWorkItemId', 'source-' || value,
                        'TargetWorkItemId', 'target-' || value,
                        'RelationType', 'Blocks',
                        'DependencyFromWorkItemId', CASE WHEN value = 1337 THEN 'needle' ELSE 'source-' || value END,
                        'DependencyToWorkItemId', CASE WHEN value = 1555 THEN 'blocked-needle' ELSE 'target-' || value END,
                        'CreatedAt', transaction_timestamp(),
                        'Version', 0)
                FROM generate_series(1, 5000) value
                ON CONFLICT (id) DO NOTHING;
                INSERT INTO work_items.work_items (id, version, document)
                SELECT
                    md5('graph-plan-workitem-' || value),
                    0,
                    jsonb_build_object(
                        'Id', md5('graph-plan-workitem-' || value),
                        'ProjectId', 'plan-project',
                        'ParentId', CASE WHEN value = 1777 THEN 'parent-needle' ELSE 'parent-' || value END,
                        'Archived', false,
                        'Version', 0)
                FROM generate_series(1, 5000) value
                ON CONFLICT (id) DO NOTHING;
                ANALYZE work_items.work_item_relation_edges;
                ANALYZE work_items.work_items;
                SET enable_seqscan = off;
                """);
            var plan = await PostgreSqlFixture.ScalarAsync<string>(connection, """
                EXPLAIN (FORMAT JSON)
                SELECT id
                FROM work_items.work_item_relation_edges
                WHERE document ->> 'ProjectId' = 'plan-project'
                  AND document ->> 'DependencyFromWorkItemId' = 'needle';
                """);
            var reversePlan = await PostgreSqlFixture.ScalarAsync<string>(connection, """
                EXPLAIN (FORMAT JSON)
                SELECT id
                FROM work_items.work_item_relation_edges
                WHERE document ->> 'ProjectId' = 'plan-project'
                  AND document ->> 'DependencyToWorkItemId' = 'blocked-needle';
                """);
            var parentPlan = await PostgreSqlFixture.ScalarAsync<string>(connection, """
                EXPLAIN (FORMAT JSON)
                SELECT id
                FROM work_items.work_items
                WHERE document ->> 'ProjectId' = 'plan-project'
                  AND document ->> 'ParentId' = 'parent-needle'
                  AND (document ->> 'Archived')::boolean = false;
                """);

            Assert.Contains("ix_work_item_relation_edges_dependency_from", plan, StringComparison.Ordinal);
            Assert.Contains("ix_work_item_relation_edges_dependency_to", reversePlan, StringComparison.Ordinal);
            Assert.Contains("ix_work_items_project_parent_archived", parentPlan, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan", plan, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan", reversePlan, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan", parentPlan, StringComparison.Ordinal);
        }
        finally
        {
            if (!migrationApplied)
            {
                await fixture.Api.MigrateAsync(CancellationToken.None);
            }

            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                DELETE FROM work_items.work_item_relation_edges
                WHERE document ->> 'ProjectId' IN ('{projectId}', 'plan-project');
                DELETE FROM work_items.work_items
                WHERE id IN ('{sourceId}', '{targetId}')
                   OR document ->> 'ProjectId' = 'plan-project';
                RESET enable_seqscan;
                """);
        }
    }
}
