namespace Zumbo.Modules.WorkItems;

internal sealed class ReorderWorkItemSlice(
    ReorderWorkItemPipeline pipeline,
    WorkItemRankService ranks)
{
    internal async Task<WorkItemResponse> HandleAsync(
        ReorderWorkItemCommand command,
        CancellationToken ct)
    {
        var initialWorkItem = await pipeline.GetWorkItemAsync(command.Id, ct);
        await using var structureLock = await pipeline.AcquireStructureLockAsync(
            initialWorkItem.ProjectId,
            ct);
        var workItem = await pipeline.GetForReorderAsync(command.Id, ct);
        var rank = await ranks.ResolveReorderRankAsync(workItem, command.Request, ct);
        return await pipeline.PersistAsync(workItem, rank, command.CorrelationId, ct);
    }
}
