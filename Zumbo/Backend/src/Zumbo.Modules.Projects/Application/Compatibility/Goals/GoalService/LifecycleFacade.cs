using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;
using Zumbo.Modules.Projects.Application.Features.Goals;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> SaveAsync(
        string? goalId,
        SaveGoalRequest request,
        string correlationId,
        CancellationToken ct)
        => await new SaveGoalHandler(
            goals, directory, audit, currentUser, clock, expectedVersion).HandleAsync(
                new SaveGoalCommand(goalId, request, correlationId), ct);

    public async Task<GoalResponse> SaveKeyResultAsync(
        string goalId,
        string? keyResultId,
        SaveKeyResultRequest request,
        string correlationId,
        CancellationToken ct)
        => await new SaveKeyResultHandler(
            goals, directory, audit, currentUser, clock, expectedVersion).HandleAsync(
                new SaveKeyResultCommand(
                    goalId, keyResultId, request, correlationId),
                ct);

    public async Task ArchiveAsync(
        string goalId,
        string correlationId,
        CancellationToken ct)
        => await new ArchiveGoalHandler(
            goals, audit, currentUser, clock, expectedVersion).HandleAsync(
                new ArchiveGoalCommand(goalId, correlationId), ct);
}
