using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class WorkItemTemplateMutationAccess(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions)
{
    internal async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
        ?? throw new ConflictException(
            "RESOURCE_BUSY",
            "The requested resource is busy; retry the operation.");
    }

    internal async Task EnsureNameAvailableAsync(
        string projectId,
        string name,
        string? ignoredTemplateId,
        CancellationToken ct)
    {
        var normalized = name.ToLowerInvariant();
        if (await templates.ExistsByFilterAsync(
                template => template.ProjectId == projectId
                    && template.Id != ignoredTemplateId
                    && !template.Archived
                    && template.Name.ToLower() == normalized,
                ct))
        {
            throw new ConflictException(
                "WORK_ITEM_TEMPLATE_EXISTS",
                "An active template with this name already exists in the project.");
        }
    }
}
