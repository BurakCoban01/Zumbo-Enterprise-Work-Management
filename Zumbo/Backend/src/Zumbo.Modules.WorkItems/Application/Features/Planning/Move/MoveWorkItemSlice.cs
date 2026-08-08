namespace Zumbo.Modules.WorkItems;

internal sealed class MoveWorkItemSlice(
    MoveWorkItemPipeline pipeline,
    IWorkflowPolicy workflowPolicy,
    IBoardPlacementPolicy boardPlacementPolicy,
    WorkItemRankService ranks)
{
    internal async Task<WorkItemResponse> HandleAsync(
        MoveWorkItemCommand command,
        CancellationToken ct)
    {
        var initialWorkItem = await pipeline.GetWorkItemAsync(command.Id, ct);
        await using var structureLock = await pipeline.AcquireStructureLockAsync(
            initialWorkItem.ProjectId,
            ct);
        var workItem = await pipeline.GetForMoveAsync(command.Id, ct);
        var target = command.Request.Status.Trim();
        var aggregate = WorkItemAggregate.Rehydrate(workItem);
        aggregate.EnsureCanTarget(target);

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(
            workItem.ProjectId,
            workItem.Type,
            workItem.Status,
            target,
            ct);
        var preparedTransition = aggregate.PrepareTransition(rule, pipeline.UtcNow);
        var placement = await boardPlacementPolicy.EnsureCanMoveAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Id,
            target,
            ct);
        var targetRank = await ranks.NextRankAsync(
            workItem.BoardId,
            placement.ColumnId,
            workItem.Id,
            ct);

        if (rule.ToStatusCategory.Equals("Done", StringComparison.OrdinalIgnoreCase))
        {
            await pipeline.EnsureCanCompleteAsync(workItem, ct);
        }

        var oldStatus = await pipeline.PersistMoveAsync(
            workItem,
            placement,
            () => aggregate.Move(
                rule,
                placement,
                targetRank,
                preparedTransition,
                pipeline.UtcNow,
                pipeline.CurrentUserId),
            ct);
        await pipeline.PublishChangesAsync(
            workItem,
            oldStatus,
            placement,
            rule,
            command.CorrelationId,
            ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }
}
