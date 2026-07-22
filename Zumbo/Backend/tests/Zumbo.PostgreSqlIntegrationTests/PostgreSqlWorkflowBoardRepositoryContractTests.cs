using Zumbo.Modules.Boards;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlWorkflowBoardRepositoryContractTests(PostgreSqlFixture fixture)
    : WorkflowBoardRepositoryContract
{
    [Fact]
    public async Task Migration15_BackfillsWorkflowAndCreatesWipProjection()
    {
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        var sprintLifecycle = Assert.Single(applied, x => x.StartsWith("16:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(sprintLifecycle, CancellationToken.None);
        var latest = Assert.Single(applied, x => x.StartsWith("15:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        var suffix = Guid.NewGuid().ToString("N");
        var workflowId = "legacy-workflow-" + suffix;
        var workItemId = "legacy-wip-" + suffix;
        var boardId = "legacy-board-" + suffix;
        var columnId = "legacy-column-" + suffix;
        try
        {
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO workflows.workflow_definitions (id, version, document)
                VALUES ('{workflowId}', 0, jsonb_build_object(
                    'Id', '{workflowId}',
                    'ProjectId', 'legacy-project-{suffix}',
                    'Statuses', jsonb_build_array(
                        jsonb_build_object('Name', 'Open', 'Category', 'Todo'),
                        jsonb_build_object('Name', 'Done', 'Category', 'Done')),
                    'Transitions', jsonb_build_array(
                        jsonb_build_object('FromStatus', 'Open', 'ToStatus', 'Done')),
                    'CreatedAt', '2026-07-20T18:30:00+00:00',
                    'UpdatedAt', '2026-07-20T18:30:00+00:00',
                    'Version', 0));
                INSERT INTO work_items.work_items (id, version, document)
                VALUES ('{workItemId}', 0, jsonb_build_object(
                    'Id', '{workItemId}',
                    'ProjectId', 'legacy-project-{suffix}',
                    'BoardId', '{boardId}',
                    'ColumnId', '{columnId}',
                    'Status', 'Open',
                    'Archived', false,
                    'Version', 0));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
            var workflows = provider.CreateRepository<WorkflowDefinitionDocument>("workflows", "workflow_definitions");
            var projections = provider.CreateRepository<BoardColumnWipProjectionDocument>("work_items", "board_column_wip_projections");
            var migrated = await workflows.SelectAsync(x => x.Id == workflowId);
            var projection = await projections.SelectAsync(x => x.Id == $"{boardId}:{columnId}");
            Assert.NotNull(migrated);
            Assert.Equal(1, migrated!.PublishedVersion);
            Assert.Single(migrated.PublishedVersions);
            Assert.Single(migrated.IssueTypeSchemes);
            Assert.NotNull(projection);
            Assert.Equal(1, projection!.ActiveCount);
        }
        finally
        {
            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                DELETE FROM work_items.board_column_wip_projections WHERE id = '{boardId}:{columnId}';
                DELETE FROM work_items.work_items WHERE id = '{workItemId}';
                DELETE FROM workflows.workflow_definitions WHERE id = '{workflowId}';
                """);
        }
    }

    [Fact]
    public async Task Migration16_BackfillsProviderNeutralSprintAggregate()
    {
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        var latest = Assert.Single(applied, x => x.StartsWith("16:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        var suffix = Guid.NewGuid().ToString("N");
        var projectId = "legacy-sprint-project-" + suffix;
        var firstId = "legacy-sprint-a-" + suffix;
        var secondId = "legacy-sprint-b-" + suffix;
        try
        {
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO work_items.work_items (id, version, document)
                VALUES
                    ('{firstId}', 2, jsonb_build_object(
                        'Id', '{firstId}', 'ProjectId', '{projectId}', 'SprintId', 'Sprint 42', 'Version', 2)),
                    ('{secondId}', 0, jsonb_build_object(
                        'Id', '{secondId}', 'ProjectId', '{projectId}', 'SprintId', 'Sprint 42', 'Version', 0));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            var expectedSprintId = await PostgreSqlFixture.ScalarAsync<string>(connection,
                $"SELECT 'legacy-' || md5('{projectId}:Sprint 42');");
            var migratedCount = await PostgreSqlFixture.ScalarAsync<long>(connection, $"""
                SELECT count(*)
                FROM work_items.work_items
                WHERE id IN ('{firstId}', '{secondId}')
                  AND document ->> 'SprintId' = '{expectedSprintId}'
                  AND document ->> 'SprintLifecycleMigratedBy' = '20260720_016';
                """);
            var sprintStatus = await PostgreSqlFixture.ScalarAsync<string>(connection, $"""
                SELECT document ->> 'Status' FROM work_items.sprints WHERE id = '{expectedSprintId}';
                """);

            Assert.Equal(2, migratedCount);
            Assert.Equal(39, expectedSprintId.Length);
            Assert.Equal("Planned", sprintStatus);
        }
        finally
        {
            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                DELETE FROM work_items.work_items WHERE id IN ('{firstId}', '{secondId}');
                DELETE FROM work_items.sprints WHERE document ->> 'ProjectId' = '{projectId}';
                """);
        }
    }

    [Fact]
    public async Task Migration17_BackfillsTypedFieldShapeAndProjectSchema()
    {
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        var latest = Assert.Single(applied, x => x.StartsWith("17:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        var suffix = Guid.NewGuid().ToString("N");
        var projectId = "legacy-schema-project-" + suffix;
        var workItemId = "legacy-schema-item-" + suffix;
        try
        {
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO work_items.work_items (id, version, document)
                VALUES ('{workItemId}', 0, jsonb_build_object(
                    'Id', '{workItemId}',
                    'ProjectId', '{projectId}',
                    'Type', 'Task',
                    'Version', 0));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
            var workItems = provider.CreateRepository<WorkItemDocument>("work_items", "work_items");
            var schemas = provider.CreateRepository<WorkItemTypeSchemaDocument>(
                "work_items", "work_item_type_schemas");
            var migrated = await workItems.SelectAsync(item => item.Id == workItemId);
            var schema = await schemas.SelectAsync(item => item.ProjectId == projectId);

            Assert.NotNull(migrated);
            Assert.Equal(1, migrated!.IssueTypeSchemaVersion);
            Assert.Empty(migrated.CustomFields);
            Assert.NotNull(schema);
            Assert.Equal(5, schema!.IssueTypes.Count);
            Assert.Contains(schema.IssueTypes, type => type.Key == "Task");
        }
        finally
        {
            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                DELETE FROM work_items.work_items WHERE id = '{workItemId}';
                DELETE FROM work_items.work_item_type_schemas WHERE id = '{projectId}';
                """);
        }
    }

    protected override Task<WorkflowBoardRepositoryFixture> CreateFixtureAsync()
    {
        var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
        return Task.FromResult<WorkflowBoardRepositoryFixture>(new Fixture(provider));
    }

    private sealed class Fixture(PostgreSqlProvider provider)
        : WorkflowBoardRepositoryFixture(
            provider.CreateRepository<WorkflowDefinitionDocument>("workflows", "workflow_definitions"),
            provider.CreateRepository<BoardDocument>("boards", "boards"),
            provider.CreateRepository<WorkItemDocument>("work_items", "work_items"),
            provider.CreateRepository<BoardColumnWipProjectionDocument>("work_items", "board_column_wip_projections"),
            provider.CreateRepository<SprintDocument>("work_items", "sprints"),
            provider.CreateRepository<SprintScopeSnapshotDocument>("work_items", "sprint_scope_snapshots"),
            provider.CreateRepository<SprintCompletionSnapshotDocument>("work_items", "sprint_completion_snapshots"))
    {
        public override async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }
}
