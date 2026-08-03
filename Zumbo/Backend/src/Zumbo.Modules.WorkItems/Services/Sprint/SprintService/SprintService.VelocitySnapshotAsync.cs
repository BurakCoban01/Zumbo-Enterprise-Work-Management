using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<WorkItemReportSnapshot<IReadOnlyList<SprintVelocityResponse>>> VelocitySnapshotAsync(
        string projectId,
        int sprintCount,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var normalizedCount = Math.Clamp(sprintCount, 1, 12);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<SprintVelocityResponse>>(
            projectId,
            $"sprint-velocity:{normalizedCount}",
            ReadModelTtl,
            async token =>
            {
                var completed = await sprints.ListByFilterAsync(
                    sprint => sprint.ProjectId == projectId && sprint.Status == SprintStatuses.Completed,
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
