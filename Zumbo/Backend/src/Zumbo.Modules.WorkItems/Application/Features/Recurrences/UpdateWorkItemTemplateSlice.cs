using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class UpdateWorkItemTemplateSlice(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemCollaboratorDirectory collaboratorDirectory,
    IBoardPlacementPolicy boardPlacementPolicy,
    IWorkItemTypeSchemaPolicy typeSchemas,
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
    private readonly WorkItemTemplateNormalizationPolicy normalization = new(
        teamPolicy,
        collaboratorDirectory,
        boardPlacementPolicy,
        typeSchemas,
        permissionChecker,
        currentUser);

    internal async Task<WorkItemTemplateResponse> HandleAsync(
        UpdateWorkItemTemplateCommand command,
        CancellationToken ct)
    {
        await using var templateLock = await access.AcquireAsync(command.TemplateId, ct);
        var template = await access.GetForUpdateAsync(command.TemplateId, ct);
        var request = command.Request;
        var normalized = await normalization.NormalizeAsync(
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
        await access.EnsureNameAvailableAsync(template, normalized.Name, ct);
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
        await access.ReplaceAsync(template, ct);
        await audit.WriteAsync(
            "WorkItemTemplateUpdated",
            "WorkItemTemplate",
            template.Id,
            oldName,
            template.Name,
            command.CorrelationId,
            ct);
        return WorkItemTemplateResponseMapper.ToResponse(template);
    }
}
