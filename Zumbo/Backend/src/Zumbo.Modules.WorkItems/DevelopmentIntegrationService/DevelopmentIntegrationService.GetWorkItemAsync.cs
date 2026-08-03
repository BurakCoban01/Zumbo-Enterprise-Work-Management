using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private async Task<(WorkItemDocument WorkItem, string OrganizationId)> GetWorkItemAsync(
        string workItemId,
        string permission,
        CancellationToken ct)
    {
        var userId = RequireUser();
        var workItem = await workItems.SelectAsync(
            item => item.Id == workItemId && !item.Archived,
            ct) ?? throw new NotFoundException(
                "WORK_ITEM_NOT_FOUND",
                "Work item was not found.");
        var access = await projectPermissions.EnsureCanAsync(
            userId,
            workItem.ProjectId,
            permission,
            ct);
        if (!string.Equals(access.OrganizationId, RequireOrganization(), StringComparison.Ordinal))
            throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        return (workItem, access.OrganizationId);
    }

}
