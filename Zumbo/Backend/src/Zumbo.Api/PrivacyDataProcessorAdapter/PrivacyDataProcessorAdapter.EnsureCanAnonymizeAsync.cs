using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed partial class PrivacyDataProcessorAdapter{

    public async Task EnsureCanAnonymizeAsync(string userId, string organizationId, CancellationToken ct)
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
