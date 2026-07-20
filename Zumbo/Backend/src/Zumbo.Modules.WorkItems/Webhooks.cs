using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemWebhookScopes
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
    [
        "work-item.created",
        "work-item.updated",
        "work-item.moved",
        "work-item.reordered",
        "work-item.archived",
        "work-item.restored"
    ], StringComparer.Ordinal);

    public static string FromEventType(string eventType) => "work-item." + eventType.Trim().ToLowerInvariant();
}

public static class WebhookDeliveryStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Delivered = "Delivered";
    public const string DeadLetter = "DeadLetter";
}

public sealed class WebhookOptions
{
    public bool Enabled { get; init; } = true;
    public bool AllowHttpLoopback { get; init; }
    public int MaximumAttempts { get; init; } = 5;
    public int BaseRetrySeconds { get; init; } = 1;
    public int MaximumRetrySeconds { get; init; } = 60;
    public double RetryJitterRatio { get; init; } = 0.2;
    public int LeaseSeconds { get; init; } = 60;
    public int RequestTimeoutSeconds { get; init; } = 5;
    public int DispatchBatchSize { get; init; } = 50;
    public int DispatcherIntervalSeconds { get; init; } = 1;
    public int RotationOverlapMinutes { get; init; } = 15;
}

public sealed class WebhookSubscriptionDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public List<string> EventScopes { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public string CurrentSecretProtected { get; set; } = string.Empty;
    public string CurrentSecretFingerprint { get; set; } = string.Empty;
    public int SecretVersion { get; set; } = 1;
    public string? PreviousSecretProtected { get; set; }
    public string? PreviousSecretFingerprint { get; set; }
    public int? PreviousSecretVersion { get; set; }
    public DateTimeOffset? PreviousSecretValidUntilUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class WebhookDeliveryDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string SourceEventId { get; set; } = string.Empty;
    public string EventScope { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public string Status { get; set; } = WebhookDeliveryStatuses.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public string? LeaseToken { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed record CreateWebhookSubscriptionRequest(
    string Name,
    string TargetUrl,
    IReadOnlyCollection<string> EventScopes);

public sealed record UpdateWebhookSubscriptionRequest(
    string Name,
    string TargetUrl,
    IReadOnlyCollection<string> EventScopes,
    long ExpectedVersion);

public sealed record WebhookSubscriptionResponse(
    string Id,
    string Name,
    string TargetUrl,
    IReadOnlyCollection<string> EventScopes,
    bool IsActive,
    string SecretFingerprint,
    int SecretVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

public sealed record WebhookSecretReceipt(
    WebhookSubscriptionResponse Subscription,
    string Secret);

public sealed record RotateWebhookSecretRequest(long ExpectedVersion);
public sealed record SetWebhookSubscriptionStateRequest(long ExpectedVersion);

public sealed record WebhookDeliveryResponse(
    string Id,
    string SubscriptionId,
    string EventScope,
    string PayloadSha256,
    string Status,
    int Attempts,
    DateTimeOffset NextAttemptAtUtc,
    string? LastErrorCode,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? DeadLetteredAtUtc,
    DateTimeOffset CreatedAtUtc,
    long Version);

public sealed record WebhookDeliveryPage(
    IReadOnlyList<WebhookDeliveryResponse> Items,
    string? NextCursor);

public sealed record WebhookDeliveryMetrics(
    long Pending,
    long Processing,
    long Delivered,
    long DeadLetter,
    DateTimeOffset? OldestPendingAtUtc,
    DateTimeOffset CapturedAtUtc);

public sealed record WebhookSendRequest(
    string TargetUrl,
    string Payload,
    string DeliveryId,
    long TimestampUnixSeconds,
    int SecretVersion,
    string Signature,
    int? PreviousSecretVersion,
    string? PreviousSignature);

public sealed record WebhookSendResult(int StatusCode);

public interface IWebhookSecretProtector
{
    string Protect(string value);
    string Unprotect(string value);
}

public interface IWebhookTargetPolicy
{
    Task ValidateAsync(string targetUrl, CancellationToken ct);
}

public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken ct);
}

public interface IWebhookAuthorization
{
    Task EnsureCanManageAsync(string organizationId, CancellationToken ct);
}

public sealed class WebhookDeliveryException(string safeCode) : Exception(safeCode)
{
    public string SafeCode { get; } = safeCode;
}

public sealed class WorkItemWebhookService(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookSecretProtector secretProtector,
    IWebhookTargetPolicy targetPolicy,
    IWebhookSender sender,
    IWebhookAuthorization authorization,
    IOptions<WebhookOptions> options,
    IClock clock,
    ICurrentUser currentUser,
    IDurableMessageJitter? retryJitter = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookSecretReceipt> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var rawSecret = GenerateSecret();
        var now = clock.UtcNow;
        var targetUrl = RequireTarget(request.TargetUrl);
        await targetPolicy.ValidateAsync(targetUrl, ct);
        var document = await subscriptions.CreateAsync(new WebhookSubscriptionDocument
        {
            OrganizationId = organizationId,
            Name = RequireName(request.Name),
            TargetUrl = targetUrl,
            EventScopes = NormalizeScopes(request.EventScopes),
            CurrentSecretProtected = secretProtector.Protect(rawSecret),
            CurrentSecretFingerprint = Fingerprint(rawSecret),
            CreatedByUserId = RequireUser(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        return new WebhookSecretReceipt(ToResponse(document), rawSecret);
    }

    public async Task<IReadOnlyList<WebhookSubscriptionResponse>> ListAsync(CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var result = new List<WebhookSubscriptionResponse>();
        string? cursor = null;
        do
        {
            var page = await subscriptions.ListByCursorAsync(
                x => x.OrganizationId == organizationId, cursor, 200, ct);
            result.AddRange(page.Items.Select(ToResponse));
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

    public async Task<WebhookSubscriptionResponse> GetAsync(string id, CancellationToken ct) =>
        ToResponse(await FindOwnedAsync(id, ct));

    public async Task<WebhookSubscriptionResponse> UpdateAsync(
        string id,
        UpdateWebhookSubscriptionRequest request,
        CancellationToken ct)
    {
        var document = await FindOwnedAsync(id, ct);
        var targetUrl = RequireTarget(request.TargetUrl);
        await targetPolicy.ValidateAsync(targetUrl, ct);
        document.Name = RequireName(request.Name);
        document.TargetUrl = targetUrl;
        document.EventScopes = NormalizeScopes(request.EventScopes);
        document.UpdatedAtUtc = clock.UtcNow;
        return ToResponse(await ReplaceAsync(document, request.ExpectedVersion, ct));
    }

    public async Task<WebhookSecretReceipt> RotateSecretAsync(
        string id,
        RotateWebhookSecretRequest request,
        CancellationToken ct)
    {
        var document = await FindOwnedAsync(id, ct);
        var rawSecret = GenerateSecret();
        document.PreviousSecretProtected = document.CurrentSecretProtected;
        document.PreviousSecretFingerprint = document.CurrentSecretFingerprint;
        document.PreviousSecretVersion = document.SecretVersion;
        document.PreviousSecretValidUntilUtc = clock.UtcNow.AddMinutes(
            Math.Clamp(options.Value.RotationOverlapMinutes, 1, 1440));
        document.CurrentSecretProtected = secretProtector.Protect(rawSecret);
        document.CurrentSecretFingerprint = Fingerprint(rawSecret);
        document.SecretVersion++;
        document.UpdatedAtUtc = clock.UtcNow;
        var updated = await ReplaceAsync(document, request.ExpectedVersion, ct);
        return new WebhookSecretReceipt(ToResponse(updated), rawSecret);
    }

    public async Task<WebhookSubscriptionResponse> SetActiveAsync(
        string id,
        bool active,
        SetWebhookSubscriptionStateRequest request,
        CancellationToken ct)
    {
        var document = await FindOwnedAsync(id, ct);
        document.IsActive = active;
        document.UpdatedAtUtc = clock.UtcNow;
        return ToResponse(await ReplaceAsync(document, request.ExpectedVersion, ct));
    }

    public async Task QueueAsync(
        string sourceEventId,
        string organizationId,
        WorkItemWebhookEvent message,
        CancellationToken ct)
    {
        var scope = WorkItemWebhookScopes.FromEventType(message.EventType);
        if (!WorkItemWebhookScopes.All.Contains(scope)) return;
        var candidates = await ListActiveSubscriptionsAsync(organizationId, ct);
        foreach (var subscription in candidates.Where(x => x.EventScopes.Contains(scope, StringComparer.Ordinal)))
        {
            var id = Hash($"{subscription.Id}:{sourceEventId}");
            var payload = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                specVersion = "1.0",
                id,
                type = scope,
                source = "zumbo/work-items",
                subject = $"work-items/{message.WorkItemId}",
                time = message.OccurredAtUtc,
                tenantId = organizationId,
                correlationId = message.CorrelationId,
                data = new
                {
                    message.WorkItemId,
                    message.ProjectId,
                    message.BoardId,
                    message.WorkItem,
                    message.ResourceVersion
                }
            }, JsonOptions);
            try
            {
                var now = clock.UtcNow;
                await deliveries.CreateAsync(new WebhookDeliveryDocument
                {
                    Id = id,
                    OrganizationId = organizationId,
                    SubscriptionId = subscription.Id,
                    SourceEventId = sourceEventId,
                    EventScope = scope,
                    TargetUrl = subscription.TargetUrl,
                    Payload = payload,
                    PayloadSha256 = Hash(payload),
                    NextAttemptAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }, ct);
            }
            catch (DocumentConflictException)
            {
                // The durable consumer may replay; the deterministic delivery id makes queueing idempotent.
            }
        }
    }

    public async Task<int> DispatchAsync(int batchSize, string workerId, CancellationToken ct)
    {
        if (!options.Value.Enabled) return 0;
        var now = clock.UtcNow;
        var candidates = await deliveries.ListByFilterAsync(
            x => (x.Status == WebhookDeliveryStatuses.Pending && x.NextAttemptAtUtc <= now)
                || (x.Status == WebhookDeliveryStatuses.Processing && x.LeaseUntilUtc <= now),
            x => x.NextAttemptAtUtc,
            pageSize: Math.Clamp(batchSize, 1, 100),
            cancellationToken: ct);
        var delivered = 0;
        foreach (var candidate in candidates)
        {
            var token = Guid.NewGuid().ToString("N");
            candidate.Status = WebhookDeliveryStatuses.Processing;
            candidate.LeaseToken = token;
            candidate.ClaimedBy = workerId;
            candidate.LeaseUntilUtc = now.AddSeconds(Math.Clamp(options.Value.LeaseSeconds, 5, 900));
            candidate.UpdatedAtUtc = now;
            var claim = await deliveries.ReplaceByFilterAsync(
                x => x.Id == candidate.Id
                    && ((x.Status == WebhookDeliveryStatuses.Pending && x.NextAttemptAtUtc <= now)
                        || (x.Status == WebhookDeliveryStatuses.Processing && x.LeaseUntilUtc <= now)),
                candidate,
                ct);
            if (claim.MatchedCount != 1) continue;

            try
            {
                var subscription = await subscriptions.SelectAsync(
                    x => x.Id == candidate.SubscriptionId
                        && x.OrganizationId == candidate.OrganizationId
                        && x.IsActive,
                    ct) ?? throw new WebhookDeliveryException("SUBSCRIPTION_UNAVAILABLE");
                await targetPolicy.ValidateAsync(candidate.TargetUrl, ct);
                var timestamp = clock.UtcNow.ToUnixTimeSeconds();
                var signature = Sign(secretProtector.Unprotect(subscription.CurrentSecretProtected), timestamp, candidate.Payload);
                string? previousSignature = null;
                int? previousVersion = null;
                if (subscription.PreviousSecretValidUntilUtc > clock.UtcNow
                    && subscription.PreviousSecretProtected is not null)
                {
                    previousVersion = subscription.PreviousSecretVersion;
                    previousSignature = Sign(
                        secretProtector.Unprotect(subscription.PreviousSecretProtected),
                        timestamp,
                        candidate.Payload);
                }

                await sender.SendAsync(new WebhookSendRequest(
                    candidate.TargetUrl,
                    candidate.Payload,
                    candidate.Id,
                    timestamp,
                    subscription.SecretVersion,
                    signature,
                    previousVersion,
                    previousSignature), ct);
                candidate.Status = WebhookDeliveryStatuses.Delivered;
                candidate.DeliveredAtUtc = clock.UtcNow;
                candidate.LastErrorCode = null;
                candidate.UpdatedAtUtc = clock.UtcNow;
                ClearLease(candidate);
                var result = await deliveries.ReplaceByFilterAsync(
                    x => x.Id == candidate.Id
                        && x.Status == WebhookDeliveryStatuses.Processing
                        && x.LeaseToken == token,
                    candidate,
                    ct);
                if (result.MatchedCount == 1) delivered++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await FailAsync(candidate, token, exception, ct);
            }
        }
        return delivered;
    }

    public async Task<WebhookDeliveryPage> ListDeliveriesAsync(
        string subscriptionId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        await FindOwnedAsync(subscriptionId, ct);
        var organizationId = RequireOrganization();
        var page = await deliveries.ListByCursorAsync(
            x => x.OrganizationId == organizationId && x.SubscriptionId == subscriptionId,
            string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim(),
            Math.Clamp(pageSize, 1, 100),
            ct);
        return new WebhookDeliveryPage(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<WebhookDeliveryResponse> GetDeliveryAsync(string id, CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var delivery = await deliveries.SelectAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            ct) ?? throw DeliveryNotFound();
        return ToResponse(delivery);
    }

    public async Task<WebhookDeliveryResponse> ReplayAsync(string id, CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var delivery = await deliveries.SelectAsync(
            x => x.Id == id
                && x.OrganizationId == organizationId
                && x.Status == WebhookDeliveryStatuses.DeadLetter,
            ct) ?? throw DeliveryNotFound();
        delivery.Status = WebhookDeliveryStatuses.Pending;
        delivery.Attempts = 0;
        delivery.NextAttemptAtUtc = clock.UtcNow;
        delivery.LastErrorCode = null;
        delivery.DeadLetteredAtUtc = null;
        delivery.UpdatedAtUtc = clock.UtcNow;
        ClearLease(delivery);
        var result = await deliveries.ReplaceByFilterAsync(
            x => x.Id == id
                && x.OrganizationId == organizationId
                && x.Status == WebhookDeliveryStatuses.DeadLetter,
            delivery,
            ct);
        if (result.MatchedCount != 1) throw new ConflictException(
            "WEBHOOK_DELIVERY_CONFLICT", "Webhook delivery changed concurrently; retry the operation.");
        return ToResponse(delivery);
    }

    public async Task<WebhookDeliveryMetrics> GetMetricsAsync(CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var pending = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Pending, ct);
        var processing = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Processing, ct);
        var delivered = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Delivered, ct);
        var deadLetter = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.DeadLetter, ct);
        var oldest = (await deliveries.ListByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Pending,
            x => x.NextAttemptAtUtc,
            pageSize: 1,
            cancellationToken: ct)).SingleOrDefault();
        return new WebhookDeliveryMetrics(
            pending, processing, delivered, deadLetter, oldest?.NextAttemptAtUtc, clock.UtcNow);
    }

