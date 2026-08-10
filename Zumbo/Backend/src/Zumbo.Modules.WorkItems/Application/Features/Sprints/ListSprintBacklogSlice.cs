namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class ListSprintBacklogSlice(SprintReadAccess access)
{
    internal async Task<SprintBacklogPageResponse> HandleAsync(
        ListSprintBacklogQuery query,
        CancellationToken ct)
    {
        await access.EnsureViewAsync(query.ProjectId, ct);
        var page = await access.ListBacklogAsync(query.ProjectId, query.After, query.PageSize, ct);
        return new SprintBacklogPageResponse(
            page.Items.Select(item => new SprintBacklogItemResponse(
                item.Id,
                item.Title,
                item.Type,
                item.Priority,
                item.EstimatePoints,
                item.Rank,
                item.Version)).ToList(),
            page.NextCursor);
    }
}
