using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class DevelopmentIntegrationServiceTests
{
    [Fact]
    public async Task CreateProtectsCredentialsAndHidesConnectionsAcrossTenants()
    {
        var fixture = new Fixture();
        var receipt = await fixture.CreateConnectionAsync(
            DevelopmentProviders.GitHub);
        var stored = await fixture.Connections.SelectAsync(
            item => item.Id == receipt.Connection.Id);

        Assert.NotNull(stored);
        Assert.NotEqual(Fixture.AccessToken, stored.CredentialProtected);
        Assert.Equal(
            Fixture.AccessToken,
            fixture.Protector.Unprotect(stored.CredentialProtected));
        Assert.NotEqual(receipt.WebhookSecret, stored.WebhookSecretProtected);
        Assert.DoesNotContain(
            Fixture.AccessToken,
            fixture.Audit.Entries.Select(entry => entry.NewValue));
        Assert.Equal("https://api.github.com", stored.BaseUrl);

        fixture.Current.OrganizationIdValue = "foreign-organization";
        Assert.Empty(await fixture.Service.ListAsync(CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetAsync(
                receipt.Connection.Id,
                CancellationToken.None));
    }

    [Fact]
    public async Task RotateWebhookSecretPreservesPreviousSecretForFifteenMinutes()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateConnectionAsync(DevelopmentProviders.GitHub);
        var before = await fixture.Connections.SelectAsync(
            item => item.Id == created.Connection.Id);
        Assert.NotNull(before);

        var rotated = await fixture.Service.RotateWebhookSecretAsync(
            created.Connection.Id,
            new DevelopmentVersionRequest(created.Connection.Version),
            "rotate-webhook-correlation",
            CancellationToken.None);
        var stored = await fixture.Connections.SelectAsync(
            item => item.Id == created.Connection.Id);

        Assert.NotNull(stored);
        Assert.NotEqual(created.WebhookSecret, rotated.WebhookSecret);
        Assert.Equal(before.WebhookSecretProtected, stored.PreviousWebhookSecretProtected);
        Assert.Equal(before.WebhookSecretVersion, stored.PreviousWebhookSecretVersion);
        Assert.Equal(
            fixture.Clock.UtcNow.AddMinutes(15),
            stored.PreviousWebhookSecretValidUntilUtc);
        Assert.Equal(before.WebhookSecretVersion + 1, stored.WebhookSecretVersion);
        Assert.Equal(rotated.Connection.WebhookSecretFingerprint, stored.WebhookSecretFingerprint);
        Assert.Equal(rotated.WebhookSecret, fixture.Protector.Unprotect(stored.WebhookSecretProtected));
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "DevelopmentWebhookSecretRotated"
                && entry.CorrelationId == "rotate-webhook-correlation");
    }

    [Fact]
    public async Task WorkItemMappingDiscoveryReturnsOnlyActiveMappingsForItsProject()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);

        var available = await fixture.Service.ListWorkItemMappingsAsync(
            "abcdef1234567890",
            CancellationToken.None);
        Assert.Equal(setup.Mapping.Id, Assert.Single(available).Id);

        var current = await fixture.Service.GetAsync(
            setup.Connection.Connection.Id,
            CancellationToken.None);
        _ = await fixture.Service.DisconnectAsync(
            current.Id,
            new DevelopmentVersionRequest(current.Version),
            "disconnect-correlation",
            CancellationToken.None);

        Assert.Empty(await fixture.Service.ListWorkItemMappingsAsync(
            "abcdef1234567890",
            CancellationToken.None));
    }

    [Fact]
    public async Task ConnectionMappingListIsTenantScopedAndDeterministicallyOrdered()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(DevelopmentProviders.GitHub);
        _ = await fixture.Service.CreateMappingAsync(
            setup.Connection.Connection.Id,
            new CreateDevelopmentRepositoryMappingRequest(
                "project-1",
                "41",
                "another-repo",
                "acme/another-repo",
                "https://github.com/acme/another-repo",
                "main"),
            "second-mapping-correlation",
            CancellationToken.None);

        var mappings = await fixture.Service.ListMappingsAsync(
            setup.Connection.Connection.Id,
            CancellationToken.None);
        Assert.Equal(
            ["acme/another-repo", "acme/repo"],
            mappings.Select(item => item.RepositoryFullName));

        fixture.Current.OrganizationIdValue = "foreign-organization";
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.ListMappingsAsync(
                setup.Connection.Connection.Id,
                CancellationToken.None));
    }

    [Fact]
    public async Task DeleteMappingRejectsStaleVersionThenRemovesLinkedDevelopmentData()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(DevelopmentProviders.GitHub);
        var link = await fixture.Service.CreateWorkItemLinkAsync(
            "abcdef1234567890",
            new CreateWorkItemDevelopmentLinkRequest(
                setup.Mapping.Id,
                DevelopmentLinkKinds.PullRequest,
                "pr:18",
                "Synthetic pull request",
                "https://github.com/acme/repo/pull/18",
                "feature/delete-mapping",
                "0123456789abcdef",
                "Open"),
            "link-correlation",
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.DeleteMappingAsync(
                setup.Mapping.Id,
                setup.Mapping.Version + 1,
                "stale-delete-correlation",
                CancellationToken.None));
        Assert.NotNull(await fixture.Mappings.SelectAsync(
            item => item.Id == setup.Mapping.Id));
        Assert.NotNull(await fixture.Links.SelectAsync(item => item.Id == link.Id));

        await fixture.Service.DeleteMappingAsync(
            setup.Mapping.Id,
            setup.Mapping.Version,
            "delete-mapping-correlation",
            CancellationToken.None);

        Assert.Null(await fixture.Mappings.SelectAsync(
            item => item.Id == setup.Mapping.Id));
        Assert.Null(await fixture.Links.SelectAsync(item => item.Id == link.Id));
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "DevelopmentRepositoryUnmapped"
                && entry.CorrelationId == "delete-mapping-correlation");
    }

    [Fact]
    public async Task CreateWorkItemLinkIsIdempotentNormalizesAndAuditsOnce()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var request = new CreateWorkItemDevelopmentLinkRequest(
            $"  {setup.Mapping.Id}  ",
            " pullrequest ",
            " pr:24 ",
            " Synthetic pull request ",
            "https://github.com/acme/repo/pull/24",
            " feature/link-handler ",
            " 0123456789abcdef ",
            " open ");

        var first = await fixture.Service.CreateWorkItemLinkAsync(
            "abcdef1234567890",
            request,
            "create-link-correlation",
            CancellationToken.None);
        var second = await fixture.Service.CreateWorkItemLinkAsync(
            "abcdef1234567890",
            request,
            "duplicate-link-correlation",
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(DevelopmentLinkKinds.PullRequest, first.Kind);
        Assert.Equal("pr:24", first.ExternalId);
        Assert.Equal("Synthetic pull request", first.Title);
        Assert.Equal("feature/link-handler", first.Branch);
        Assert.Equal("0123456789abcdef", first.CommitSha);
        Assert.Equal("Open", first.Status);
        Assert.Equal(1, await fixture.Links.CountByFilterAsync());
        Assert.Single(
            fixture.Audit.Entries,
            entry => entry.Action == "WorkItemDevelopmentLinkCreated"
                && entry.CorrelationId == "create-link-correlation");
    }

    [Fact]
    public async Task DeleteWorkItemLinkRejectsStaleVersionThenDeletesAndAudits()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var link = await fixture.Service.CreateWorkItemLinkAsync(
            "abcdef1234567890",
            new CreateWorkItemDevelopmentLinkRequest(
                setup.Mapping.Id,
                DevelopmentLinkKinds.Commit,
                "commit:24",
                "Synthetic commit",
                "https://github.com/acme/repo/commit/0123456789abcdef",
                "feature/delete-link",
                "0123456789abcdef",
                "Pushed"),
            "create-delete-link-correlation",
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.DeleteWorkItemLinkAsync(
                "abcdef1234567890",
                link.Id,
                link.Version + 1,
                "stale-delete-link-correlation",
                CancellationToken.None));
        Assert.NotNull(await fixture.Links.SelectAsync(item => item.Id == link.Id));

        await fixture.Service.DeleteWorkItemLinkAsync(
            "abcdef1234567890",
            link.Id,
            link.Version,
            "delete-link-correlation",
            CancellationToken.None);

        Assert.Null(await fixture.Links.SelectAsync(item => item.Id == link.Id));
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "WorkItemDevelopmentLinkDeleted"
                && entry.CorrelationId == "delete-link-correlation");
    }

    [Fact]
    public async Task DeleteConnectionRejectsStaleVersionThenRemovesOwnedDataAndAudits()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var link = await fixture.Service.CreateWorkItemLinkAsync(
            "abcdef1234567890",
            new CreateWorkItemDevelopmentLinkRequest(
                setup.Mapping.Id,
                DevelopmentLinkKinds.PullRequest,
                "pr:delete-connection",
                "Connection deletion link",
                "https://github.com/acme/repo/pull/31",
                "feature/delete-connection",
                "0123456789abcdef",
                "Open"),
            "create-delete-connection-link",
            CancellationToken.None);
        await fixture.Receipts.CreateAsync(new DevelopmentWebhookReceiptDocument
        {
            Id = "delete-connection-receipt",
            OrganizationId = Fixture.OrganizationId,
            ConnectionId = setup.Connection.Connection.Id,
            DeliveryId = "delete-connection-delivery",
            ExpiresAtUtc = fixture.Clock.UtcNow.AddDays(1).UtcDateTime
        });
        var current = await fixture.Service.GetAsync(
            setup.Connection.Connection.Id,
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.DeleteConnectionAsync(
                current.Id,
                current.Version + 1,
                "stale-delete-connection",
                CancellationToken.None));
        Assert.NotNull(await fixture.Connections.SelectAsync(item => item.Id == current.Id));
        Assert.NotNull(await fixture.Mappings.SelectAsync(item => item.Id == setup.Mapping.Id));
        Assert.NotNull(await fixture.Links.SelectAsync(item => item.Id == link.Id));
        Assert.NotNull(await fixture.Receipts.SelectAsync(
            item => item.Id == "delete-connection-receipt"));

        await fixture.Service.DeleteConnectionAsync(
            current.Id,
            current.Version,
            "delete-connection",
            CancellationToken.None);

        Assert.Null(await fixture.Connections.SelectAsync(item => item.Id == current.Id));
        Assert.Null(await fixture.Mappings.SelectAsync(item => item.Id == setup.Mapping.Id));
        Assert.Null(await fixture.Links.SelectAsync(item => item.Id == link.Id));
        Assert.Null(await fixture.Receipts.SelectAsync(
            item => item.Id == "delete-connection-receipt"));
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "DevelopmentConnectionDeleted"
                && entry.CorrelationId == "delete-connection");
    }

    [Fact]
    public async Task GitHubWebhookRejectsBadSignatureDeduplicatesAndSurvivesHealthVersionChange()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var payload = GitHubPullRequestPayload(
            "open",
            fixture.Clock.UtcNow,
            "Implement PLAT-abcdef12");

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.ReceiveWebhookAsync(
                setup.Connection.Connection.Id,
                new DevelopmentWebhookRequest(
                    "delivery-1",
                    "pull_request",
                    null,
                    "sha256=invalid",
                    payload),
                CancellationToken.None));
        Assert.Equal(0, await fixture.Receipts.CountByFilterAsync());

        var request = GitHubRequest(
            setup.Connection.WebhookSecret,
            "delivery-1",
            payload);
        var accepted = await fixture.Service.ReceiveWebhookAsync(
            setup.Connection.Connection.Id,
            request,
            CancellationToken.None);
        var receipt = await fixture.Receipts.SelectAsync(
            item => item.DeliveryId == "delivery-1");
        Assert.Equal("Accepted", accepted.Status);
        Assert.Equal(DevelopmentWebhookReceiptStatuses.Pending, receipt!.Status);

        var duplicate = await fixture.Service.ReceiveWebhookAsync(
            setup.Connection.Connection.Id,
            request,
            CancellationToken.None);
        Assert.True(duplicate.Duplicate);
        Assert.Single(fixture.Queue.Messages);

        var changedPayload = GitHubPullRequestPayload(
            "closed",
            fixture.Clock.UtcNow,
            "Implement PLAT-abcdef12");
        var collision = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.ReceiveWebhookAsync(
                setup.Connection.Connection.Id,
                GitHubRequest(
                    setup.Connection.WebhookSecret,
                    "delivery-1",
                    changedPayload),
                CancellationToken.None));
        Assert.Equal(
            "DEVELOPMENT_WEBHOOK_DELIVERY_COLLISION",
            collision.Code);

        _ = await fixture.Service.CheckHealthAsync(
            setup.Connection.Connection.Id,
            "health-correlation",
            CancellationToken.None);
        await fixture.Service.ProcessWebhookAsync(
            Assert.Single(fixture.Queue.Messages),
            CancellationToken.None);

        var link = Assert.Single(await fixture.Links.ListByFilterAsync());
        Assert.Equal("Open", link.Status);
        Assert.Equal("Webhook", link.Source);
        Assert.Equal("abcdef1234567890", link.WorkItemId);
        var processedReceipt = await fixture.Receipts.SelectAsync(
            item => item.DeliveryId == "delivery-1");
        Assert.Equal(
            DevelopmentWebhookReceiptStatuses.Applied,
            processedReceipt!.Status);
        Assert.Equal(1, processedReceipt.AppliedLinks);
    }

    [Fact]
    public async Task DisconnectInvalidatesQueuedWebhookAndClearsSecretsAndMappings()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var payload = GitHubPullRequestPayload(
            "open",
            fixture.Clock.UtcNow,
            "Implement PLAT-abcdef12");
        _ = await fixture.Service.ReceiveWebhookAsync(
            setup.Connection.Connection.Id,
            GitHubRequest(
                setup.Connection.WebhookSecret,
                "delivery-disconnect",
                payload),
            CancellationToken.None);

        var current = await fixture.Service.GetAsync(
            setup.Connection.Connection.Id,
            CancellationToken.None);
        _ = await fixture.Service.DisconnectAsync(
            current.Id,
            new DevelopmentVersionRequest(current.Version),
            "disconnect-correlation",
            CancellationToken.None);
        await fixture.Service.ProcessWebhookAsync(
            Assert.Single(fixture.Queue.Messages),
            CancellationToken.None);

        Assert.Equal(0, await fixture.Links.CountByFilterAsync());
        var connection = await fixture.Connections.SelectAsync(
            item => item.Id == current.Id);
        Assert.False(connection!.IsConnected);
        Assert.Empty(connection.CredentialProtected);
        Assert.Empty(connection.WebhookSecretProtected);
        Assert.Equal(2, connection.LifecycleVersion);
        var mapping = await fixture.Mappings.SelectAsync(
            item => item.Id == setup.Mapping.Id);
        Assert.False(mapping!.IsActive);
        var receipt = await fixture.Receipts.SelectAsync(
            item => item.DeliveryId == "delivery-disconnect");
        Assert.Equal(
            DevelopmentWebhookReceiptStatuses.Ignored,
            receipt!.Status);
    }

    [Fact]
    public async Task OlderProviderEventCannotOverwriteNewerLinkState()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var newer = GitHubPullRequestPayload(
            "open",
            fixture.Clock.UtcNow,
            "Implement PLAT-abcdef12");
        _ = await fixture.Service.ReceiveWebhookAsync(
            setup.Connection.Connection.Id,
            GitHubRequest(
                setup.Connection.WebhookSecret,
                "delivery-newer",
                newer),
            CancellationToken.None);
        await fixture.Service.ProcessWebhookAsync(
            fixture.Queue.Messages[0],
            CancellationToken.None);

        var older = GitHubPullRequestPayload(
            "closed",
            fixture.Clock.UtcNow.AddHours(-1),
            "Implement PLAT-abcdef12");
        _ = await fixture.Service.ReceiveWebhookAsync(
            setup.Connection.Connection.Id,
            GitHubRequest(
                setup.Connection.WebhookSecret,
                "delivery-older",
                older),
            CancellationToken.None);
        await fixture.Service.ProcessWebhookAsync(
            fixture.Queue.Messages[1],
            CancellationToken.None);

        var link = Assert.Single(await fixture.Links.ListByFilterAsync());
        Assert.Equal("Open", link.Status);
        Assert.Equal(fixture.Clock.UtcNow, link.LastEventAtUtc);
        var oldReceipt = await fixture.Receipts.SelectAsync(
            item => item.DeliveryId == "delivery-older");
        Assert.Equal(
            DevelopmentWebhookReceiptStatuses.Ignored,
            oldReceipt!.Status);
    }

    [Fact]
    public async Task WebhookRejectsExcessiveReferencesBeforeCreatingReceipt()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var references = string.Join(
            ' ',
            Enumerable.Range(
                    1,
                    DevelopmentIntegrationLimits.MaximumWorkItemReferencesPerEvent + 1)
                .Select(index => $"PLAT-{index:x8}"));
        var payload = GitHubPullRequestPayload(
            "open",
            fixture.Clock.UtcNow,
            references);

        var exception = await Assert.ThrowsAsync<
            DevelopmentWebhookReferenceLimitException>(() =>
            fixture.Service.ReceiveWebhookAsync(
                setup.Connection.Connection.Id,
                GitHubRequest(
                    setup.Connection.WebhookSecret,
                    "delivery-reference-limit",
                    payload),
                CancellationToken.None));

        Assert.Equal(
            "DEVELOPMENT_WEBHOOK_REFERENCE_LIMIT_EXCEEDED",
            exception.Code);
        Assert.DoesNotContain("PLAT-0000000b", exception.Message);
        Assert.Equal(0, await fixture.Receipts.CountByFilterAsync());
        Assert.Empty(fixture.Queue.Messages);
    }

    [Fact]
    public async Task LegacyQueuedWebhookRejectsExcessiveReferencesBeforeLinkMutation()
    {
        var fixture = new Fixture();
        var setup = await fixture.CreateMappedConnectionAsync(
            DevelopmentProviders.GitHub);
        var payload = GitHubPullRequestPayload(
            "open",
            fixture.Clock.UtcNow,
            "Implement PLAT-abcdef12");
        _ = await fixture.Service.ReceiveWebhookAsync(
            setup.Connection.Connection.Id,
            GitHubRequest(
                setup.Connection.WebhookSecret,
                "delivery-legacy-reference-limit",
                payload),
            CancellationToken.None);
        var references = string.Join(
            ' ',
            new[] { "PLAT-abcdef12" }.Concat(
                Enumerable.Range(
                        1,
                        DevelopmentIntegrationLimits.MaximumWorkItemReferencesPerEvent)
                    .Select(index => $"PLAT-{index:x8}")));
        var queued = Assert.Single(fixture.Queue.Messages);
        var legacy = queued with
        {
            Event = queued.Event! with
            {
                ReferenceTexts = [references]
            }
        };

        var exception = await Assert.ThrowsAsync<
            DevelopmentWebhookReferenceLimitException>(() =>
            fixture.Service.ProcessWebhookAsync(
                legacy,
                CancellationToken.None));

        Assert.Equal(
            "DEVELOPMENT_WEBHOOK_REFERENCE_LIMIT_EXCEEDED",
            exception.Code);
        Assert.DoesNotContain("PLAT-abcdef12", exception.Message);
        Assert.Equal(0, await fixture.Links.CountByFilterAsync());
        var receipt = await fixture.Receipts.SelectAsync(
            item => item.DeliveryId == "delivery-legacy-reference-limit");
        Assert.Equal(
            DevelopmentWebhookReceiptStatuses.Pending,
            receipt!.Status);
    }

    [Fact]
    public void GitLabStandardSignatureEnforcesTimestampAndNormalizesMergeRequest()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            29,
            12,
            0,
            0,
            TimeSpan.Zero);
        var key = Encoding.UTF8.GetBytes(
            "synthetic-gitlab-webhook-key-32b");
        var secret = "whsec_" + Convert.ToBase64String(key);
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "object_kind": "merge_request",
              "project": { "id": 42 },
              "object_attributes": {
                "iid": 17,
                "title": "Implement PLAT-abcdef12",
                "description": "Synthetic merge request",
                "source_branch": "feature/PLAT-abcdef12",
                "last_commit": { "id": "0123456789abcdef" },
                "url": "https://gitlab.com/acme/repo/-/merge_requests/17",
                "state": "opened",
                "updated_at": "2026-07-29T12:00:00Z"
              }
            }
            """);
        var deliveryId = "gitlab-delivery-1";
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var signature = GitLabSignature(
            key,
            deliveryId,
            timestamp,
            payload);
        var request = new DevelopmentWebhookRequest(
            deliveryId,
            "Merge Request Hook",
            timestamp,
            "v1,not-valid " + signature,
            payload);

        Assert.True(DevelopmentWebhookSecurity.Verify(
            DevelopmentProviders.GitLab,
            secret,
            request,
            now));
        Assert.False(DevelopmentWebhookSecurity.Verify(
            DevelopmentProviders.GitLab,
            secret,
            request,
            now.AddSeconds(
                DevelopmentIntegrationLimits.ReplayWindowSeconds + 1)));
        var normalized = DevelopmentWebhookSecurity.Normalize(
            DevelopmentProviders.GitLab,
            request.EventName,
            payload);
        Assert.NotNull(normalized);
        Assert.Equal(DevelopmentLinkKinds.PullRequest, normalized.Kind);
        Assert.Equal("mr:17", normalized.ExternalId);
        Assert.Equal("Open", normalized.Status);
        Assert.Contains(
            "Implement PLAT-abcdef12",
            normalized.ReferenceTexts);
    }

    [Fact]
    public async Task RetentionPurgesOnlyExpiredReceipts()
    {
        var fixture = new Fixture();
        await fixture.Receipts.CreateAsync(new DevelopmentWebhookReceiptDocument
        {
            Id = "expired",
            ConnectionId = "connection",
            OrganizationId = Fixture.OrganizationId,
            DeliveryId = "expired",
            ExpiresAtUtc = fixture.Clock.UtcNow.AddSeconds(-1).UtcDateTime
        });
        await fixture.Receipts.CreateAsync(new DevelopmentWebhookReceiptDocument
        {
            Id = "current",
            ConnectionId = "connection",
            OrganizationId = Fixture.OrganizationId,
            DeliveryId = "current",
            ExpiresAtUtc = fixture.Clock.UtcNow.AddDays(1).UtcDateTime
        });
        var retention = new DevelopmentWebhookReceiptRetentionService(
            fixture.Receipts,
            fixture.Clock);

        Assert.Equal(
            1,
            await retention.PurgeExpiredAsync(CancellationToken.None));
        Assert.Null(await fixture.Receipts.SelectAsync(
            item => item.Id == "expired"));
        Assert.NotNull(await fixture.Receipts.SelectAsync(
            item => item.Id == "current"));
    }

    private static DevelopmentWebhookRequest GitHubRequest(
        string secret,
        string deliveryId,
        byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = "sha256=" + Convert
            .ToHexString(hmac.ComputeHash(payload))
            .ToLowerInvariant();
        return new DevelopmentWebhookRequest(
            deliveryId,
            "pull_request",
            null,
            signature,
            payload);
    }

    private static string GitLabSignature(
        byte[] key,
        string deliveryId,
        string timestamp,
        byte[] payload)
    {
        var prefix = Encoding.UTF8.GetBytes(
            $"{deliveryId}.{timestamp}.");
        var message = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
        Buffer.BlockCopy(payload, 0, message, prefix.Length, payload.Length);
        using var hmac = new HMACSHA256(key);
        return "v1," + Convert.ToBase64String(
            hmac.ComputeHash(message));
    }

    private static byte[] GitHubPullRequestPayload(
        string state,
        DateTimeOffset updatedAt,
        string title) =>
        Encoding.UTF8.GetBytes(
            $$"""
            {
              "number": 17,
              "repository": { "id": 42 },
              "pull_request": {
                "title": "{{title}}",
                "body": "Synthetic pull request",
                "html_url": "https://github.com/acme/repo/pull/17",
                "state": "{{state}}",
                "merged": false,
                "updated_at": "{{updatedAt:O}}",
                "head": {
                  "ref": "feature/PLAT-abcdef12",
                  "sha": "0123456789abcdef"
                }
              }
            }
            """);

    private sealed class Fixture
    {
        public const string OrganizationId = "organization-1";
        public const string AccessToken =
            "synthetic-provider-token-123456";

        public InMemoryDocumentRepository<DevelopmentConnectionDocument>
            Connections { get; } = new();
        public InMemoryDocumentRepository<DevelopmentRepositoryMappingDocument>
            Mappings { get; } = new();
        public InMemoryDocumentRepository<WorkItemDevelopmentLinkDocument>
            Links { get; } = new();
        public InMemoryDocumentRepository<DevelopmentWebhookReceiptDocument>
            Receipts { get; } = new();
        public InMemoryDocumentRepository<WorkItemDocument>
            WorkItems { get; } = new();
        public CurrentUser Current { get; } = new();
        public Protector Protector { get; } = new();
        public Queue Queue { get; } = new();
        public Audit Audit { get; } = new();
        public MutableClock Clock { get; } = new();
        public DevelopmentIntegrationService Service { get; }

        public Fixture()
        {
            Service = new DevelopmentIntegrationService(
                Connections,
                Mappings,
                Links,
                Receipts,
                WorkItems,
                Protector,
                new Authorization(),
                new ProjectDirectory(),
                new ProviderGateway(),
                Queue,
                new ProjectPermissions(),
                Audit,
                Clock,
                Current);
        }

        public Task<DevelopmentConnectionReceipt> CreateConnectionAsync(
            string provider) =>
            Service.CreateAsync(
                new CreateDevelopmentConnectionRequest(
                    provider + " connection",
                    provider,
                    string.Empty,
                    AccessToken),
                "create-correlation",
                CancellationToken.None);

        public async Task<Setup> CreateMappedConnectionAsync(
            string provider)
        {
            _ = await WorkItems.CreateAsync(new WorkItemDocument
            {
                Id = "abcdef1234567890",
                ProjectId = "project-1",
                BoardId = "board-1",
                ColumnId = "column-1",
                Title = "Synthetic work item",
                CreatedAt = Clock.UtcNow,
                UpdatedAt = Clock.UtcNow
            });
            var connection = await CreateConnectionAsync(provider);
            var repositoryUrl = provider == DevelopmentProviders.GitHub
                ? "https://github.com/acme/repo"
                : "https://gitlab.com/acme/repo";
            var mapping = await Service.CreateMappingAsync(
                connection.Connection.Id,
                new CreateDevelopmentRepositoryMappingRequest(
                    "project-1",
                    "42",
                    "repo",
                    "acme/repo",
                    repositoryUrl,
                    "main"),
                "mapping-correlation",
                CancellationToken.None);
            return new Setup(connection, mapping);
        }
    }

    private sealed record Setup(
        DevelopmentConnectionReceipt Connection,
        DevelopmentRepositoryMappingResponse Mapping);

    private sealed class Protector : IDevelopmentCredentialProtector
    {
        public string Protect(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        public string Unprotect(string value) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private sealed class Authorization : IDevelopmentIntegrationAuthorization
    {
        public Task EnsureCanManageAsync(
            string organizationId,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ProjectDirectory : IDevelopmentProjectDirectory
    {
        public Task<DevelopmentProjectResource> GetAsync(
            string organizationId,
            string projectId,
            CancellationToken ct) =>
            Task.FromResult(new DevelopmentProjectResource(
                organizationId,
                projectId,
                "PLAT",
                "Platform"));
    }

    private sealed class ProviderGateway : IDevelopmentProviderGateway
    {
        public Task ValidateBaseUrlAsync(
            string provider,
            string baseUrl,
            CancellationToken ct) => Task.CompletedTask;

        public Task<DevelopmentProviderProbeResult> ProbeAsync(
            string provider,
            string baseUrl,
            string accessToken,
            CancellationToken ct) =>
            Task.FromResult(new DevelopmentProviderProbeResult(true, null));

        public Task<DevelopmentProviderRepositoryResult> ListRepositoriesAsync(
            string provider,
            string baseUrl,
            string accessToken,
            int maximumItems,
            CancellationToken ct) =>
            Task.FromResult(new DevelopmentProviderRepositoryResult([], false));
    }

    private sealed class Queue : IDevelopmentWebhookQueue
    {
        public List<DevelopmentWebhookEvent> Messages { get; } = [];

        public Task EnqueueAsync(
            DevelopmentWebhookEvent message,
            CancellationToken ct)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ProjectPermissions : IProjectPermissionChecker
    {
        public Task<ProjectResourceAuthorization> EnsureCanAsync(
            string userId,
            string projectId,
            string permission,
            CancellationToken ct) =>
            Task.FromResult(new ProjectResourceAuthorization(
                projectId,
                Fixture.OrganizationId,
                userId,
                "Developer",
                false));
    }

    private sealed class Audit : IWorkItemAuditPublisher
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(
            string action,
            string entityType,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Entries.Add(new AuditEntry(
                action,
                entityType,
                entityId,
                oldValue,
                newValue,
                correlationId));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEntry(
        string Action,
        string EntityType,
        string EntityId,
        string? OldValue,
        string? NewValue,
        string CorrelationId);

    private sealed class CurrentUser : ICurrentUser
    {
        public string UserIdValue { get; set; } = "owner-1";
        public string OrganizationIdValue { get; set; } =
            Fixture.OrganizationId;
        public string? UserId => UserIdValue;
        public string? OrganizationId => OrganizationIdValue;
        public IReadOnlyCollection<string> Roles => ["OrganizationAdmin"];
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } =
            new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    }
}
