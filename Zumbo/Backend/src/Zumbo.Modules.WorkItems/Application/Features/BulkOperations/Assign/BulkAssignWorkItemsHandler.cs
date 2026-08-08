using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Assign;

public sealed class BulkAssignWorkItemsHandler(AssignWorkItemHandler assignWorkItemHandler)
{
    public Task<BulkWorkItemResponse> HandleAsync(
        BulkAssignWorkItemsCommand command,
        CancellationToken ct) =>
        BulkWorkItemExecutor.ExecuteAsync(
            command.Request.WorkItemIds,
            (id, correlationId, token) => assignWorkItemHandler.HandleAsync(
                new AssignWorkItemCommand(
                    id,
                    new AssignWorkItemRequest(command.Request.AssigneeUserId),
                    correlationId),
                token),
            command.CorrelationId,
            ct);
}
