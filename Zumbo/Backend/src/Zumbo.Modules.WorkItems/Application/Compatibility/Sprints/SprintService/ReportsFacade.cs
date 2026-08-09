using Zumbo.Modules.WorkItems.Application.Features.Sprints;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService
{
    public async Task<IReadOnlyList<SprintBurndownPointResponse>> BurndownAsync(
        string projectId,
        string sprintId,
        DateOnly? requestedStart,
        DateOnly? requestedEnd,
        CancellationToken ct) =>
        (await getSprintBurndownHandler.HandleAsync(
            new GetSprintBurndownQuery(projectId, sprintId, requestedStart, requestedEnd),
            ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<SprintBurndownPointResponse>>> BurndownSnapshotAsync(
        string projectId,
        string sprintId,
        DateOnly? requestedStart,
        DateOnly? requestedEnd,
        CancellationToken ct) =>
        await getSprintBurndownHandler.HandleAsync(
            new GetSprintBurndownQuery(projectId, sprintId, requestedStart, requestedEnd),
            ct);

    public async Task<IReadOnlyList<SprintVelocityResponse>> VelocityAsync(
        string projectId,
        int sprintCount,
        CancellationToken ct) =>
        (await getSprintVelocityHandler.HandleAsync(
            new GetSprintVelocityQuery(projectId, sprintCount),
            ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<SprintVelocityResponse>>> VelocitySnapshotAsync(
        string projectId,
        int sprintCount,
        CancellationToken ct) =>
        await getSprintVelocityHandler.HandleAsync(
            new GetSprintVelocityQuery(projectId, sprintCount),
            ct);
}
