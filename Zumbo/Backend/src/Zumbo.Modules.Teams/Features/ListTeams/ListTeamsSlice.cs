using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

internal sealed class ListTeamsSlice(
    IDocumentRepository<TeamDocument> teams,
    ITeamOrganizationDirectory organizationDirectory,
    IClock clock,
    ICurrentUser currentUser)
{
    internal async Task<IReadOnlyList<TeamResponse>> HandleAsync(
        ListTeamsQuery query,
        CancellationToken ct)
    {
        ListTeamsValidator.Validate(query);
        EnsureOrganizationScope(query.OrganizationId);
        var normalizedOrganizationId = query.OrganizationId.Trim();
        await organizationDirectory.EnsureActiveAsync(normalizedOrganizationId, ct);
        var result = await teams.ListByFilterAsync(
            team => team.OrganizationId == normalizedOrganizationId && team.Archived == query.Archived,
            team => team.Name,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(team => TeamResponseMapper.ToResponse(team, clock)).ToList();
    }

    private void EnsureOrganizationScope(string organizationId)
    {
        if (!PermissionCatalog.IsSystemAdministrator(currentUser.Roles)
            && !string.Equals(currentUser.OrganizationId, organizationId.Trim(), StringComparison.Ordinal))
        {
            throw new ForbiddenException("User cannot access teams outside the current organization.");
        }
    }
}
