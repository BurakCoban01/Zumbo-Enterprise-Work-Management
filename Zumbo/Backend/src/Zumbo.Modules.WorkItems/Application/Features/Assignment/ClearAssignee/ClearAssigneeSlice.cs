namespace Zumbo.Modules.WorkItems;

internal sealed class ClearAssigneeSlice(AssignmentMutationPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(
        ClearAssigneeCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForAssignmentAsync(command.Id, ct);
        if (workItem.AssigneeUserId is null)
        {
            return WorkItemResponseMapper.ToResponse(workItem);
        }

        var oldAssignee = workItem.AssigneeUserId;
        workItem.AssigneeUserId = null;
        return await pipeline.PersistClearAsync(
            workItem,
            oldAssignee,
            command.CorrelationId,
            ct);
    }
}
