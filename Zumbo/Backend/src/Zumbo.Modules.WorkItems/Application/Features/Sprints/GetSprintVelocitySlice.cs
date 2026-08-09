using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class GetSprintVelocitySlice(
    IDocumentRepository<SprintDocument> sprints,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
{
    private TimeSpan ReadModelTtl => TimeSpan.FromSeconds(
        Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 1, 300));

    internal async Task<WorkItemReportSnapshot<IReadOnlyList<SprintVelocityResponse>>> HandleAsync(
        GetSprintVelocityQuery query,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        _ = await permissionChecker.EnsureCanAsync(
            userId,
            query.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        var normalizedCount = Math.Clamp(query.SprintCount, 1, 12);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<SprintVelocityResponse>>(
            query.ProjectId,
            $"sprint-velocity:{normalizedCount}",
            ReadModelTtl,
            async token =>
            {
                var completed = await sprints.ListByFilterAsync(
                    sprint => sprint.ProjectId == query.ProjectId && sprint.Status == SprintStatuses.Completed,
                    sprint => sprint.CompletedAt!,
                    orderDescending: true,
                    pageSize: normalizedCount,
                    cancellationToken: token);
                return completed.Select(sprint => new SprintVelocityResponse(
                    sprint.Id,
                    sprint.CompletedItems,
                    sprint.CompletedPoints)).ToList();
            },
            ct);
    }
}
