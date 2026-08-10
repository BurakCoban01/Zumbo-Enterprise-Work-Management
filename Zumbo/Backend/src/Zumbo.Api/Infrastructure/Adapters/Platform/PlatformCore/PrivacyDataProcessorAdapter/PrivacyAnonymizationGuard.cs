using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Infrastructure.Adapters.Platform.PlatformCore.PrivacyDataProcessorAdapter;

internal sealed class PrivacyAnonymizationGuard(
    IDocumentRepository<OrganizationDocument> organizations,
    IDocumentRepository<TeamDocument> teams,
    IDocumentRepository<ProjectDocument> projects)
{
    internal async Task EnsureCanAnonymizeAsync(string userId, string organizationId, CancellationToken ct)
    {
        var ownedOrganization = await organizations.SelectAsync(
            x => (x.Id == organizationId || x.TenantKey == organizationId) && x.OwnerUserId == userId,
            ct);
        if (ownedOrganization is not null)
        {
            throw new ConflictException(
                "PRIVACY_OWNERSHIP_TRANSFER_REQUIRED",
                "Organization ownership must be transferred before anonymization.");
        }

        var ownedTeam = await teams.SelectAsync(
            x => x.OrganizationId == organizationId
                && !x.Archived
                && x.Members.Any(member => member.UserId == userId && member.Status == "Active" && member.Role == "Owner"),
            ct);
        var ownedProject = await projects.SelectAsync(
            x => x.OrganizationId == organizationId
                && !x.Archived
                && x.Members.Any(member => member.UserId == userId && member.Role == "ProjectOwner"),
            ct);
        if (ownedTeam is not null || ownedProject is not null)
        {
            throw new ConflictException(
                "PRIVACY_OWNERSHIP_TRANSFER_REQUIRED",
                "Team and project ownership must be transferred before anonymization.");
        }
    }
}
