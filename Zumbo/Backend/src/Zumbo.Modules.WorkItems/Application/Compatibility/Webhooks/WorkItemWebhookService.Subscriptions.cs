using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService
{
    public async Task<IReadOnlyList<WebhookSubscriptionResponse>> ListAsync(CancellationToken ct)
        => await listHandler.HandleAsync(new ListWebhookSubscriptionsQuery(), ct);

    public async Task<WebhookSubscriptionResponse> GetAsync(string id, CancellationToken ct) =>
        await getHandler.HandleAsync(new GetWebhookSubscriptionQuery(id), ct);

    public async Task<WebhookSubscriptionResponse> SetActiveAsync(
        string id,
        bool active,
        SetWebhookSubscriptionStateRequest request,
        CancellationToken ct,
        string? correlationId = null)
        => await stateHandler.HandleAsync(
            new SetSubscriptionStateCommand(id, active, request, correlationId),
            ct);

    public async Task<WebhookSubscriptionResponse> UpdateAsync(
        string id,
        UpdateWebhookSubscriptionRequest request,
        CancellationToken ct,
        string? correlationId = null)
        => await updateHandler.HandleAsync(
            new UpdateSubscriptionCommand(id, request, correlationId),
            ct);

    public async Task<WebhookSecretReceipt> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken ct,
        string? correlationId = null)
    {
        return await createHandler.HandleAsync(
            new CreateSubscriptionCommand(request, correlationId),
            ct);
    }

    public async Task<WebhookSecretReceipt> RotateSecretAsync(
        string id,
        RotateWebhookSecretRequest request,
        CancellationToken ct,
        string? correlationId = null)
    {
        return await rotateSecretHandler.HandleAsync(
            new RotateSecretCommand(id, request, correlationId),
            ct);
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

    private static string GenerateSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Fingerprint(string value) => Hash(value)[..16];

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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

    private string RequireOrganization() => currentUser.OrganizationId
        ?? throw new UnauthorizedException("Authenticated organization is required.");

    private static string RequireTarget(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 2048)
            throw new ValidationException("Webhook target URL must contain between 1 and 2048 characters.");
        return normalized;
    }

    private string RequireUser() => currentUser.UserId
        ?? throw new UnauthorizedException("Authenticated user is required.");

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

    private static string SubscriptionAuditValue(WebhookSubscriptionDocument document) =>
        $"{document.Name}|{new Uri(document.TargetUrl).Host}|{document.IsActive}|v{document.SecretVersion}"
        + $"|{string.Join(',', document.EventScopes)}";

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

    private Task WriteAuditAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string? correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            entityType,
            entityId,
            oldValue,
            newValue,
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            ct);
}
