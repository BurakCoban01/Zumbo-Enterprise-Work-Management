using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class GetWorkflowHandler(WorkflowService service)
{
    private GetWorkflowSlice? slice;

    public GetWorkflowHandler(
        IDocumentRepository<WorkflowDefinitionDocument> workflows,
        IWorkflowProjectAccessChecker accessChecker,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions)
        : this(null!)
    {
        slice = new GetWorkflowSlice(
            workflows,
            accessChecker,
            distributedLockProvider,
            distributedLockOptions,
            clock,
            expectedVersions);
    }

    public Task<WorkflowResponse> HandleAsync(GetWorkflowQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetOrCreateDefaultAsync(query.ProjectId, ct);
}
