using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>> ListMappingsAsync(
        string connectionId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        var documents = await ListAllAsync(
            mappings,
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        return documents
            .OrderBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

}
