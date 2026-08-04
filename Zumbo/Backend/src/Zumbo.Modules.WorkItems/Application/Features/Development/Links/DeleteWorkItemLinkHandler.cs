using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Links;

public sealed class DeleteWorkItemLinkHandler(
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IProjectPermissionChecker projectPermissions,
    IWorkItemAuditPublisher audit,
    ICurrentUser currentUser)
{
    public async Task HandleAsync(
        DeleteWorkItemLinkCommand command,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(command.WorkItemId, ct);
        var link = await links.SelectAsync(
            item => item.Id == command.LinkId
                && item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.WorkItemId == workItem.Id,
            ct) ?? throw LinkNotFound();
        if (link.Version != command.ExpectedVersion)
        {
            throw LinkConflict();
        }

        var deleted = await links.DeleteByFilterAsync(
            item => item.Id == link.Id
                && item.OrganizationId == organizationId
                && item.Version == command.ExpectedVersion,
            ct);
        if (deleted != 1)
        {
            throw LinkConflict();
        }

        await audit.WriteAsync(
            "WorkItemDevelopmentLinkDeleted",
            "WorkItem",
            workItem.Id,
            $"{link.Provider}|{link.RepositoryFullName}|{link.Kind}|{link.ExternalId}",
            null,
            command.CorrelationId,
            ct);
    }

    private async Task<(WorkItemDocument WorkItem, string OrganizationId)> GetWorkItemAsync(
        string workItemId,
        CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        var workItem = await workItems.SelectAsync(
            item => item.Id == workItemId && !item.Archived,
            ct) ?? throw new NotFoundException(
                "WORK_ITEM_NOT_FOUND",
                "Work item was not found.");
        var access = await projectPermissions.EnsureCanAsync(
            userId,
            workItem.ProjectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(access.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        }

        return (workItem, access.OrganizationId);
    }

    private static NotFoundException LinkNotFound() => new(
        "DEVELOPMENT_LINK_NOT_FOUND",
        "Development link was not found.");

    private static ConflictException LinkConflict() => new(
        "DEVELOPMENT_LINK_CONFLICT",
        "Development link changed concurrently; refresh and retry.");
}
