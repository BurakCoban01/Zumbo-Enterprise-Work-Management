using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task DeleteWorkItemLinkAsync(
        string workItemId,
        string linkId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemLink,
            ct);
        var link = await links.SelectAsync(
            item => item.Id == linkId
                && item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.WorkItemId == workItem.Id,
            ct) ?? throw LinkNotFound();
        if (link.Version != expectedVersion) throw LinkConflict();
        var deleted = await links.DeleteByFilterAsync(
            item => item.Id == link.Id
                && item.OrganizationId == organizationId
                && item.Version == expectedVersion,
            ct);
        if (deleted != 1) throw LinkConflict();
        await WriteAuditAsync(
            "WorkItemDevelopmentLinkDeleted",
            "WorkItem",
            workItem.Id,
            $"{link.Provider}|{link.RepositoryFullName}|{link.Kind}|{link.ExternalId}",
            null,
            correlationId,
            ct);
    }

}
