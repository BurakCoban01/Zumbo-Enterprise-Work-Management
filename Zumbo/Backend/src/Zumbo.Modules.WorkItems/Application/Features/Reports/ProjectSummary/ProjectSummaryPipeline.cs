using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class ProjectSummaryPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);

    internal async Task<WorkItemReportSnapshot<ProjectSummaryResponse>> GetAsync(
        ProjectSummaryQuery query,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(query.ProjectId, PermissionCatalog.WorkItemView, ct);
        return await readModelCache.GetOrCreateSnapshotAsync(
            query.ProjectId,
            "project-summary",
            TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300)),
            async token =>
            {
                var now = clock.UtcNow;
                return new ProjectSummaryResponse(
                    checked((int)await workItems.CountByFilterAsync(
                        item => item.ProjectId == query.ProjectId && !item.Archived,
                        token)),
                    checked((int)await workItems.CountByFilterAsync(
                        item => item.ProjectId == query.ProjectId
                            && !item.Archived
                            && item.CompletedAt != null,
                        token)),
                    checked((int)await workItems.CountByFilterAsync(
                        item => item.ProjectId == query.ProjectId
                            && !item.Archived
                            && item.CompletedAt == null
                            && (item.Status == "In Progress"
                                || item.Status == "Code Review"
                                || item.Status == "Test"),
                        token)),
                    checked((int)await workItems.CountByFilterAsync(
                        item => item.ProjectId == query.ProjectId
                            && !item.Archived
                            && item.DueDate < now
                            && item.CompletedAt == null,
                        token)));
            },
            ct);
    }

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var authorization = await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
        authorizedOrganizationIds[projectId] = authorization.OrganizationId;
        return authorization;
    }
}
