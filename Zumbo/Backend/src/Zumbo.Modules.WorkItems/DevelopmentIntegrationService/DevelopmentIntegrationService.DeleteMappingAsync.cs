using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task DeleteMappingAsync(
        string mappingId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
    {
        var mapping = await GetManagedMappingAsync(mappingId, ct);
        await projectPermissions.EnsureCanAsync(
            RequireUser(),
            mapping.ProjectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (mapping.Version != expectedVersion)
            throw MappingConflict();
        await links.DeleteByFilterAsync(
            item => item.OrganizationId == mapping.OrganizationId
                && item.MappingId == mapping.Id,
            ct);
        var deleted = await mappings.DeleteByFilterAsync(
            item => item.Id == mapping.Id
                && item.OrganizationId == mapping.OrganizationId
                && item.Version == expectedVersion,
            ct);
        if (deleted != 1) throw MappingConflict();
        await WriteAuditAsync(
            "DevelopmentRepositoryUnmapped",
            "DevelopmentRepositoryMapping",
            mapping.Id,
            $"{mapping.ProjectKey}|{mapping.RepositoryFullName}",
            null,
            correlationId,
            ct);
    }

}
