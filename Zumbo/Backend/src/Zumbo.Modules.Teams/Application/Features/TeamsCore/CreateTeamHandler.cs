using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed class CreateTeamHandler(TeamService service)
{
    private CreateTeamSlice? slice;

    public CreateTeamHandler(
        IDocumentRepository<TeamDocument> teams,
        ITeamUserDirectory userDirectory,
        ITeamOrganizationDirectory organizationDirectory,
        ITeamAuditWriter audit,
        IClock clock,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new CreateTeamSlice(
            teams,
            userDirectory,
            organizationDirectory,
            audit,
            clock,
            currentUser);
    }

    public Task<TeamResponse> HandleAsync(
        CreateTeamRequest request,
        string correlationId,
        CancellationToken ct) =>
        slice?.HandleAsync(request, correlationId, ct)
        ?? service.CreateAsync(request, correlationId, ct);
}
