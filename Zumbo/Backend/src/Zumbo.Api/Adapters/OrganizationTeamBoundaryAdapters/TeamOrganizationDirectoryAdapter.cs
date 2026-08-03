using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

public sealed class TeamOrganizationDirectoryAdapter(
    IDocumentRepository<OrganizationDocument> organizations) : ITeamOrganizationDirectory
{
    public async Task EnsureActiveAsync(string organizationId, CancellationToken ct)
    {
        var organization = await organizations.SelectAsync(
            candidate => candidate.Id == organizationId || candidate.TenantKey == organizationId,
            ct)
            ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Team organization was not found.");
        if (!string.IsNullOrWhiteSpace(organization.Status)
            && !string.Equals(organization.Status, OrganizationStatuses.Active, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "TEAM_ORGANIZATION_INACTIVE",
                "Teams require an active organization.");
        }
    }
}
