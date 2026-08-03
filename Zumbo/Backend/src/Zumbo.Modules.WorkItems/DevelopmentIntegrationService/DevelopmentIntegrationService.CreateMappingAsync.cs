using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentRepositoryMappingResponse> CreateMappingAsync(
        string connectionId,
        CreateDevelopmentRepositoryMappingRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        if (await mappings.CountByFilterAsync(
                item => item.OrganizationId == connection.OrganizationId
                    && item.ConnectionId == connection.Id,
                ct) >= DevelopmentIntegrationLimits.MaximumMappingsPerConnection)
        {
            throw new ValidationException(
                $"A development connection cannot contain more than {DevelopmentIntegrationLimits.MaximumMappingsPerConnection} repository mappings.");
        }

        var userId = RequireUser();
        var projectId = Required(request.ProjectId, "Project id", 128);
        var projectAccess = await projectPermissions.EnsureCanAsync(
            userId,
            projectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(projectAccess.OrganizationId, connection.OrganizationId, StringComparison.Ordinal))
            throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
        var project = await projectDirectory.GetAsync(connection.OrganizationId, projectId, ct);
        var externalRepositoryId = Required(
            request.ExternalRepositoryId,
            "External repository id",
            200);
        if (await mappings.ExistsByFilterAsync(
                item => item.OrganizationId == connection.OrganizationId
                    && item.ConnectionId == connection.Id
                    && item.ExternalRepositoryId == externalRepositoryId,
                ct))
        {
            throw new ConflictException(
                "DEVELOPMENT_REPOSITORY_ALREADY_MAPPED",
                "The provider repository is already mapped for this connection.");
        }

        var now = clock.UtcNow;
        var document = await mappings.CreateAsync(new DevelopmentRepositoryMappingDocument
        {
            OrganizationId = connection.OrganizationId,
            ConnectionId = connection.Id,
            ProjectId = project.ProjectId,
            ProjectKey = project.ProjectKey,
            ProjectName = project.ProjectName,
            ExternalRepositoryId = externalRepositoryId,
            RepositoryName = Required(request.RepositoryName, "Repository name", 120),
            RepositoryFullName = Required(request.RepositoryFullName, "Repository full name", 240),
            RepositoryUrl = NormalizeRepositoryUrl(connection, request.RepositoryUrl),
            DefaultBranch = Required(request.DefaultBranch, "Default branch", 255),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await WriteAuditAsync(
            "DevelopmentRepositoryMapped",
            "DevelopmentRepositoryMapping",
            document.Id,
            null,
            $"{document.ProjectKey}|{document.RepositoryFullName}",
            correlationId,
            ct);
        return ToResponse(document);
    }

}
