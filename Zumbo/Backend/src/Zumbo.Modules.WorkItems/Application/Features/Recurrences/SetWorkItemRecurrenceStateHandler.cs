using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class SetWorkItemRecurrenceStateHandler(WorkItemTemplateRecurrenceService service)
{
    private SetWorkItemRecurrenceStateSlice? slice;

    public SetWorkItemRecurrenceStateHandler(
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        IWorkItemAuditPublisher audit,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new SetWorkItemRecurrenceStateSlice(
            recurrences,
            occurrences,
            permissionChecker,
            currentUser,
            distributedLocks,
            lockOptions,
            clock,
            audit,
            expectedVersions);
    }

    public Task<WorkItemRecurrenceResponse> HandleAsync(
        SetWorkItemRecurrenceStateCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SetRecurrenceStateAsync(
            command.RecurrenceId,
            command.Active,
            command.CorrelationId,
            ct);
}
