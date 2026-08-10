using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class ListGoalsSlice(
    GoalReadAccess access,
    IDocumentRepository<GoalDocument> goals)
{
    internal async Task<GoalPageResponse> HandleAsync(
        ListGoalsQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var visible = new List<GoalDocument>();
        string? cursor = null;
        do
        {
            var batch = await goals.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (query.IncludeArchived || !item.Archived),
                cursor,
                100,
                ct);
            visible.AddRange(batch.Items.Where(item =>
                GoalReadAccess.CanView(item, actor.UserId)));
            cursor = batch.NextCursor;
        } while (cursor is not null);

        var normalizedPage = Math.Max(query.Page, 1);
        var normalizedPageSize = Math.Clamp(query.PageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        return new GoalPageResponse(
            ordered.Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => GoalResponseMapper.ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count);
    }
}
