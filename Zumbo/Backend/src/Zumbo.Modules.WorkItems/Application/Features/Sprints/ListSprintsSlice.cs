namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class ListSprintsSlice(SprintReadAccess access)
{
    internal async Task<SprintCursorPageResponse> HandleAsync(
        ListSprintsQuery query,
        CancellationToken ct)
    {
        await access.EnsureViewAsync(query.ProjectId, ct);
        var page = await access.ListSprintsAsync(query.ProjectId, query.After, query.PageSize, ct);
        return new SprintCursorPageResponse(
            page.Items.Select(SprintResponseMapper.ToResponse).ToList(),
            page.NextCursor);
    }
}
