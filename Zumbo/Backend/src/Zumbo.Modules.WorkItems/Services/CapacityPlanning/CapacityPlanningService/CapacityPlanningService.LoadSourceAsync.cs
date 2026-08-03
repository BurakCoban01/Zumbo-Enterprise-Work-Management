using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private async Task<CapacitySource> LoadSourceAsync(
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
            throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
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
            if (loaded.Count == MaximumSourceItems) break;
        }
        return new CapacitySource(
            visible,
            access.Where(item => !item.Available).Select(item => item.Id).ToList(),
            loaded,
            truncated);
    }
}
