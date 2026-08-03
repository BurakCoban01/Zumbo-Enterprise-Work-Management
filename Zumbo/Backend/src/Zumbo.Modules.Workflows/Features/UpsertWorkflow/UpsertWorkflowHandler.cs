using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class UpsertWorkflowHandler(WorkflowService service)
{
    private UpsertWorkflowSlice? slice;

    public UpsertWorkflowHandler(
        IDocumentRepository<WorkflowDefinitionDocument> workflows,
        IWorkflowProjectAccessChecker accessChecker,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IClock clock,
        IWorkflowAuditWriter audit,
        IExpectedVersionAccessor? expectedVersions,
        IWorkflowPublicationGuard? publicationGuard)
        : this(null!)
    {
        slice = new UpsertWorkflowSlice(
            workflows,
            accessChecker,
            distributedLockProvider,
            distributedLockOptions,
            clock,
            audit,
            expectedVersions,
            publicationGuard);
    }

    public Task<WorkflowResponse> HandleAsync(CreateWorkflowRequest request, string correlationId, CancellationToken ct) =>
        slice?.HandleAsync(request, correlationId, ct)
        ?? service.UpsertAsync(request, correlationId, ct);
}
