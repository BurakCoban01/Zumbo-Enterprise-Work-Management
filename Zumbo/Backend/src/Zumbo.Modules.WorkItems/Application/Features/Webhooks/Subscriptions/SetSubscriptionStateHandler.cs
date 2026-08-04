using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed class SetSubscriptionStateHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IWebhookAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<WebhookSubscriptionResponse> HandleAsync(
        SetSubscriptionStateCommand command,
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
        var wasActive = document.IsActive;
        document.IsActive = command.Active;
        document.UpdatedAtUtc = clock.UtcNow;
        var updated = await ReplaceAsync(document, command.Request.ExpectedVersion, ct);
        await audit.WriteAsync(
            command.Active
                ? "WebhookSubscriptionEnabled"
                : "WebhookSubscriptionDisabled",
            "WebhookSubscription",
            updated.Id,
            wasActive.ToString(),
            command.Active.ToString(),
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

    private static NotFoundException SubscriptionNotFound() => new(
        "WEBHOOK_SUBSCRIPTION_NOT_FOUND",
        "Webhook subscription was not found.");
}
