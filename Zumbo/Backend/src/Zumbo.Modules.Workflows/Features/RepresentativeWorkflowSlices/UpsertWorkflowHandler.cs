using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class UpsertWorkflowHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(CreateWorkflowRequest request, string correlationId, CancellationToken ct) =>
        service.UpsertAsync(request, correlationId, ct);
}
