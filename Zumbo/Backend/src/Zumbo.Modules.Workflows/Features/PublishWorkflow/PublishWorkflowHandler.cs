using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class PublishWorkflowHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(string projectId, string correlationId, CancellationToken ct) =>
        service.PublishAsync(projectId, correlationId, ct);
}
