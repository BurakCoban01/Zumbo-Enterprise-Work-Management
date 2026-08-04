using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<IReadOnlyCollection<DevelopmentConnectionResponse>> ListAsync(
        CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var documents = await ListAllAsync(
            connections,
            item => item.OrganizationId == organizationId,
            ct);
        return documents
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(ToResponse)
            .ToList();
    }

}
