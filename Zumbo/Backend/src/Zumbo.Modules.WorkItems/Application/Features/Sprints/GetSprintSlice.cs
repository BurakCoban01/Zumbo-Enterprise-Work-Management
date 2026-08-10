namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class GetSprintSlice(SprintReadAccess access)
{
    internal async Task<SprintResponse> HandleAsync(GetSprintQuery query, CancellationToken ct)
    {
        var sprint = await access.GetSprintAsync(query.SprintId, ct);
        await access.EnsureViewAsync(sprint.ProjectId, ct);
        return SprintResponseMapper.ToResponse(sprint);
    }
}
