using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

public sealed class CreateMappingHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDevelopmentIntegrationAuthorization authorization,
    IDevelopmentProjectDirectory projectDirectory,
    IProjectPermissionChecker projectPermissions,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentRepositoryMappingResponse> HandleAsync(
        CreateMappingCommand command,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(command.ConnectionId, ct);
        EnsureConnected(connection);
        if (await mappings.CountByFilterAsync(
                item => item.OrganizationId == connection.OrganizationId
                    && item.ConnectionId == connection.Id,
                ct) >= DevelopmentIntegrationLimits.MaximumMappingsPerConnection)
        {
            throw new ValidationException(
                $"A development connection cannot contain more than {DevelopmentIntegrationLimits.MaximumMappingsPerConnection} repository mappings.");
        }

        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var projectId = Required(command.Request.ProjectId, "Project id", 128);
        var projectAccess = await projectPermissions.EnsureCanAsync(
            userId,
            projectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(
                projectAccess.OrganizationId,
                connection.OrganizationId,
                StringComparison.Ordinal))
        {
            throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
        }

        var project = await projectDirectory.GetAsync(
            connection.OrganizationId,
            projectId,
            ct);
        var externalRepositoryId = Required(
            command.Request.ExternalRepositoryId,
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
            RepositoryName = Required(command.Request.RepositoryName, "Repository name", 120),
            RepositoryFullName = Required(
                command.Request.RepositoryFullName,
                "Repository full name",
                240),
            RepositoryUrl = NormalizeRepositoryUrl(
                connection,
                command.Request.RepositoryUrl),
            DefaultBranch = Required(command.Request.DefaultBranch, "Default branch", 255),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await audit.WriteAsync(
            "DevelopmentRepositoryMapped",
            "DevelopmentRepositoryMapping",
            document.Id,
            null,
            $"{document.ProjectKey}|{document.RepositoryFullName}",
            command.CorrelationId,
            ct);
        return ToResponse(document);
    }

    private async Task<DevelopmentConnectionDocument> GetManagedConnectionAsync(
        string connectionId,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await connections.SelectAsync(
            item => item.Id == connectionId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
    }

    private static void EnsureConnected(DevelopmentConnectionDocument connection)
    {
        if (!connection.IsConnected
            || string.IsNullOrWhiteSpace(connection.CredentialProtected)
            || string.IsNullOrWhiteSpace(connection.WebhookSecretProtected))
        {
            throw new ConflictException(
                "DEVELOPMENT_CONNECTION_DISCONNECTED",
                "The development connection is disconnected.");
        }
    }

    private static string Required(string value, string label, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximum)
        {
            throw new ValidationException(
                $"{label} must contain between 1 and {maximum} characters.");
        }

        return normalized;
    }

    private static string NormalizeRepositoryUrl(
        DevelopmentConnectionDocument connection,
        string value)
    {
        var normalized = NormalizeHttpsUrl(value, "Repository URL");
        var providerHost = new Uri(connection.BaseUrl).Host;
        var repositoryHost = new Uri(normalized).Host;
        var allowed = repositoryHost.Equals(providerHost, StringComparison.OrdinalIgnoreCase)
            || connection.Provider == DevelopmentProviders.GitHub
            && providerHost.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            && repositoryHost.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            throw new ValidationException(
                "Repository URL host must match the configured development provider.");
        }

        return normalized;
    }

    private static string NormalizeHttpsUrl(string value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 2_048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException($"{label} must be a safe absolute HTTPS URL.");
        }

        return uri.AbsoluteUri;
    }

    private static DevelopmentRepositoryMappingResponse ToResponse(
        DevelopmentRepositoryMappingDocument document) =>
        new(
            document.Id,
            document.ConnectionId,
            document.ProjectId,
            document.ProjectKey,
            document.ProjectName,
            document.ExternalRepositoryId,
            document.RepositoryName,
            document.RepositoryFullName,
            document.RepositoryUrl,
            document.DefaultBranch,
            document.IsActive,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);
}
