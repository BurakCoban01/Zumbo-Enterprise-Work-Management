using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService{

    public Task<IReadOnlyList<UserProfileResponse>> SearchUsersAsync(string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var organizationId = PermissionCatalog.IsSystemAdministrator(currentUser.Roles)
            ? null
            : currentUser.OrganizationId
                ?? throw new ForbiddenException("Organization scope is required.");
        return users.SearchAsync(search, organizationId, ct);
    }
}
