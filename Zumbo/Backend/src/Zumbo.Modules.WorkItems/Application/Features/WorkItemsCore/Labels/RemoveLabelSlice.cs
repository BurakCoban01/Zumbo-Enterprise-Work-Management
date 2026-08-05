using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class RemoveLabelSlice(LabelMutationPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(RemoveLabelCommand command, CancellationToken ct)
    {
        var workItem = await pipeline.LoadForUpdateAsync(command.Id, ct);
        var removed = workItem.Labels.RemoveAll(
            label => label.Equals(command.Label, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new NotFoundException(
                "WORK_ITEM_LABEL_NOT_FOUND",
                "Work item label was not found.");
        }

        return await pipeline.PersistAsync(
            workItem,
            "WorkItemLabelRemoved",
            "Label removed",
            "label:remove:" + command.Label,
            "label-removed",
            command.Label,
            ct);
    }
}
