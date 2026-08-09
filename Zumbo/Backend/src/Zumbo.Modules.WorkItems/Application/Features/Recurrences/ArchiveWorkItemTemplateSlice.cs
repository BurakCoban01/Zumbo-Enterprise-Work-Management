using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class ArchiveWorkItemTemplateSlice(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock,
    IWorkItemAuditPublisher audit,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly WorkItemTemplateUpdateAccess access = new(
        templates,
        recurrences,
        permissionChecker,
        currentUser,
        distributedLocks,
        lockOptions,
        expectedVersions);

    internal async Task HandleAsync(
        ArchiveWorkItemTemplateCommand command,
        CancellationToken ct)
    {
        await using var templateLock = await access.AcquireAsync(command.TemplateId, ct);
        var template = await access.GetForUpdateAsync(command.TemplateId, ct);
        await access.EnsureNoActiveRecurrencesAsync(template.Id, ct);
        template.Archived = true;
        template.UpdatedAt = clock.UtcNow;
        await access.ReplaceAsync(template, ct);
        await audit.WriteAsync(
            "WorkItemTemplateArchived",
            "WorkItemTemplate",
            template.Id,
            template.Name,
            null,
            command.CorrelationId,
            ct);
    }
}
