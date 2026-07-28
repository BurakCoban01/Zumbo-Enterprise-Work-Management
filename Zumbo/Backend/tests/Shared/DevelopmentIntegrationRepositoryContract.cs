using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.RepositoryContracts;

public abstract class DevelopmentIntegrationRepositoryContract
{
    protected abstract IDocumentRepository<DevelopmentConnectionDocument>
        Connections();

    protected abstract IDocumentRepository<DevelopmentRepositoryMappingDocument>
        Mappings();

    protected abstract IDocumentRepository<WorkItemDevelopmentLinkDocument>
        Links();

    protected abstract IDocumentRepository<DevelopmentWebhookReceiptDocument>
        Receipts();

    [Fact]
    public async Task StoresTenantScopedConnectionMappingLinkAndReceiptWithCompareExchange()
    {
        var connections = Connections();
        var mappings = Mappings();
        var links = Links();
        var receipts = Receipts();
        var prefix = "development-contract-" + Guid.NewGuid().ToString("N");
        var organizationId = prefix + "-organization";
        var connection = new DevelopmentConnectionDocument
        {
            Id = prefix + "-connection",
            OrganizationId = organizationId,
            Name = "Synthetic GitHub",
            Provider = DevelopmentProviders.GitHub,
            BaseUrl = "https://api.github.com",
            CredentialProtected = "protected-credential",
            CredentialFingerprint = "credential-fp",
            WebhookSecretProtected = "protected-webhook",
            WebhookSecretFingerprint = "webhook-fp",
            LifecycleVersion = 1,
            CreatedByUserId = prefix + "-owner",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var mapping = new DevelopmentRepositoryMappingDocument
        {
            Id = prefix + "-mapping",
            OrganizationId = organizationId,
            ConnectionId = connection.Id,
            ProjectId = prefix + "-project",
            ProjectKey = "PLAT",
            ProjectName = "Platform",
            ExternalRepositoryId = "42",
            RepositoryName = "repo",
            RepositoryFullName = "acme/repo",
            RepositoryUrl = "https://github.com/acme/repo",
            DefaultBranch = "main",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var link = new WorkItemDevelopmentLinkDocument
        {
            Id = prefix + "-link",
            OrganizationId = organizationId,
            ConnectionId = connection.Id,
            MappingId = mapping.Id,
            ProjectId = mapping.ProjectId,
            WorkItemId = prefix + "-work-item",
            Provider = DevelopmentProviders.GitHub,
            RepositoryFullName = mapping.RepositoryFullName,
            Kind = DevelopmentLinkKinds.PullRequest,
            ExternalId = "pr:17",
            Title = "Synthetic pull request",
            Url = "https://github.com/acme/repo/pull/17",
            Branch = "feature/PLAT-12345678",
            CommitSha = "0123456789abcdef",
            Status = "Open",
            Source = "Webhook",
            LastEventAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var receipt = new DevelopmentWebhookReceiptDocument
        {
            Id = prefix + "-receipt",
            OrganizationId = organizationId,
            ConnectionId = connection.Id,
            DeliveryId = prefix + "-delivery",
            ProviderEvent = "pull_request",
            PayloadSha256 = new string('a', 64),
            Status = DevelopmentWebhookReceiptStatuses.Pending,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(90)
        };

        try
        {
            connection = await connections.CreateAsync(connection);
            var stale = await connections.SelectAsync(
                item => item.Id == connection.Id);
            mapping = await mappings.CreateAsync(mapping);
            link = await links.CreateAsync(link);
            receipt = await receipts.CreateAsync(receipt);

            var loadedConnection = await connections.SelectAsync(item =>
                item.Id == connection.Id
                && item.OrganizationId == organizationId
                && item.IsConnected);
            Assert.NotNull(loadedConnection);
            Assert.Equal("protected-credential", loadedConnection.CredentialProtected);
            Assert.Equal(1, loadedConnection.LifecycleVersion);
            Assert.Null(await connections.SelectAsync(item =>
                item.Id == connection.Id
                && item.OrganizationId == prefix + "-foreign"));

            Assert.NotNull(await mappings.SelectAsync(item =>
                item.OrganizationId == organizationId
                && item.ConnectionId == connection.Id
                && item.ExternalRepositoryId == "42"));
            Assert.NotNull(await links.SelectAsync(item =>
                item.OrganizationId == organizationId
                && item.ProjectId == mapping.ProjectId
                && item.WorkItemId == link.WorkItemId));
            Assert.NotNull(await receipts.SelectAsync(item =>
                item.ConnectionId == connection.Id
                && item.ExpiresAtUtc > DateTime.UtcNow));

            connection.HealthStatus = "Healthy";
            connection.UpdatedAtUtc = connection.UpdatedAtUtc.AddMinutes(1);
            var replaced = await connections.ReplaceByVersionAsync(
                item => item.Id == connection.Id
                    && item.OrganizationId == organizationId,
                connection,
                connection.Version);
            Assert.True(replaced.Found);
            connection.Version = replaced.Version!.Value;

            stale!.HealthStatus = "Degraded";
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                connections.ReplaceByVersionAsync(
                    item => item.Id == stale.Id,
                    stale,
                    stale.Version));
        }
        finally
        {
            await receipts.DeleteByFilterAsync(
                item => item.Id == receipt.Id);
            await links.DeleteByFilterAsync(
                item => item.Id == link.Id);
            await mappings.DeleteByFilterAsync(
                item => item.Id == mapping.Id);
            await connections.DeleteByFilterAsync(
                item => item.Id == connection.Id);
        }
    }
}
