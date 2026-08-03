using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public interface IOrganizationMemberDirectory
{
    Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct);
}
