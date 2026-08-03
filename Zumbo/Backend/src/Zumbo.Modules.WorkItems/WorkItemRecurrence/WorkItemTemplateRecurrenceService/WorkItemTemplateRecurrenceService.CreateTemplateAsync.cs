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

    public async Task<WorkItemTemplateResponse> CreateTemplateAsync(
        CreateWorkItemTemplateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, PermissionCatalog.WorkItemCreate, ct);
        var normalized = await NormalizeTemplateAsync(
            request.ProjectId,
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
        await using var templateLock = await AcquireAsync("work-item-template-project:" + request.ProjectId, ct);
        await EnsureTemplateNameAvailableAsync(request.ProjectId, normalized.Name, null, ct);
        var now = clock.UtcNow;
        var template = new WorkItemTemplateDocument
        {
            OrganizationId = authorization.OrganizationId,
            ProjectId = request.ProjectId,
            BoardId = normalized.BoardId,
            Name = normalized.Name,
            Title = normalized.Title,
            Description = normalized.Description,
            Type = normalized.Type,
            IssueTypeSchemaVersion = normalized.SchemaVersion,
            CustomFields = normalized.CustomFields,
            Priority = normalized.Priority,
            AssigneeUserId = normalized.AssigneeUserId,
            TeamId = normalized.TeamId,
            DueAfterDays = normalized.DueAfterDays,
            Labels = normalized.Labels,
            CreatedByUserId = RequireCurrentUser(),
            CreatedAt = now,
            UpdatedAt = now
        };
        try
        {
            template = await templates.CreateAsync(template, ct);
        }
        catch (DocumentConflictException)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_EXISTS", "An active template with this name already exists in the project.");
        }

        await audit.WriteAsync(
            "WorkItemTemplateCreated", "WorkItemTemplate", template.Id, null, template.Name, correlationId, ct);
        return ToResponse(template);
    }
}
