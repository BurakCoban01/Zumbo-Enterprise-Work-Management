using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>>
        ListWorkItemMappingsAsync(
            string workItemId,
            CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemLink,
            ct);
        var documents = await ListAllAsync(
            mappings,
            item => item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.IsActive,
            ct);
        return documents
            .OrderBy(item => item.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

}
