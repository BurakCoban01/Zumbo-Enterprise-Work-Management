using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Move;

public sealed record BulkMoveWorkItemsCommand(
    BulkMoveWorkItemsRequest Request,
    string CorrelationId);
