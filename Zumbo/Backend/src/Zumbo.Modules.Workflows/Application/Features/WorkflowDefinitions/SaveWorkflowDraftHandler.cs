using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class SaveWorkflowDraftHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(CreateWorkflowRequest request, string correlationId, CancellationToken ct) =>
        service.SaveDraftAsync(request, correlationId, ct);
}
