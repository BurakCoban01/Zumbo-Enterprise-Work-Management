using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed class UpdateSubscriptionHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IWebhookTargetPolicy targetPolicy,
    IWebhookAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<WebhookSubscriptionResponse> HandleAsync(
        UpdateSubscriptionCommand command,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var document = await subscriptions.SelectAsync(
            item => item.Id == command.SubscriptionId
                && item.OrganizationId == organizationId,
            ct) ?? throw SubscriptionNotFound();
        var oldValue = SubscriptionAuditValue(document);
        var targetUrl = RequireTarget(command.Request.TargetUrl);
        await targetPolicy.ValidateAsync(targetUrl, ct);
        document.Name = RequireName(command.Request.Name);
        document.TargetUrl = targetUrl;
        document.EventScopes = NormalizeScopes(command.Request.EventScopes);
        document.UpdatedAtUtc = clock.UtcNow;
        var updated = await ReplaceAsync(document, command.Request.ExpectedVersion, ct);
        await audit.WriteAsync(
            "WebhookSubscriptionUpdated",
            "WebhookSubscription",
            updated.Id,
            oldValue,
            SubscriptionAuditValue(updated),
            string.IsNullOrWhiteSpace(command.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : command.CorrelationId,
            ct);
        return SubscriptionResponseMapper.ToResponse(updated);
    }

    private async Task<WebhookSubscriptionDocument> ReplaceAsync(
        WebhookSubscriptionDocument document,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await subscriptions.ReplaceByVersionAsync(
                item => item.Id == document.Id
                    && item.OrganizationId == document.OrganizationId,
                document,
                expectedVersion,
                ct);
            if (!result.Found)
            {
                throw SubscriptionNotFound();
            }

            document.Version = result.Version!.Value;
            return document;
        }
        catch (DocumentConcurrencyException)
        {
            throw new ConflictException(
                "WEBHOOK_SUBSCRIPTION_CONFLICT",
                "Webhook subscription changed concurrently; refresh and retry.");
        }
    }

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

    private static NotFoundException SubscriptionNotFound() => new(
        "WEBHOOK_SUBSCRIPTION_NOT_FOUND",
        "Webhook subscription was not found.");
}
