using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static DevelopmentConnectionResponse ToResponse(
        DevelopmentConnectionDocument document) =>
        new(
            document.Id,
            document.Name,
            document.Provider,
            document.BaseUrl,
            document.CredentialFingerprint,
            document.WebhookSecretFingerprint,
            document.WebhookSecretVersion,
            document.IsConnected,
            document.HealthStatus,
            document.HealthErrorCode,
            document.HealthCheckedAtUtc,
            document.DisconnectedAtUtc,
            RequiredScopes(document.Provider),
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);

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

}
