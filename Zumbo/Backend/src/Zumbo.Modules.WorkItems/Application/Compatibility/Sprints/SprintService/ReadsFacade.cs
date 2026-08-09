using Zumbo.Modules.WorkItems.Application.Features.Sprints;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService
{
    public async Task<SprintResponse> GetAsync(string sprintId, CancellationToken ct) =>
        await getSprintHandler.HandleAsync(new GetSprintQuery(sprintId), ct);

    public async Task<SprintCursorPageResponse> ListAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct) =>
        await listSprintsHandler.HandleAsync(
            new ListSprintsQuery(projectId, after, pageSize),
            ct);

    public async Task<SprintBacklogPageResponse> BacklogAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct) =>
        await listSprintBacklogHandler.HandleAsync(
            new ListSprintBacklogQuery(projectId, after, pageSize),
            ct);
}
