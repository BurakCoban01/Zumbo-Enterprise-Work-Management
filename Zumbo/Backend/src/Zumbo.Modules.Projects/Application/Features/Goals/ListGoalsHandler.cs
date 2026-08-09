using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class ListGoalsHandler(GoalService service)
{
    private ListGoalsSlice? slice;

    public ListGoalsHandler(
        IDocumentRepository<GoalDocument> goals,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new ListGoalsSlice(new GoalReadAccess(goals, currentUser), goals);

    public Task<GoalPageResponse> HandleAsync(ListGoalsQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListAsync(query.IncludeArchived, query.Page, query.PageSize, ct);
}
