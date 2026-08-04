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

    public async Task<WorkItemTemplateResponse> UpdateTemplateAsync(
        string templateId,
        UpdateWorkItemTemplateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var templateLock = await AcquireAsync("work-item-template:" + templateId, ct);
        var template = await GetTemplateAsync(templateId, includeArchived: false, ct);
        await EnsurePermissionAsync(template.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        var normalized = await NormalizeTemplateAsync(
            template.ProjectId,
            request.BoardId,
            request.Name,
            request.Title,
            request.Description,
            request.Type,
            request.Priority,
            request.AssigneeUserId,
            request.TeamId,
            request.DueAfterDays,
            request.Labels,
            request.CustomFields,
            ct);
        await EnsureTemplateNameAvailableAsync(template.ProjectId, normalized.Name, template.Id, ct);
        var oldName = template.Name;
        template.BoardId = normalized.BoardId;
        template.Name = normalized.Name;
        template.Title = normalized.Title;
        template.Description = normalized.Description;
        template.Type = normalized.Type;
        template.IssueTypeSchemaVersion = normalized.SchemaVersion;
        template.CustomFields = normalized.CustomFields;
        template.Priority = normalized.Priority;
        template.AssigneeUserId = normalized.AssigneeUserId;
        template.TeamId = normalized.TeamId;
        template.DueAfterDays = normalized.DueAfterDays;
        template.Labels = normalized.Labels;
        template.UpdatedAt = clock.UtcNow;
        var expected = expectedVersion.Consume(template.Version);
        var result = await templates.ReplaceByVersionAsync(x => x.Id == template.Id, template, expected, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_CONFLICT", "The template changed concurrently; reload and retry.");
        }
        template.Version = result.Version!.Value;
        await audit.WriteAsync(
            "WorkItemTemplateUpdated", "WorkItemTemplate", template.Id, oldName, template.Name, correlationId, ct);
        return ToResponse(template);
    }
}
