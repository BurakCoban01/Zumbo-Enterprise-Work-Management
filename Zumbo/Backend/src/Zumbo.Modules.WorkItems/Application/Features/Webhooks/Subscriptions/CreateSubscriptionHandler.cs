using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed class CreateSubscriptionHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IWebhookSecretProtector secretProtector,
    IWebhookTargetPolicy targetPolicy,
    IWebhookAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<WebhookSecretReceipt> HandleAsync(
        CreateSubscriptionCommand command,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var rawSecret = GenerateSecret();
        var now = clock.UtcNow;
        var targetUrl = RequireTarget(command.Request.TargetUrl);
        await targetPolicy.ValidateAsync(targetUrl, ct);
        var document = await subscriptions.CreateAsync(new WebhookSubscriptionDocument
        {
            OrganizationId = organizationId,
            Name = RequireName(command.Request.Name),
            TargetUrl = targetUrl,
            EventScopes = NormalizeScopes(command.Request.EventScopes),
            CurrentSecretProtected = secretProtector.Protect(rawSecret),
            CurrentSecretFingerprint = Fingerprint(rawSecret),
            CreatedByUserId = currentUser.UserId
                ?? throw new UnauthorizedException(
                    "Authenticated user is required."),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await audit.WriteAsync(
            "WebhookSubscriptionCreated",
            "WebhookSubscription",
            document.Id,
            null,
            SubscriptionAuditValue(document),
            string.IsNullOrWhiteSpace(command.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : command.CorrelationId,
            ct);
        return new WebhookSecretReceipt(
            SubscriptionResponseMapper.ToResponse(document),
            rawSecret);
    }

    private static string GenerateSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Fingerprint(string value) => Hash(value)[..16];

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RequireName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 100)
        {
            throw new ValidationException(
                "Webhook name must contain between 1 and 100 characters.");
        }

        return normalized;
    }

    private static string RequireTarget(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 2048)
        {
            throw new ValidationException(
                "Webhook target URL must contain between 1 and 2048 characters.");
        }

        return normalized;
    }

    private static List<string> NormalizeScopes(IReadOnlyCollection<string>? scopes)
    {
        if (scopes is null || scopes.Count == 0)
        {
            throw new ValidationException(
                "At least one webhook event scope is required.");
        }

        var normalized = scopes
            .Select(item => item?.Trim().ToLowerInvariant() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (normalized.Any(item => !WorkItemWebhookScopes.All.Contains(item)))
        {
            throw new ValidationException(
                "One or more webhook event scopes are not supported.");
        }

        return normalized;
    }

    private static string SubscriptionAuditValue(WebhookSubscriptionDocument document) =>
        $"{document.Name}|{new Uri(document.TargetUrl).Host}|{document.IsActive}|v{document.SecretVersion}"
        + $"|{string.Join(',', document.EventScopes)}";
}
