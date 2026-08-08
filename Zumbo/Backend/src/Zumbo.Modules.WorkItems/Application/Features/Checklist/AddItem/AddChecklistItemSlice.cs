namespace Zumbo.Modules.WorkItems;

internal sealed class AddChecklistItemSlice(ChecklistMutationPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(
        AddChecklistItemCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForUpdateAsync(command.Id, ct);
        workItem.Checklist.Add(new ChecklistItemDocument { Text = command.Request.Text.Trim() });
        var checklistItem = workItem.Checklist[^1];
        return await pipeline.PersistAsync(
            workItem,
            "WorkItemChecklistItemAdded",
            "Checklist item added",
            checklistItem.Id,
            ct);
    }
}
