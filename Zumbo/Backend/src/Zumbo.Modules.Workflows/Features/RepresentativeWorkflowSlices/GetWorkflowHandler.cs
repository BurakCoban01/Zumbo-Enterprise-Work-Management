using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class GetWorkflowHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(GetWorkflowQuery query, CancellationToken ct)
    {
        GetWorkflowValidator.Validate(query);
        return service.GetOrCreateDefaultAsync(query.ProjectId, ct);
    }
}
