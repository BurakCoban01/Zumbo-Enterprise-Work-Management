using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;

internal sealed class SnapshotSourceLoader(
    IDocumentRepository<WorkItemDocument> workItems,
    ICapacityPlanningDirectory directory)
{
    private const int MaximumSourceItems = 10_000;
    private const int SourcePageSize = 500;

    public async Task<SnapshotSource> LoadAsync(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor,
        CancellationToken ct)
    {
        var access = await directory.ReadProjectAccessAsync(
            actor.OrganizationId,
            actor.UserId,
            plan.ProjectIds,
            ct);
        var visible = access.Where(item => item.Available).ToList();
        if (plan.OwnerUserId != actor.UserId && visible.Count == 0)
        {
            throw CapacityPlanAccessPolicy.PlanNotFound();
        }

        var loaded = new List<WorkItemDocument>();
        long sourceCount = 0;
        foreach (var project in visible)
        {
            sourceCount += await workItems.CountByFilterAsync(
                item => item.ProjectId == project.Id
                    && !item.Archived
                    && item.CompletedAt == null
                    && item.Status != "Done",
                ct);
        }

        var truncated = sourceCount > MaximumSourceItems;
        foreach (var project in visible)
        {
            string? cursor = null;
            do
            {
                var remaining = MaximumSourceItems - loaded.Count;
                if (remaining <= 0)
                {
                    break;
                }

                var result = await workItems.ListByCursorAsync(
                    item => item.ProjectId == project.Id
                        && !item.Archived
                        && item.CompletedAt == null
                        && item.Status != "Done",
                    cursor,
                    Math.Min(SourcePageSize, remaining),
                    ct);
                loaded.AddRange(result.Items);
                cursor = result.NextCursor;
            } while (cursor is not null);

            if (loaded.Count == MaximumSourceItems)
            {
                break;
            }
        }

        return new SnapshotSource(
            visible,
            access.Where(item => !item.Available).Select(item => item.Id).ToList(),
            loaded,
            truncated);
    }
}
