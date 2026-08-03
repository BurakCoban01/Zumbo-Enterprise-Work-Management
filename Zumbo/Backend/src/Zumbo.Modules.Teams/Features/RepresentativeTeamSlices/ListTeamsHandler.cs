using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed class ListTeamsHandler(TeamService service)
{
    public Task<IReadOnlyList<TeamResponse>> HandleAsync(ListTeamsQuery query, CancellationToken ct)
    {
        ListTeamsValidator.Validate(query);
        return service.ListAsync(query.OrganizationId, ct, query.Archived);
    }
}
