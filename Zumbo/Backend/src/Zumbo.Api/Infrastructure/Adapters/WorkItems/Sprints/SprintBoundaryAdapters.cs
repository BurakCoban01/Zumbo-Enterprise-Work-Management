using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class WorkItemSprintPolicyAdapter(
    IDocumentRepository<SprintDocument> sprints) : IWorkItemSprintPolicy
{
    public async Task EnsurePlanningAllowedAsync(
        string projectId,
        string? currentSprintId,
        string? targetSprintId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(currentSprintId))
        {
            var current = await sprints.SelectAsync(sprint => sprint.Id == currentSprintId, ct)
                ?? throw new ConflictException("SPRINT_NOT_FOUND", "Current sprint was not found.");
            EnsureSameProjectAndPlanned(current, projectId);
        }

        if (!string.IsNullOrWhiteSpace(targetSprintId))
        {
            var target = await sprints.SelectAsync(sprint => sprint.Id == targetSprintId, ct)
                ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Target sprint was not found.");
            EnsureSameProjectAndPlanned(target, projectId);
        }
    }

    private static void EnsureSameProjectAndPlanned(SprintDocument sprint, string projectId)
    {
        if (sprint.ProjectId != projectId)
        {
            throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Sprint and work item must belong to the same project.");
        }

        if (sprint.Status != SprintStatuses.Planned)
        {
            throw new ConflictException("SPRINT_PLANNING_CLOSED", "Only a planned sprint can change scope.");
        }
    }
}
