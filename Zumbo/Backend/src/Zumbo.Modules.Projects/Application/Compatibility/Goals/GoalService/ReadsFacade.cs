using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;
using Zumbo.Modules.Projects.Application.Features.Goals;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> GetAsync(
        string goalId,
        bool includeArchived,
        CancellationToken ct)
        => await new GetGoalHandler(goals, currentUser).HandleAsync(
            new GetGoalQuery(goalId, includeArchived), ct);

    public async Task<GoalPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
        => await new ListGoalsHandler(goals, currentUser).HandleAsync(
            new ListGoalsQuery(includeArchived, page, pageSize), ct);

    public async Task<GoalRollupResponse> GetRollupAsync(
        string goalId,
        CancellationToken ct)
        => await new GetGoalRollupHandler(goals, directory, currentUser, clock).HandleAsync(
            new GetGoalRollupQuery(goalId), ct);
}
