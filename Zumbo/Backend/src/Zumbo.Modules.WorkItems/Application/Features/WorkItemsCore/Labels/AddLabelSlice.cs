using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class AddLabelSlice(LabelMutationPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(AddLabelCommand command, CancellationToken ct)
    {
        var label = command.Request.Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ValidationException("Label is required.");
        }

        var workItem = await pipeline.LoadForUpdateAsync(command.Id, ct);
        if (workItem.Labels.Any(x => x.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException(
                "WORK_ITEM_LABEL_EXISTS",
                "Work item already has this label.");
        }

        workItem.Labels.Add(label);
        return await pipeline.PersistAsync(
            workItem,
            "WorkItemLabelAdded",
            "Label added",
            "label:add:" + label,
            "label-added",
            label,
            ct);
    }
}
