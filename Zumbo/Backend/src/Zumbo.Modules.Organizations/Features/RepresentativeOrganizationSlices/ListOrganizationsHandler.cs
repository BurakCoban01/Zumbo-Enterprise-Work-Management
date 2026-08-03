using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed class ListOrganizationsHandler(OrganizationService service)
{
    public Task<IReadOnlyList<OrganizationResponse>> HandleAsync(ListOrganizationsQuery query, CancellationToken ct)
    {
        ListOrganizationsValidator.Validate(query);
        return service.ListAsync(ct);
    }
}
