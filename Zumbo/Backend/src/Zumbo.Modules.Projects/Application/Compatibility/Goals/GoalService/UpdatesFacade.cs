using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;
using Zumbo.Modules.Projects.Application.Features.Goals;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> AddKeyResultProgressAsync(
        string goalId,
        string keyResultId,
        AddKeyResultProgressRequest request,
        string correlationId,
        CancellationToken ct)
        => await new AddKeyResultProgressHandler(
            goals, audit, currentUser, clock, expectedVersion).HandleAsync(
                new AddKeyResultProgressCommand(
                    goalId, keyResultId, request, correlationId),
                ct);

    public async Task<GoalResponse> AddStatusUpdateAsync(
        string goalId,
        AddGoalStatusUpdateRequest request,
        string correlationId,
        CancellationToken ct)
        => await new AddGoalStatusUpdateHandler(
            goals, audit, currentUser, clock, expectedVersion).HandleAsync(
                new AddGoalStatusUpdateCommand(goalId, request, correlationId),
                ct);
}
