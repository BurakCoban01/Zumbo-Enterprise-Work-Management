using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class ArchiveWorkItemTemplateHandler(WorkItemTemplateRecurrenceService service)
{
    private ArchiveWorkItemTemplateSlice? slice;

    public ArchiveWorkItemTemplateHandler(
        IDocumentRepository<WorkItemTemplateDocument> templates,
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        IWorkItemAuditPublisher audit,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new ArchiveWorkItemTemplateSlice(
            templates,
            recurrences,
            permissionChecker,
            currentUser,
            distributedLocks,
            lockOptions,
            clock,
            audit,
            expectedVersions);
    }

    public Task HandleAsync(
        ArchiveWorkItemTemplateCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ArchiveTemplateAsync(command.TemplateId, command.CorrelationId, ct);
}