    public static string Sign(string secret, long timestampUnixSeconds, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{timestampUnixSeconds}.{payload}"))).ToLowerInvariant();
    }

    private async Task FailAsync(
        WebhookDeliveryDocument delivery,
        string leaseToken,
        Exception exception,
        CancellationToken ct)
    {
        delivery.Attempts++;
        delivery.LastErrorCode = exception is WebhookDeliveryException known
            ? known.SafeCode
            : "DELIVERY_FAILED";
        delivery.UpdatedAtUtc = clock.UtcNow;
        if (delivery.Attempts >= Math.Clamp(options.Value.MaximumAttempts, 1, 20))
        {
            delivery.Status = WebhookDeliveryStatuses.DeadLetter;
            delivery.DeadLetteredAtUtc = clock.UtcNow;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatuses.Pending;
            delivery.NextAttemptAtUtc = clock.UtcNow.Add(RetryDelay(delivery.Attempts));
        }
        ClearLease(delivery);
        await deliveries.ReplaceByFilterAsync(
            x => x.Id == delivery.Id
                && x.Status == WebhookDeliveryStatuses.Processing
                && x.LeaseToken == leaseToken,
            delivery,
            ct);
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Clamp(options.Value.BaseRetrySeconds, 1, 3600));
        var maximumDelay = TimeSpan.FromSeconds(Math.Clamp(options.Value.MaximumRetrySeconds, 1, 86_400));
        if (retryJitter is not null)
        {
            return new DurableMessageRetryPolicy(
                baseDelay,
                maximumDelay,
                Math.Clamp(options.Value.RetryJitterRatio, 0, 1),
                retryJitter).DelayForAttempt(attempt);
        }

