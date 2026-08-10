using Zumbo.Modules.WorkItems.Application.Features.Sprints;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService
{
    public async Task<SprintResponse> CreateAsync(
        CreateSprintRequest request,
        string correlationId,
        CancellationToken ct) =>
        await createSprintHandler.HandleAsync(new CreateSprintCommand(request, correlationId), ct);

    public async Task<SprintResponse> StartAsync(
        string sprintId,
        string correlationId,
        CancellationToken ct) =>
        await startSprintHandler.HandleAsync(new StartSprintCommand(sprintId, correlationId), ct);

    public async Task<SprintResponse> CompleteAsync(
        string sprintId,
        CompleteSprintRequest request,
        string correlationId,
        CancellationToken ct) =>
        await completeSprintHandler.HandleAsync(
            new CompleteSprintCommand(sprintId, request, correlationId), ct);
}
