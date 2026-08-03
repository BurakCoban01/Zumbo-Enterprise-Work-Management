using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class KnowledgeDirectoryAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<PortfolioDocument> portfolios,
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<UserDocument> users,
    ICurrentUser currentUser) : IKnowledgeDirectory
{
    public async Task<KnowledgeScopeAccess> AuthorizeScopeAsync(
        string scopeType,
        string scopeId,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required.");
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var systemAdmin = PermissionCatalog.IsSystemAdministrator(currentUser.Roles);

        if (scopeType == KnowledgeScopeTypes.Project)
        {
            var project = await projects.SelectAsync(
                item => item.Id == scopeId
                    && !item.Archived,
                ct)
                ?? throw ScopeNotFound();
            if (!systemAdmin
                && (!string.Equals(
                        project.OrganizationId,
                        organizationId,
                        StringComparison.Ordinal)
                    || !ProjectVisibilityAccess.CanView(
                        project.Visibility,
                        project.Members.Select(member => member.UserId),
                        userId)))
            {
                throw ScopeNotFound();
            }

            var role = project.Members
                .SingleOrDefault(member => member.UserId == userId)
                ?.Role;
            return new KnowledgeScopeAccess(
                project.OrganizationId,
                project.Name,
                [project.Id],
                systemAdmin || role is ProjectRoles.Owner or ProjectRoles.Admin,
                systemAdmin
                    || role is not null
                    && PermissionCatalog.HasProjectPermission(
                        role,
                        PermissionCatalog.CommentCreate));
        }

        if (scopeType == KnowledgeScopeTypes.Initiative)
        {
            var portfolio = await portfolios.SelectAsync(
                item => item.OrganizationId == organizationId
                    && !item.Archived
                    && item.Initiatives.Any(initiative => initiative.Id == scopeId),
                ct)
                ?? throw ScopeNotFound();
            var initiative = portfolio.Initiatives.Single(
                item => item.Id == scopeId);
            var visible = systemAdmin
                || portfolio.OwnerUserId == userId
                || portfolio.ViewerUserIds.Contains(userId, StringComparer.Ordinal)
                || initiative.OwnerUserId == userId;
            if (!visible)
                throw ScopeNotFound();

            var canManage = systemAdmin
                || portfolio.OwnerUserId == userId
                || initiative.OwnerUserId == userId;
            return new KnowledgeScopeAccess(
                portfolio.OrganizationId,
                $"{portfolio.Name} / {initiative.Name}",
                initiative.ProjectIds,
                canManage,
                visible);
        }

        throw new ValidationException("Knowledge scope type is not supported.");
    }

    public async Task EnsureLinksAsync(
        string organizationId,
        IReadOnlyCollection<string> scopeProjectIds,
        IReadOnlyCollection<string> workItemIds,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct)
    {
        foreach (var userId in userIds)
        {
            if (!await users.ExistsByFilterAsync(
                    user => user.Id == userId
                        && user.OrganizationId == organizationId
                        && user.IsActive,
                    ct))
            {
                throw new ValidationException(
                    "Knowledge user links must reference active users in the current organization.");
            }
        }

        foreach (var workItemId in workItemIds)
        {
            if (!await workItems.ExistsByFilterAsync(
                    item => item.Id == workItemId
                        && !item.Archived
                        && scopeProjectIds.Contains(item.ProjectId),
                    ct))
            {
                throw new ValidationException(
                    "Knowledge work-item links must belong to the selected project or initiative.");
            }
        }
    }

    public async Task<KnowledgeLinkOptionsResponse> ReadLinkOptionsAsync(
        string organizationId,
        IReadOnlyCollection<string> scopeProjectIds,
        string? query,
        CancellationToken ct)
    {
        var normalized = query?.Trim().ToLowerInvariant();
        var workItemPage = await workItems.ListByCursorAsync(
            item => !item.Archived
                && scopeProjectIds.Contains(item.ProjectId)
                && (string.IsNullOrEmpty(normalized)
                    || item.Title.ToLower().Contains(normalized)),
            pageSize: 100,
            cancellationToken: ct);
        var userFilter = await users.ListByFilterAsync(
            item => item.OrganizationId == organizationId
                && item.IsActive
                && (string.IsNullOrEmpty(normalized)
                    || item.Username.ToLower().Contains(normalized)
                    || item.Email.ToLower().Contains(normalized)),
            item => item.Username,
            pageSize: 100,
            cancellationToken: ct);
        var userCount = await users.CountByFilterAsync(
            item => item.OrganizationId == organizationId
                && item.IsActive
                && (string.IsNullOrEmpty(normalized)
                    || item.Username.ToLower().Contains(normalized)
                    || item.Email.ToLower().Contains(normalized)),
            ct);
        return new KnowledgeLinkOptionsResponse(
            workItemPage.Items.Select(item =>
                new KnowledgeLinkOptionResponse(
                    item.Id,
                    item.Title,
                    item.ProjectId)).ToList(),
            userFilter.Select(item =>
                new KnowledgeLinkOptionResponse(
                    item.Id,
                    item.Username,
                    item.Email)).ToList(),
            workItemPage.NextCursor is null && userCount <= 100
                ? KnowledgeSourceStatuses.Ready
                : KnowledgeSourceStatuses.Partial);
    }

    private static NotFoundException ScopeNotFound() =>
        new("KNOWLEDGE_SCOPE_NOT_FOUND", "Knowledge scope was not found.");
}
