using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class GetGoalHandler(GoalService service)
{
    private GetGoalSlice? slice;

    public GetGoalHandler(
        IDocumentRepository<GoalDocument> goals,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new GetGoalSlice(new GoalReadAccess(goals, currentUser));

    public Task<GoalResponse> HandleAsync(GetGoalQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetAsync(query.GoalId, query.IncludeArchived, ct);
}
