using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Move;

public sealed class BulkMoveWorkItemsHandler(MoveWorkItemHandler moveWorkItemHandler)
{
    public Task<BulkWorkItemResponse> HandleAsync(
        BulkMoveWorkItemsCommand command,
        CancellationToken ct) =>
        BulkWorkItemExecutor.ExecuteAsync(
            command.Request.WorkItemIds,
            (id, correlationId, token) => moveWorkItemHandler.HandleAsync(
                new MoveWorkItemCommand(
                    id,
                    new MoveWorkItemRequest(command.Request.Status),
                    correlationId),
                token),
            command.CorrelationId,
            ct);
}
