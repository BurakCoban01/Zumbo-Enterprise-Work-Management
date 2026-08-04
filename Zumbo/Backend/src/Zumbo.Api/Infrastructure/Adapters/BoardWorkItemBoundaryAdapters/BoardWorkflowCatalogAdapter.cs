using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class BoardWorkflowCatalogAdapter(
    IDocumentRepository<WorkflowDefinitionDocument> workflows) : IBoardWorkflowCatalog
{
    public async Task EnsureStatusesAvailableAsync(
        string projectId,
        IReadOnlyCollection<string> statuses,
        CancellationToken ct)
    {
        var workflow = await workflows.SelectAsync(x => x.ProjectId == projectId, ct);
        if (workflow is null)
        {
            return;
        }

        var available = workflow.Statuses.Select(x => x.Name)
            .Concat(workflow.Draft?.Statuses.Select(x => x.Name) ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (statuses.Any(x => !available.Contains(x)))
        {
            throw new ConflictException("BOARD_WORKFLOW_STATUS_UNKNOWN", "Board mapping contains a status outside the published workflow or current draft.");
        }
    }
}