        var exponent = Math.Min(attempt - 1, 20);
        return TimeSpan.FromSeconds(Math.Min(
            baseDelay.TotalSeconds * Math.Pow(2, exponent),
            maximumDelay.TotalSeconds));
    }

    private async Task<IReadOnlyList<WebhookSubscriptionDocument>> ListActiveSubscriptionsAsync(
        string organizationId,
        CancellationToken ct)
    {
        var result = new List<WebhookSubscriptionDocument>();
        string? cursor = null;
        do
        {
            var page = await subscriptions.ListByCursorAsync(
                x => x.OrganizationId == organizationId && x.IsActive,
                cursor,
                200,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

    private async Task<WebhookSubscriptionDocument> FindOwnedAsync(string id, CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await subscriptions.SelectAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "WEBHOOK_SUBSCRIPTION_NOT_FOUND", "Webhook subscription was not found.");
    }

    private async Task<WebhookSubscriptionDocument> ReplaceAsync(
        WebhookSubscriptionDocument document,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await subscriptions.ReplaceByVersionAsync(
                x => x.Id == document.Id && x.OrganizationId == document.OrganizationId,
                document,
                expectedVersion,
                ct);
            if (!result.Found) throw new NotFoundException(
                "WEBHOOK_SUBSCRIPTION_NOT_FOUND", "Webhook subscription was not found.");
            document.Version = result.Version!.Value;
            return document;
        }
        catch (DocumentConcurrencyException)
        {
            throw new ConflictException(
                "WEBHOOK_SUBSCRIPTION_CONFLICT", "Webhook subscription changed concurrently; refresh and retry.");
        }
    }

    private static List<string> NormalizeScopes(IReadOnlyCollection<string>? scopes)
    {
        if (scopes is null || scopes.Count == 0)
            throw new ValidationException("At least one webhook event scope is required.");
        var normalized = scopes.Select(x => x?.Trim().ToLowerInvariant() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (normalized.Any(x => !WorkItemWebhookScopes.All.Contains(x)))
            throw new ValidationException("One or more webhook event scopes are not supported.");
        return normalized;
    }

    private static string RequireName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 100)
            throw new ValidationException("Webhook name must contain between 1 and 100 characters.");
        return normalized;
    }

    private static string RequireTarget(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 2048)
            throw new ValidationException("Webhook target URL must contain between 1 and 2048 characters.");
        return normalized;
    }

    private string RequireOrganization() => currentUser.OrganizationId
        ?? throw new UnauthorizedException("Authenticated organization is required.");

    private string RequireUser() => currentUser.UserId
        ?? throw new UnauthorizedException("Authenticated user is required.");

    private static string GenerateSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Fingerprint(string value) => Hash(value)[..16];

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void ClearLease(WebhookDeliveryDocument document)
    {
        document.LeaseToken = null;
        document.ClaimedBy = null;
        document.LeaseUntilUtc = null;
    }

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscriptionDocument document) => new(
        document.Id,
        document.Name,
        document.TargetUrl,
        document.EventScopes,
        document.IsActive,
        document.CurrentSecretFingerprint,
        document.SecretVersion,
        document.CreatedAtUtc,
        document.UpdatedAtUtc,
        document.Version);

    private static WebhookDeliveryResponse ToResponse(WebhookDeliveryDocument document) => new(
        document.Id,
        document.SubscriptionId,
        document.EventScope,
        document.PayloadSha256,
        document.Status,
        document.Attempts,
        document.NextAttemptAtUtc,
        document.LastErrorCode,
        document.DeliveredAtUtc,
        document.DeadLetteredAtUtc,
        document.CreatedAtUtc,
        document.Version);

    private static NotFoundException DeliveryNotFound() => new(
        "WEBHOOK_DELIVERY_NOT_FOUND", "Webhook delivery was not found.");
}
