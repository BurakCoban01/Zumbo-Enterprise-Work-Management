using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

internal sealed class SearchUsersSlice(
    IUserRepository users,
    ICurrentUser currentUser)
{
    internal Task<IReadOnlyList<UserProfileResponse>> HandleAsync(SearchUsersQuery query, CancellationToken ct)
    {
        SearchUsersValidator.Validate(query);

        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var organizationId = PermissionCatalog.IsSystemAdministrator(currentUser.Roles)
            ? null
            : currentUser.OrganizationId
                ?? throw new ForbiddenException("Organization scope is required.");
        return users.SearchAsync(query.Search, organizationId, ct);
    }
}
