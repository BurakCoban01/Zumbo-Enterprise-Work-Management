using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Links;

public sealed class CreateWorkItemLinkHandler(
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IProjectPermissionChecker projectPermissions,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<WorkItemDevelopmentLinkResponse> HandleAsync(
        CreateWorkItemLinkCommand command,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(command.WorkItemId, ct);
        var mappingId = Required(command.Request.MappingId, "Repository mapping id", 128);
        var mapping = await mappings.SelectAsync(
            item => item.Id == mappingId
                && item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.IsActive,
            ct) ?? throw new NotFoundException(
                "DEVELOPMENT_REPOSITORY_MAPPING_NOT_FOUND",
                "Development repository mapping was not found.");
        var connection = await connections.SelectAsync(
            item => item.Id == mapping.ConnectionId
                && item.OrganizationId == organizationId
                && item.IsConnected,
            ct) ?? throw new ConflictException(
                "DEVELOPMENT_CONNECTION_DISCONNECTED",
                "The development connection is disconnected.");
        if (await links.CountByFilterAsync(
                item => item.OrganizationId == organizationId
                    && item.WorkItemId == workItem.Id,
                ct) >= DevelopmentIntegrationLimits.MaximumLinksPerWorkItem)
        {
            throw new ValidationException(
                $"A work item cannot contain more than {DevelopmentIntegrationLimits.MaximumLinksPerWorkItem} development links.");
        }

        var normalized = NormalizeLinkRequest(mapping, command.Request);
        var id = StableId(
            connection.Id,
            mapping.Id,
            workItem.Id,
            normalized.Kind,
            normalized.ExternalId);
        var existing = await links.SelectAsync(
            item => item.Id == id && item.OrganizationId == organizationId,
            ct);
        if (existing is not null)
        {
            return ToResponse(existing, connection.IsConnected);
        }

        var now = clock.UtcNow;
        var document = await links.CreateAsync(new WorkItemDevelopmentLinkDocument
        {
            Id = id,
            OrganizationId = organizationId,
            ConnectionId = connection.Id,
            MappingId = mapping.Id,
            ProjectId = mapping.ProjectId,
            WorkItemId = workItem.Id,
            Provider = connection.Provider,
            RepositoryFullName = mapping.RepositoryFullName,
            Kind = normalized.Kind,
            ExternalId = normalized.ExternalId,
            Title = normalized.Title,
            Url = normalized.Url,
            Branch = normalized.Branch,
            CommitSha = normalized.CommitSha,
            Status = normalized.Status,
            Source = "Manual",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await audit.WriteAsync(
            "WorkItemDevelopmentLinkCreated",
            "WorkItem",
            workItem.Id,
            null,
            $"{document.Provider}|{document.RepositoryFullName}|{document.Kind}|{document.ExternalId}",
            command.CorrelationId,
            ct);
        return ToResponse(document, true);
    }

    private async Task<(WorkItemDocument WorkItem, string OrganizationId)> GetWorkItemAsync(
        string workItemId,
        CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        var workItem = await workItems.SelectAsync(
            item => item.Id == workItemId && !item.Archived,
            ct) ?? throw new NotFoundException(
                "WORK_ITEM_NOT_FOUND",
                "Work item was not found.");
        var access = await projectPermissions.EnsureCanAsync(
            userId,
            workItem.ProjectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(access.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        }

        return (workItem, access.OrganizationId);
    }

    private static LinkValues NormalizeLinkRequest(
        DevelopmentRepositoryMappingDocument mapping,
        CreateWorkItemDevelopmentLinkRequest request) =>
        new(
            NormalizeKind(request.Kind),
            Required(request.ExternalId, "External development id", 300),
            Required(request.Title, "Development link title", 200),
            NormalizeLinkUrl(mapping.RepositoryUrl, request.Url),
            Optional(request.Branch, "Development branch", 255),
            Optional(request.CommitSha, "Development commit", 128),
            NormalizeStatus(request.Status));

    private static string NormalizeKind(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var kind = DevelopmentLinkKinds.All.FirstOrDefault(
            item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return kind ?? throw new ValidationException(
            "Development link kind is not supported.");
    }

    private static string NormalizeLinkUrl(string repositoryUrl, string value)
    {
        var normalized = NormalizeHttpsUrl(value, "Development link URL");
        if (!new Uri(normalized).Host.Equals(
                new Uri(repositoryUrl).Host,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "Development link URL host must match the mapped repository.");
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

    private static string NormalizeStatus(string value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "open" => "Open",
            "merged" => "Merged",
            "closed" => "Closed",
            "success" => "Success",
            "failed" => "Failed",
            "pending" => "Pending",
            "running" => "Running",
            "pushed" => "Pushed",
            "unknown" or "" or null => "Unknown",
            _ => throw new ValidationException("Development status is not supported.")
        };

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

    private static string? Optional(string? value, string label, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maximum)
        {
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        }

        return normalized;
    }

    private static string StableId(params string[] values) =>
        Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join('\u001f', values))))
            .ToLowerInvariant()[..32];

    private static WorkItemDevelopmentLinkResponse ToResponse(
        WorkItemDevelopmentLinkDocument document,
        bool connectionActive) =>
        new(
            document.Id,
            document.ConnectionId,
            document.MappingId,
            document.ProjectId,
            document.WorkItemId,
            document.Provider,
            document.RepositoryFullName,
            document.Kind,
            document.ExternalId,
            document.Title,
            document.Url,
            document.Branch,
            document.CommitSha,
            document.Status,
            document.Source,
            connectionActive,
            document.LastEventAtUtc,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);

    private sealed record LinkValues(
        string Kind,
        string ExternalId,
        string Title,
        string Url,
        string? Branch,
        string? CommitSha,
        string Status);
}
