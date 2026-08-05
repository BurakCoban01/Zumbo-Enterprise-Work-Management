namespace Zumbo.Modules.WorkItems;

internal sealed class AssignWorkItemSlice(
    AssignmentMutationPipeline pipeline,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemNotificationPublisher notifications)
{
    internal async Task<WorkItemResponse> HandleAsync(
        AssignWorkItemCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForAssignmentAsync(command.Id, ct);
        if (workItem.TeamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(
                workItem.ProjectId,
                workItem.TeamId,
                command.Request.AssigneeUserId,
                ct);
        }

        var oldAssignee = workItem.AssigneeUserId;
        workItem.AssigneeUserId = command.Request.AssigneeUserId;
        return await pipeline.PersistAssignmentAsync(
            workItem,
            oldAssignee,
            command.Request.AssigneeUserId,
            command.CorrelationId,
            notifications,
            ct);
    }
}
