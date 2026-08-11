using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class IdentityPermissionService(
    IDocumentRepository<UserDocument> users,
    IdentityRoleCatalogService roleCatalog,
    ICurrentUser currentUser)
{
    public async Task<bool> HasPermissionAsync(string permission, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(permission) || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return false;
        }

        var userId = currentUser.UserId;
        var user = await users.SelectAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null)
        {
            return false;
        }

        return await roleCatalog.HasPermissionAsync(user.Roles, user.OrganizationId, permission, ct);
    }
}
