using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

internal sealed class ListOrganizationsSlice(
    IDocumentRepository<OrganizationDocument> organizations,
    ICurrentUser currentUser)
{
    internal async Task<IReadOnlyList<OrganizationResponse>> HandleAsync(
        ListOrganizationsQuery query,
        CancellationToken ct)
    {
        ListOrganizationsValidator.Validate(query);
        RequireCurrentUser();

        var tenantId = currentUser.OrganizationId;
        var result = PermissionCatalog.IsSystemAdministrator(currentUser.Roles)
            ? await organizations.ListByFilterAsync(
                orderBy: document => document.Name,
                pageSize: 100,
                cancellationToken: ct)
            : await organizations.ListByFilterAsync(
                document => document.Id == tenantId || document.TenantKey == tenantId,
                document => document.Name,
                pageSize: 1,
                cancellationToken: ct);
        return result.Select(OrganizationResponseMapper.ToResponse).ToList();
    }

    private void RequireCurrentUser()
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }
    }
}
