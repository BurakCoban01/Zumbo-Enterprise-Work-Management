using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Assign;

public sealed record BulkAssignWorkItemsCommand(
    BulkAssignWorkItemsRequest Request,
    string CorrelationId);
