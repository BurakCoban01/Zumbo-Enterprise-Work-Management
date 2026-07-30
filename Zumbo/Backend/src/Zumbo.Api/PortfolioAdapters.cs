using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class PortfolioDirectoryAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<UserDocument> users,
    ICurrentUser currentUser,
    IClock clock) : IPortfolioDirectory
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
                    "Portfolio users must be active users in the portfolio organization.");
            }
        }
    }

    public async Task EnsureProjectsManageableAsync(
        string organizationId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct)
    {
        foreach (var projectId in projectIds)
        {
            var project = await projects.SelectAsync(
                item => item.Id == projectId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct);
            if (project is null)
                throw new ValidationException(
                    "Portfolio projects must belong to the active organization.");
            if (PermissionCatalog.IsSystemAdministrator(currentUser.Roles))
                continue;
            var userId = currentUser.UserId
                ?? throw new UnauthorizedException("Authenticated user is required.");
            var role = project.Members.SingleOrDefault(member => member.UserId == userId)?.Role;
            if (role is not (ProjectRoles.Owner or ProjectRoles.Admin))
                throw new ForbiddenException("Portfolio projects require project owner or admin access.");
        }
    }

    public async Task EnsureMilestoneLinksAsync(
        string organizationId,
        IReadOnlyCollection<PortfolioMilestoneLinkRequest> milestoneLinks,
        CancellationToken ct)
    {
        foreach (var group in milestoneLinks.GroupBy(link => link.ProjectId, StringComparer.Ordinal))
        {
            var project = await projects.SelectAsync(
                item => item.Id == group.Key
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct);
            if (project is null
                || group.Any(link => project.Milestones.All(
                    milestone => milestone.Id != link.MilestoneId)))
            {
                throw new ValidationException(
                    "Portfolio milestone links must reference current project milestones.");
            }
        }
    }

    public async Task<PortfolioProjectSourceResult> ReadProjectSourcesAsync(
        string organizationId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct)
    {
        var sources = new List<PortfolioProjectSource>();
        var unavailable = new List<string>();
        foreach (var projectId in projectIds)
        {
            var project = await projects.SelectAsync(
                item => item.Id == projectId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct);
            if (project is null || !CanView(project))
            {
                unavailable.Add(projectId);
                continue;
            }
            var scopedItems = await LoadWorkItemsAsync(project.Id, ct);
            var completed = scopedItems.Count(item =>
                item.CompletedAt is not null
                || item.Status is "Done" or "Closed" or "Completed");
            var overdue = scopedItems.Count(item =>
                item.CompletedAt is null
                && item.Status is not ("Done" or "Closed" or "Completed")
                && item.DueDate is not null
                && item.DueDate < clock.UtcNow);
            var latestWorkItemUpdate = scopedItems.Count == 0
                ? project.UpdatedAt
                : scopedItems.Max(item => item.UpdatedAt);
            var updatedAt = latestWorkItemUpdate > project.UpdatedAt
                ? latestWorkItemUpdate
                : project.UpdatedAt;
            sources.Add(new PortfolioProjectSource(
                project.Id,
                project.Key,
                project.Name,
                scopedItems.Count,
                completed,
                overdue,
                project.Milestones.Select(milestone =>
                    new PortfolioProjectMilestoneSource(
                        milestone.Id,
                        milestone.Name,
                        milestone.DueAt,
                        milestone.Status,
                        milestone.CompletedAt)).ToList(),
                updatedAt));
        }
        return new PortfolioProjectSourceResult(sources, unavailable);
    }

    private bool CanView(ProjectDocument project)
    {
        if (PermissionCatalog.IsSystemAdministrator(currentUser.Roles))
            return true;
        if (!string.Equals(
                currentUser.OrganizationId,
                project.OrganizationId,
                StringComparison.Ordinal))
        {
            return false;
        }
        var userId = currentUser.UserId;
        return userId is not null
            && (project.Visibility == ProjectVisibilities.Internal
                || project.Members.Any(member => member.UserId == userId));
    }

    private async Task<List<WorkItemDocument>> LoadWorkItemsAsync(
        string projectId,
        CancellationToken ct)
    {
        const int maximum = 10_000;
        var result = new List<WorkItemDocument>();
        string? cursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == projectId && !item.Archived,
                cursor,
                200,
                ct);
            result.AddRange(page.Items);
            if (result.Count > maximum)
            {
                throw new ConflictException(
                    "PORTFOLIO_SOURCE_LIMIT_EXCEEDED",
                    $"Portfolio project roll-up exceeds the supported limit of {maximum} work items.");
            }
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }
}

public sealed class PortfolioAuditWriterAdapter(AuditService audit) : IPortfolioAuditWriter
{
    public Task WriteAsync(
        string action,
        string portfolioId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "Portfolio",
            portfolioId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
