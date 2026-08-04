using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

public sealed class DeleteMappingHandler(
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IDevelopmentIntegrationAuthorization authorization,
    IProjectPermissionChecker projectPermissions,
    IWorkItemAuditPublisher audit,
    ICurrentUser currentUser)
{
    public async Task HandleAsync(DeleteMappingCommand command, CancellationToken ct)
    {
        var mapping = await GetManagedMappingAsync(command.MappingId, ct);
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        await projectPermissions.EnsureCanAsync(
            userId,
            mapping.ProjectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (mapping.Version != command.ExpectedVersion)
        {
            throw MappingConflict();
        }

        await links.DeleteByFilterAsync(
            item => item.OrganizationId == mapping.OrganizationId
                && item.MappingId == mapping.Id,
            ct);
        var deleted = await mappings.DeleteByFilterAsync(
            item => item.Id == mapping.Id
                && item.OrganizationId == mapping.OrganizationId
                && item.Version == command.ExpectedVersion,
            ct);
        if (deleted != 1)
        {
            throw MappingConflict();
        }

        await audit.WriteAsync(
            "DevelopmentRepositoryUnmapped",
            "DevelopmentRepositoryMapping",
            mapping.Id,
            $"{mapping.ProjectKey}|{mapping.RepositoryFullName}",
            null,
            command.CorrelationId,
            ct);
    }

    private async Task<DevelopmentRepositoryMappingDocument> GetManagedMappingAsync(
        string mappingId,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await mappings.SelectAsync(
            item => item.Id == mappingId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "DEVELOPMENT_REPOSITORY_MAPPING_NOT_FOUND",
                "Development repository mapping was not found.");
    }

    private static ConflictException MappingConflict() => new(
        "DEVELOPMENT_MAPPING_CONFLICT",
        "Development repository mapping changed concurrently; refresh and retry.");
}
