using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class SetPlanningSlice(
    SetPlanningPipeline pipeline,
    IClock clock,
    IWorkItemSprintPolicy sprintPolicy)
{
    internal async Task<WorkItemResponse> HandleAsync(SetPlanningCommand command, CancellationToken ct)
    {
        var workItem = await pipeline.LoadForUpdateAsync(command.Id, ct);
        await sprintPolicy.EnsurePlanningAllowedAsync(
            workItem.ProjectId,
            workItem.SprintId,
            command.Request.SprintId,
            ct);
        var aggregate = WorkItemAggregate.Rehydrate(workItem);
        aggregate.Plan(command.Request.SprintId, command.Request.EstimatePoints, clock.UtcNow);
        return await pipeline.PersistAsync(workItem, ct);
    }
}
