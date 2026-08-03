using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    public async Task ArchiveTemplateAsync(string templateId, string correlationId, CancellationToken ct)
    {
        await using var templateLock = await AcquireAsync("work-item-template:" + templateId, ct);
        var template = await GetTemplateAsync(templateId, includeArchived: false, ct);
        await EnsurePermissionAsync(template.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        if (await recurrences.ExistsByFilterAsync(
                recurrence => recurrence.TemplateId == template.Id && !recurrence.Archived && recurrence.Active,
                ct))
        {
            throw new ConflictException(
                "WORK_ITEM_TEMPLATE_RECURRENCE_ACTIVE",
                "Pause or archive active recurrences before archiving this template.");
        }

        template.Archived = true;
        template.UpdatedAt = clock.UtcNow;
        var result = await templates.ReplaceByVersionAsync(
            x => x.Id == template.Id,
            template,
            expectedVersion.Consume(template.Version),
            ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_CONFLICT", "The template changed concurrently; reload and retry.");
        }
        await audit.WriteAsync(
            "WorkItemTemplateArchived", "WorkItemTemplate", template.Id, template.Name, null, correlationId, ct);
    }
}
