using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class WebhookServiceTests
{
    [Fact]
    public async Task Create_returns_secret_once_without_persisting_plaintext_and_is_tenant_scoped()
    {
        var context = CreateContext();
        var receipt = await context.Service.CreateAsync(new(
            "Delivery hook",
            "https://receiver.example.test/events",
            ["work-item.created", "work-item.updated"]), default, "create-correlation");

        var stored = await context.Subscriptions.SelectAsync(x => x.Id == receipt.Subscription.Id);
        Assert.NotNull(stored);
        Assert.StartsWith("whsec_", receipt.Secret);
        Assert.DoesNotContain(receipt.Secret, JsonSerializer.Serialize(stored), StringComparison.Ordinal);
        Assert.NotEqual(receipt.Secret, stored.CurrentSecretProtected);
        Assert.Equal(receipt.Secret, context.Protector.Unprotect(stored.CurrentSecretProtected));
        var createdAudit = Assert.Single(context.Audit.Entries);
        Assert.Equal("WebhookSubscriptionCreated", createdAudit.Action);
        Assert.Equal("create-correlation", createdAudit.CorrelationId);
        Assert.DoesNotContain(receipt.Secret, createdAudit.NewValue, StringComparison.Ordinal);

        context.User.OrganizationId = "another-tenant";
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.Service.GetAsync(receipt.Subscription.Id, default));
        Assert.Equal("WEBHOOK_SUBSCRIPTION_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task Create_rejects_unknown_or_empty_scope()
    {
        var context = CreateContext();
        await Assert.ThrowsAsync<ValidationException>(() => context.Service.CreateAsync(new(
            "Invalid",
            "https://receiver.example.test/events",
            ["work-item.*"]), default));
        await Assert.ThrowsAsync<ValidationException>(() => context.Service.CreateAsync(new(
            "Invalid",
            "https://receiver.example.test/events",
            []), default));
    }

    [Fact]
    public async Task Queue_is_scope_filtered_and_idempotent_with_immutable_versioned_payload()
    {
        var context = CreateContext();
        await context.Service.CreateAsync(new(
            "Created only",
            "https://receiver.example.test/events",
            ["work-item.created"]), default);
        var created = Event("created");

        await context.Service.QueueAsync("source-1", "tenant-1", created, default);
        await context.Service.QueueAsync("source-1", "tenant-1", created, default);
        await context.Service.QueueAsync("source-2", "tenant-1", Event("moved"), default);

        var records = await context.Deliveries.ListByFilterAsync();
        var delivery = Assert.Single(records);
        using var payload = JsonDocument.Parse(delivery.Payload);
        Assert.Equal(1, payload.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("1.0", payload.RootElement.GetProperty("specVersion").GetString());
        Assert.Equal("work-item.created", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal("tenant-1", payload.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal(delivery.Id, payload.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Rotation_sends_current_and_previous_valid_signatures()
    {
        var context = CreateContext();
        var original = await context.Service.CreateAsync(new(
            "Rotation",
            "https://receiver.example.test/events",
            ["work-item.updated"]), default);
        var rotated = await context.Service.RotateSecretAsync(
            original.Subscription.Id,
            new(original.Subscription.Version),
            default);
        await context.Service.QueueAsync("source-rotation", "tenant-1", Event("updated"), default);

        Assert.Equal(1, await context.Service.DispatchAsync(10, "worker", default));
        var send = Assert.Single(context.Sender.Requests);
        Assert.Equal(2, send.SecretVersion);
        Assert.Equal(1, send.PreviousSecretVersion);
        Assert.Equal(
            WorkItemWebhookService.Sign(rotated.Secret, send.TimestampUnixSeconds, send.Payload),
            send.Signature);
        Assert.Equal(
            WorkItemWebhookService.Sign(original.Secret, send.TimestampUnixSeconds, send.Payload),
            send.PreviousSignature);
        Assert.Contains(context.Audit.Entries, x =>
            x.Action == "WebhookSecretRotated"
            && x.OldValue == original.Subscription.SecretFingerprint
            && x.NewValue == rotated.Subscription.SecretFingerprint);
        Assert.DoesNotContain(
            context.Audit.Entries,
            x => string.Equals(x.NewValue, rotated.Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Test_delivery_is_safe_signed_and_requires_an_active_subscription()
    {
        var context = CreateContext();
        var receipt = await context.Service.CreateAsync(new(
            "Health check",
            "https://receiver.example.test/events?private=ignored",
            ["work-item.created"]), default, "create-test");

        var queued = await context.Service.QueueTestDeliveryAsync(
            receipt.Subscription.Id,
            default,
            "test-correlation");

        Assert.Equal(WebhookDeliveryStatuses.Pending, queued.Status);
        Assert.Equal("webhook.test", queued.EventScope);
        var stored = await context.Deliveries.SelectAsync(x => x.Id == queued.Id);
        Assert.NotNull(stored);
        using (var payload = JsonDocument.Parse(stored.Payload))
        {
            Assert.Equal("webhook.test", payload.RootElement.GetProperty("type").GetString());
            Assert.True(payload.RootElement.GetProperty("data").GetProperty("test").GetBoolean());
            Assert.False(payload.RootElement.TryGetProperty("workItem", out _));
        }

        Assert.Equal(1, await context.Service.DispatchAsync(10, "test-worker", default));
        var send = Assert.Single(context.Sender.Requests);
        Assert.Equal(
            WorkItemWebhookService.Sign(receipt.Secret, send.TimestampUnixSeconds, send.Payload),
            send.Signature);
        var queuedAudit = Assert.Single(context.Audit.Entries, x => x.Action == "WebhookTestDeliveryQueued");
        Assert.Equal("test-correlation", queuedAudit.CorrelationId);
        Assert.DoesNotContain("private=ignored", JsonSerializer.Serialize(context.Audit.Entries));

        var disabled = await context.Service.SetActiveAsync(
            receipt.Subscription.Id,
            false,
            new(receipt.Subscription.Version),
            default,
            "disable-correlation");
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => context.Service.QueueTestDeliveryAsync(receipt.Subscription.Id, default));
        Assert.Equal("WEBHOOK_SUBSCRIPTION_DISABLED", exception.Code);
        Assert.Contains(context.Audit.Entries, x =>
            x.Action == "WebhookSubscriptionDisabled"
            && x.OldValue == bool.TrueString
            && x.NewValue == bool.FalseString);
        Assert.False(disabled.IsActive);
    }

    [Fact]
    public async Task Receiver_failures_retry_then_dead_letter_and_replay_same_payload()
    {
        var context = CreateContext(maximumAttempts: 2);
        var subscription = await context.Service.CreateAsync(new(
            "Failure",
            "https://receiver.example.test/events",
            ["work-item.created"]), default);
        await context.Service.QueueAsync("source-failure", "tenant-1", Event("created"), default);

        context.Sender.Fail = true;
        Assert.Equal(0, await context.Service.DispatchAsync(10, "worker-a", default));
        var first = Assert.Single(await context.Deliveries.ListByFilterAsync());
        var originalPayload = first.Payload;
        var originalHash = first.PayloadSha256;
        Assert.Equal(WebhookDeliveryStatuses.Pending, first.Status);
        Assert.Equal(1, first.Attempts);

        context.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await context.Service.DispatchAsync(10, "worker-b", default));
        var deadLetter = Assert.Single(await context.Deliveries.ListByFilterAsync());
        Assert.Equal(WebhookDeliveryStatuses.DeadLetter, deadLetter.Status);
        Assert.Equal(2, deadLetter.Attempts);
        Assert.Equal("RECEIVER_FAILURE", deadLetter.LastErrorCode);

        var replayed = await context.Service.ReplayAsync(deadLetter.Id, default);
        Assert.Equal(WebhookDeliveryStatuses.Pending, replayed.Status);
        context.Sender.Fail = false;
        Assert.Equal(1, await context.Service.DispatchAsync(10, "worker-c", default));
        var delivered = Assert.Single(await context.Deliveries.ListByFilterAsync());
        Assert.Equal(WebhookDeliveryStatuses.Delivered, delivered.Status);
        Assert.Equal(originalPayload, delivered.Payload);
        Assert.Equal(originalHash, delivered.PayloadSha256);
        Assert.All(context.Sender.Requests, request => Assert.Equal(originalPayload, request.Payload));

        context.User.OrganizationId = "another-tenant";
        await Assert.ThrowsAsync<NotFoundException>(
            () => context.Service.ListDeliveriesAsync(subscription.Subscription.Id, null, 20, default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => context.Service.GetDeliveryAsync(delivered.Id, default));
    }

    private static WorkItemWebhookEvent Event(string type) => new(
        type,
        "work-item-1",
        "project-1",
        "correlation-1",
        new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero),
        "board-1",
        new WorkItemRealtimeItem(
            "work-item-1", "project-1", "board-1", "column-1", "Title", "Task", "High",
            "Open", null, null, null, 3, null, 1000, 7),
        7);

    private static TestContext CreateContext(int maximumAttempts = 5)
    {
        var subscriptions = new InMemoryDocumentRepository<WebhookSubscriptionDocument>();
        var deliveries = new InMemoryDocumentRepository<WebhookDeliveryDocument>();
        var protector = new TestProtector();
        var sender = new TestSender();
        var audit = new TestAuditPublisher();
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));
        var user = new MutableCurrentUser { UserId = "user-1", OrganizationId = "tenant-1" };
        var service = new WorkItemWebhookService(
            subscriptions,
            deliveries,
            protector,
            new AllowTargetPolicy(),
            sender,
            new AllowAuthorization(),
            audit,
            Options.Create(new WebhookOptions
            {
                MaximumAttempts = maximumAttempts,
                BaseRetrySeconds = 1,
                MaximumRetrySeconds = 8,
                LeaseSeconds = 30,
                RotationOverlapMinutes = 15
            }),
            clock,
            user);
        return new TestContext(service, subscriptions, deliveries, protector, sender, audit, clock, user);
    }

    private sealed record TestContext(
        WorkItemWebhookService Service,
        InMemoryDocumentRepository<WebhookSubscriptionDocument> Subscriptions,
        InMemoryDocumentRepository<WebhookDeliveryDocument> Deliveries,
        TestProtector Protector,
        TestSender Sender,
        TestAuditPublisher Audit,
        MutableClock Clock,
        MutableCurrentUser User);

    private sealed class TestProtector : IWebhookSecretProtector
    {
        public string Protect(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        public string Unprotect(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private sealed class AllowTargetPolicy : IWebhookTargetPolicy
    {
        public Task ValidateAsync(string targetUrl, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AllowAuthorization : IWebhookAuthorization
    {
        public Task EnsureCanManageAsync(string organizationId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestAuditPublisher : IWorkItemAuditPublisher
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
            Entries.Add(new(action, entityType, entityId, oldValue, newValue, correlationId));
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

    private sealed class TestSender : IWebhookSender
    {
        public bool Fail { get; set; }
        public List<WebhookSendRequest> Requests { get; } = [];

        public Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (Fail) throw new WebhookDeliveryException("RECEIVER_FAILURE");
            return Task.FromResult(new WebhookSendResult(204));
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class MutableCurrentUser : ICurrentUser
    {
        public string? UserId { get; set; }
        public string? OrganizationId { get; set; }
        public IReadOnlyCollection<string> Roles { get; } = ["OrganizationAdmin"];
    }
}
