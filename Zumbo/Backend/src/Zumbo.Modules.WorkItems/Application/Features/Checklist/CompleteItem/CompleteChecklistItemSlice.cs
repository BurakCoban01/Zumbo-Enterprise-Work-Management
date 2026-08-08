using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class CompleteChecklistItemSlice(ChecklistMutationPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(
        CompleteChecklistItemCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForUpdateAsync(command.Id, ct);
        var item = workItem.Checklist.SingleOrDefault(x => x.Id == command.ItemId)
            ?? throw new NotFoundException(
                "CHECKLIST_ITEM_NOT_FOUND",
                "Checklist item was not found.");
        item.Completed = command.Request.Completed;
        return await pipeline.PersistMutationAsync(
            workItem,
            "WorkItemChecklistItemUpdated",
            command.Request.Completed ? "Checklist item completed" : "Checklist item reopened",
            "checklist:" + command.ItemId,
            ct);
    }
}
