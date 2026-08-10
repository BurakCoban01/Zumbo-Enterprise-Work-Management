using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

public sealed class OrganizationMemberDirectoryAdapter(IUserRepository users) : IOrganizationMemberDirectory
{
    public async Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "Organization member user was not found.");
        if (!user.IsActive)
        {
            throw new ConflictException("USER_INACTIVE", "Inactive users cannot be assigned to departments.");
        }

        if (!string.Equals(user.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "ORGANIZATION_MEMBER_TENANT_MISMATCH",
                "Department members must belong to the organization tenant.");
        }
    }
}
