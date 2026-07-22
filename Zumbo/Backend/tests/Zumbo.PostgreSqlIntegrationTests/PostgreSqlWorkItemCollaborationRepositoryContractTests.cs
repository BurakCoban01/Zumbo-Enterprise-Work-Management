using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlWorkItemCollaborationRepositoryContractTests(PostgreSqlFixture fixture)
    : WorkItemCollaborationRepositoryContract
{
    [Fact]
    public async Task Migration19_CreatesCollaborationAndRecurrenceTablesAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'work_items'
              AND table_name IN (
                  'work_item_collaborations',
                  'work_item_event_activities',
                  'work_item_templates',
                  'work_item_recurrences',
                  'work_item_recurrence_occurrences');
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*)
            FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                  'ux_workitem_collaboration_owner',
                  'ix_workitem_event_activity_owner_created',
                  'ux_workitem_templates_active_project_name_ci',
                  'ix_workitem_templates_project_archived_name',
                  'ix_workitem_recurrences_due',
                  'ix_workitem_recurrences_project_archived_created',
                  'ux_workitem_recurrence_occurrence_schedule',
                  'ix_workitem_recurrence_occurrence_status_schedule');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);

        Assert.Equal(5, tables);
        Assert.Equal(8, indexes);
        Assert.Contains("19:work_item_collaboration_and_recurrence", applied);
    }

    protected override IDocumentRepository<WorkItemCollaborationDocument> Collaborations() => fixture.Api.CreateRepository<WorkItemCollaborationDocument>("work_items", "work_item_collaborations");
    protected override IDocumentRepository<WorkItemEventActivityDocument> Activities() => fixture.Api.CreateRepository<WorkItemEventActivityDocument>("work_items", "work_item_event_activities");
    protected override IDocumentRepository<WorkItemTemplateDocument> Templates() => fixture.Api.CreateRepository<WorkItemTemplateDocument>("work_items", "work_item_templates");
    protected override IDocumentRepository<WorkItemRecurrenceDocument> Recurrences() => fixture.Api.CreateRepository<WorkItemRecurrenceDocument>("work_items", "work_item_recurrences");
    protected override IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> Occurrences() => fixture.Api.CreateRepository<WorkItemRecurrenceOccurrenceDocument>("work_items", "work_item_recurrence_occurrences");
}
