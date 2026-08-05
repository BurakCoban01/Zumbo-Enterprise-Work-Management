using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class UpdateWorkItemSlice(UpdateWorkItemPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(
        UpdateWorkItemCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadAsync(command.Id, ct);
        var oldValue = UpdateWorkItemPipeline.AuditValue(workItem);
        if (!string.IsNullOrWhiteSpace(command.Request.Title))
        {
            if (command.Request.Title.Length > 200)
            {
                throw new ValidationException("Work item title cannot exceed 200 characters.");
            }

            workItem.Title = command.Request.Title.Trim();
        }

        if (command.Request.Description is not null)
        {
            workItem.Description = command.Request.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(command.Request.Priority))
        {
            workItem.Priority = command.Request.Priority.Trim();
        }

        if (workItem.DueDate != command.Request.DueDate)
        {
            workItem.DueReminderSentAt = null;
        }

        workItem.DueDate = command.Request.DueDate;
        return await pipeline.PersistAsync(
            workItem,
            oldValue,
            command.CorrelationId,
            ct);
    }
}
