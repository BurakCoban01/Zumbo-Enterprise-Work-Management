using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class WorkItemTemplateUpdateAccess(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly WorkItemTemplateReadAccess readAccess =
        new(templates, permissionChecker, currentUser);
    private readonly WorkItemTemplateMutationAccess mutationAccess =
        new(templates, distributedLocks, lockOptions);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal Task<IAsyncDisposable> AcquireAsync(string templateId, CancellationToken ct) =>
        mutationAccess.AcquireAsync("work-item-template:" + templateId, ct);

    internal async Task<WorkItemTemplateDocument> GetForUpdateAsync(
        string templateId,
        CancellationToken ct)
    {
        var template = await readAccess.GetTemplateAsync(templateId, includeArchived: false, ct);
        _ = await readAccess.AuthorizeProjectAsync(
            template.ProjectId,
            PermissionCatalog.WorkItemUpdate,
            ct);
        return template;
    }

    internal Task EnsureNameAvailableAsync(
        WorkItemTemplateDocument template,
        string name,
        CancellationToken ct) =>
        mutationAccess.EnsureNameAvailableAsync(template.ProjectId, name, template.Id, ct);

    internal async Task EnsureNoActiveRecurrencesAsync(
        string templateId,
        CancellationToken ct)
    {
        if (await recurrences.ExistsByFilterAsync(
                recurrence => recurrence.TemplateId == templateId
                    && !recurrence.Archived
                    && recurrence.Active,
                ct))
        {
            throw new ConflictException(
                "WORK_ITEM_TEMPLATE_RECURRENCE_ACTIVE",
                "Pause or archive active recurrences before archiving this template.");
        }
    }

    internal async Task ReplaceAsync(
        WorkItemTemplateDocument template,
        CancellationToken ct)
    {
        var result = await templates.ReplaceByVersionAsync(
            item => item.Id == template.Id,
            template,
            expectedVersion.Consume(template.Version),
            ct);
        if (!result.Found)
        {
            throw new ConflictException(
                "WORK_ITEM_TEMPLATE_CONFLICT",
                "The template changed concurrently; reload and retry.");
        }
        template.Version = result.Version!.Value;
    }
}
