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

    private async Task EnsureTemplateNameAvailableAsync(
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
            throw new ConflictException("WORK_ITEM_TEMPLATE_EXISTS", "An active template with this name already exists in the project.");
        }
    }
}
