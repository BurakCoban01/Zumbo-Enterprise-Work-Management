using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class UpsertWorkItemTypeSchemaHandler(WorkItemTypeSchemaService service)
{
    private UpsertWorkItemTypeSchemaSlice? slice;

    public UpsertWorkItemTypeSchemaHandler(
        IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        IWorkItemAuditPublisher audit,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IOptions<WorkItemTypeSchemaOptions> configuredOptions,
        IClock clock,
        ICurrentUser currentUser,
        IExpectedVersionAccessor? expectedVersions)
        : this(null!)
    {
        slice = new UpsertWorkItemTypeSchemaSlice(
            schemas,
            workItems,
            permissionChecker,
            audit,
            distributedLocks,
            lockOptions,
            configuredOptions,
            clock,
            currentUser,
            expectedVersions);
    }

    public Task<WorkItemTypeSchemaResponse> HandleAsync(
        UpsertWorkItemTypeSchemaCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.UpsertAsync(command.ProjectId, command.Request, command.CorrelationId, ct);
}
