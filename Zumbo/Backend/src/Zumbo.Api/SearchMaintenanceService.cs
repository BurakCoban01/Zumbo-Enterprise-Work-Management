using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;

public sealed class SearchMaintenanceService(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemSearchIndex searchIndex,
    IOptions<OpenSearchOptions> options)
{
    public async Task<WorkItemSearchRebuildResult> RebuildAsync(CancellationToken cancellationToken)
    {
        var maximum = Math.Clamp(options.Value.MaxReindexItems, 1, 100_000);
        var organizationByProject = new Dictionary<string, string>(StringComparer.Ordinal);
        string? projectCursor = null;
        do
        {
            var page = await projects.ListByCursorAsync(
                project => !project.Archived,
                projectCursor,
                Math.Min(200, maximum),
                cancellationToken);
            foreach (var project in page.Items)
            {
                if (organizationByProject.Count >= maximum)
                    throw new InvalidOperationException($"Search rebuild exceeds the configured limit of {maximum} projects.");
                organizationByProject[project.Id] = project.OrganizationId;
            }
            projectCursor = page.NextCursor;
        } while (projectCursor is not null);

        var records = new List<WorkItemSearchRecord>(Math.Min(maximum, 1_000));
        string? itemCursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                item => !item.Archived,
                itemCursor,
                Math.Min(200, maximum + 1 - records.Count),
                cancellationToken);
            foreach (var item in page.Items)
            {
                if (records.Count >= maximum)
                    throw new InvalidOperationException($"Search rebuild exceeds the configured limit of {maximum} work items.");
                if (!organizationByProject.TryGetValue(item.ProjectId, out var organizationId))
                    continue;
                records.Add(WorkItemService.ToSearchRecord(item, organizationId));
            }
            itemCursor = page.NextCursor;
        } while (itemCursor is not null);

        return await searchIndex.RebuildAsync(records, cancellationToken);
    }
}
