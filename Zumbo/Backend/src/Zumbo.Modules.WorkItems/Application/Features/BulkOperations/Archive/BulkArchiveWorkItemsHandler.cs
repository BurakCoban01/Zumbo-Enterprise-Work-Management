using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Archive;

public sealed class BulkArchiveWorkItemsHandler(ArchiveWorkItemHandler archiveWorkItemHandler)
{
    public Task<BulkWorkItemResponse> HandleAsync(
        BulkArchiveWorkItemsCommand command,
        CancellationToken ct) =>
        BulkWorkItemExecutor.ExecuteAsync(
            command.Request.WorkItemIds,
            async (id, correlationId, token) =>
            {
                await archiveWorkItemHandler.HandleAsync(
                    new ArchiveWorkItemCommand(id, correlationId),
                    token);
                return true;
            },
            command.CorrelationId,
            ct);
}
