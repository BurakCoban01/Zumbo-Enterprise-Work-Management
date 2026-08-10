using Zumbo.Modules.WorkItems.Application.Features.Sprints;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService
{
    public async Task<SprintPlannedItemResponse> PlanAsync(
        string sprintId,
        string workItemId,
        PlanSprintWorkItemRequest request,
        string correlationId,
        CancellationToken ct) =>
        await planSprintWorkItemHandler.HandleAsync(
            new PlanSprintWorkItemCommand(sprintId, workItemId, request, correlationId), ct);

    public async Task<SprintPlannedItemResponse> UnplanAsync(
        string sprintId,
        string workItemId,
        string correlationId,
        CancellationToken ct) =>
        await unplanSprintWorkItemHandler.HandleAsync(
            new UnplanSprintWorkItemCommand(sprintId, workItemId, correlationId), ct);
}
