using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<WorkItemDevelopmentLinkResponse> CreateWorkItemLinkAsync(
        string workItemId,
        CreateWorkItemDevelopmentLinkRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemLink,
            ct);
        var mappingId = Required(request.MappingId, "Repository mapping id", 128);
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

        var normalized = NormalizeLinkRequest(mapping, request);
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
            return ToResponse(existing, connection.IsConnected);
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
        await WriteAuditAsync(
            "WorkItemDevelopmentLinkCreated",
            "WorkItem",
            workItem.Id,
            null,
            $"{document.Provider}|{document.RepositoryFullName}|{document.Kind}|{document.ExternalId}",
            correlationId,
            ct);
        return ToResponse(document, true);
    }

}
