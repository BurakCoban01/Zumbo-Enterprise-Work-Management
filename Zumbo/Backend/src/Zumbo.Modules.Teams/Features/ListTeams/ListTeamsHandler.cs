using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed class ListTeamsHandler(TeamService service)
{
    private ListTeamsSlice? slice;

    public ListTeamsHandler(
        IDocumentRepository<TeamDocument> teams,
        ITeamOrganizationDirectory organizationDirectory,
        IClock clock,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListTeamsSlice(teams, organizationDirectory, clock, currentUser);
    }

    public Task<IReadOnlyList<TeamResponse>> HandleAsync(ListTeamsQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListAsync(query.OrganizationId, ct, query.Archived);
}
