using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class WorkItemTemplateReadAccess(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    internal async Task<ProjectResourceAuthorization> AuthorizeProjectAsync(
        string projectId,
        string permission,
        CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        return await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

    internal async Task<WorkItemTemplateDocument> GetTemplateAsync(
        string templateId,
        bool includeArchived,
        CancellationToken ct) =>
        await templates.SelectAsync(
            template => template.Id == templateId
                && (includeArchived || !template.Archived),
            ct)
        ?? throw new NotFoundException(
            "WORK_ITEM_TEMPLATE_NOT_FOUND",
            "Work item template was not found.");

    internal static void EnsureOwnership(
        WorkItemTemplateDocument template,
        string organizationId,
        string projectId)
    {
        if (template.OrganizationId != organizationId || template.ProjectId != projectId)
        {
            throw new NotFoundException(
                "WORK_ITEM_TEMPLATE_NOT_FOUND",
                "Work item template was not found.");
        }
    }
}
