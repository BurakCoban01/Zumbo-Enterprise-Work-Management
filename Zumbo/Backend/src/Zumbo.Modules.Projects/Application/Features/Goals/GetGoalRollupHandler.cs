using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class GetGoalRollupHandler(GoalService service)
{
    private GetGoalRollupSlice? slice;

    public GetGoalRollupHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalDirectory directory,
        ICurrentUser currentUser,
        IClock clock)
        : this(null!) =>
        slice = new GetGoalRollupSlice(
            new GoalReadAccess(goals, currentUser),
            directory,
            clock);

    public Task<GoalRollupResponse> HandleAsync(
        GetGoalRollupQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct) ?? service.GetRollupAsync(query.GoalId, ct);
}
