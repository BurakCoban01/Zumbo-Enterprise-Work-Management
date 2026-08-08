using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Archive;

public sealed record BulkArchiveWorkItemsCommand(
    BulkArchiveWorkItemsRequest Request,
    string CorrelationId);
