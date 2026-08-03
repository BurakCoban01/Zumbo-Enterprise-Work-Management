using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<IReadOnlyCollection<WorkItemDevelopmentLinkResponse>> ListWorkItemLinksAsync(
        string workItemId,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemView,
            ct);
        var documents = await ListAllAsync(
            links,
            item => item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.WorkItemId == workItem.Id,
            ct);
        var connectionStates = await ConnectionStatesAsync(
            organizationId,
            documents.Select(item => item.ConnectionId),
            ct);
        return documents
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(item => ToResponse(
                item,
                connectionStates.GetValueOrDefault(item.ConnectionId)))
            .ToList();
    }

}
