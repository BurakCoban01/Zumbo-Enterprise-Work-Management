using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

public sealed class GoalDirectoryAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<PortfolioDocument> portfolios,
    IDocumentRepository<UserDocument> users,
    ICurrentUser currentUser) : IGoalDirectory
{
    public async Task EnsureOrganizationUsersAsync(
        string organizationId,
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
                    "Goal users must be active users in the goal organization.");
            }
        }
    }

    public async Task EnsureSourcesReadableAsync(
        string organizationId,
        IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct)
    {
        var result = await ReadSourcesAsync(
            organizationId,
            initiativeLinks,
            projectIds,
            ct);
        if (result.UnavailableSources.Count > 0)
        {
            throw new ValidationException(
                "Goal links must reference readable initiatives and projects in the active organization.");
        }
    }

    public async Task<GoalSourceResult> ReadSourcesAsync(
        string organizationId,
        IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct)
    {
        var initiativeSources = new List<GoalInitiativeSource>();
        var projectSources = new List<GoalProjectSource>();
        var unavailable = new List<string>();

        foreach (var link in initiativeLinks)
        {
            var portfolio = await portfolios.SelectAsync(
                item => item.Id == link.PortfolioId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct);
            var initiative = portfolio?.Initiatives.SingleOrDefault(
                item => item.Id == link.InitiativeId);
            if (portfolio is null || initiative is null || !CanView(portfolio))
            {
                unavailable.Add($"initiative:{link.PortfolioId}:{link.InitiativeId}");
                continue;
            }
            initiativeSources.Add(new GoalInitiativeSource(
                portfolio.Id,
                initiative.Id,
                initiative.Name,
                initiative.Status,
                initiative.Health,
                initiative.Confidence));
        }

        foreach (var projectId in projectIds)
        {
            var project = await projects.SelectAsync(
                item => item.Id == projectId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct);
            if (project is null || !CanView(project))
            {
                unavailable.Add($"project:{projectId}");
                continue;
            }
            projectSources.Add(new GoalProjectSource(project.Id, project.Key, project.Name));
        }
        return new GoalSourceResult(initiativeSources, projectSources, unavailable);
    }

    private bool CanView(PortfolioDocument portfolio)
    {
        if (PermissionCatalog.IsSystemAdministrator(currentUser.Roles))
            return true;
        var userId = currentUser.UserId;
        return userId is not null
            && string.Equals(
                currentUser.OrganizationId,
                portfolio.OrganizationId,
                StringComparison.Ordinal)
            && (portfolio.OwnerUserId == userId
                || portfolio.ViewerUserIds.Contains(userId, StringComparer.Ordinal));
    }

    private bool CanView(ProjectDocument project)
    {
        if (PermissionCatalog.IsSystemAdministrator(currentUser.Roles))
            return true;
        var userId = currentUser.UserId;
        return userId is not null
            && string.Equals(
                currentUser.OrganizationId,
                project.OrganizationId,
                StringComparison.Ordinal)
            && ProjectVisibilityAccess.CanView(
                project.Visibility,
                project.Members.Select(member => member.UserId),
                userId);
    }
}

public sealed class GoalAuditWriterAdapter(AuditService audit) : IGoalAuditWriter
{
    public Task WriteAsync(
        string action,
        string goalId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "Goal",
            goalId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
