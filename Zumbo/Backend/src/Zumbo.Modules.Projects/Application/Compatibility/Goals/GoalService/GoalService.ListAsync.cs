using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var visible = new List<GoalDocument>();
        string? cursor = null;
        do
        {
            var batch = await goals.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (includeArchived || !item.Archived),
                cursor,
                100,
                ct);
            visible.AddRange(batch.Items.Where(item => CanView(item, actor.UserId)));
            cursor = batch.NextCursor;
        } while (cursor is not null);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        return new GoalPageResponse(
            ordered.Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count);
    }
}
