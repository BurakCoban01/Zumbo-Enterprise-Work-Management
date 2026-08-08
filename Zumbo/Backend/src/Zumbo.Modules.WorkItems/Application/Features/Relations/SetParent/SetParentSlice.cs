using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class SetParentSlice(SetParentPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(SetParentCommand command, CancellationToken ct)
    {
        var initialWorkItem = await pipeline.GetWorkItemAsync(command.Id, ct);
        await using var structureLock = await pipeline.AcquireStructureLockAsync(
            initialWorkItem.ProjectId,
            ct);
        var workItem = await pipeline.GetForSetParentAsync(command.Id, ct);
        var parent = await pipeline.ValidateParentAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Type,
            command.Request.ParentId,
            workItem.Id,
            ct);
        var oldParentId = workItem.ParentId;

        if (string.Equals(oldParentId, parent?.Id, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "WORK_ITEM_PARENT_UNCHANGED",
                "Work item already has the requested parent.");
        }

        return await pipeline.PersistAsync(
            workItem,
            oldParentId,
            parent?.Id,
            command.CorrelationId,
            ct);
    }
}
