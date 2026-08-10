using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class CreateWorkItemTemplateSlice(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemCollaboratorDirectory collaboratorDirectory,
    IBoardPlacementPolicy boardPlacementPolicy,
    IWorkItemTypeSchemaPolicy typeSchemas,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock,
    IWorkItemAuditPublisher audit)
{
    private readonly WorkItemTemplateReadAccess readAccess =
        new(templates, permissionChecker, currentUser);
    private readonly WorkItemTemplateMutationAccess mutationAccess =
        new(templates, distributedLocks, lockOptions);
    private readonly WorkItemTemplateNormalizationPolicy normalization = new(
        teamPolicy,
        collaboratorDirectory,
        boardPlacementPolicy,
        typeSchemas,
        permissionChecker,
        currentUser);

    internal async Task<WorkItemTemplateResponse> HandleAsync(
        CreateWorkItemTemplateCommand command,
        CancellationToken ct)
    {
        var request = command.Request;
        var authorization = await readAccess.AuthorizeProjectAsync(
            request.ProjectId,
            PermissionCatalog.WorkItemCreate,
            ct);
        var normalized = await normalization.NormalizeAsync(
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
        await using var templateLock = await mutationAccess.AcquireAsync(
            "work-item-template-project:" + request.ProjectId,
            ct);
        await mutationAccess.EnsureNameAvailableAsync(
            request.ProjectId,
            normalized.Name,
            ignoredTemplateId: null,
            ct);
        var now = clock.UtcNow;
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
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
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        try
        {
            template = await templates.CreateAsync(template, ct);
        }
        catch (DocumentConflictException)
        {
            throw new ConflictException(
                "WORK_ITEM_TEMPLATE_EXISTS",
                "An active template with this name already exists in the project.");
        }

        await audit.WriteAsync(
            "WorkItemTemplateCreated",
            "WorkItemTemplate",
            template.Id,
            null,
            template.Name,
            command.CorrelationId,
            ct);
        return WorkItemTemplateResponseMapper.ToResponse(template);
    }
}
