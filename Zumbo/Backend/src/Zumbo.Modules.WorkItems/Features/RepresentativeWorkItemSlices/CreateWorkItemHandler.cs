using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class CreateWorkItemHandler(WorkItemService service)
{
    public Task<WorkItemResponse> HandleAsync(CreateWorkItemRequest request, string correlationId, CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}
