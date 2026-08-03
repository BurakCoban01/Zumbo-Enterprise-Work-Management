using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed class CreateTeamHandler(TeamService service)
{
    public Task<TeamResponse> HandleAsync(CreateTeamRequest request, string correlationId, CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}
