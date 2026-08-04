using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;

public sealed class ProcessWebhookHandler(
    IDocumentRepository<DevelopmentWebhookReceiptDocument> receipts,
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    ApplyWebhookLinksHandler applyWebhookLinks,
    IWorkItemAuditPublisher audit)
{
    public async Task HandleAsync(
        ProcessWebhookCommand command,
        CancellationToken ct)
    {
        var message = command.Message;
        var receipt = await receipts.SelectAsync(
            item => item.Id == message.ReceiptId
                && item.ConnectionId == message.ConnectionId
                && item.OrganizationId == message.OrganizationId,
            ct);
        if (receipt is null || receipt.Status == DevelopmentWebhookReceiptStatuses.Applied)
        {
            return;
        }

        var connection = await connections.SelectAsync(
            item => item.Id == message.ConnectionId
                && item.OrganizationId == message.OrganizationId
                && item.IsConnected,
            ct);
        if (connection is null
            || connection.LifecycleVersion != message.ConnectionLifecycleVersion)
        {
            receipt.Status = DevelopmentWebhookReceiptStatuses.Ignored;
            await ReplaceReceiptAsync(receipt, ct);
            return;
        }

        if (message.Event is null)
        {
            receipt.Status = DevelopmentWebhookReceiptStatuses.Ignored;
            await ReplaceReceiptAsync(receipt, ct);
            return;
        }

        var mapping = await mappings.SelectAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id
                && item.ExternalRepositoryId == message.Event.RepositoryExternalId
                && item.IsActive,
            ct);
        if (mapping is null)
        {
            receipt.Status = DevelopmentWebhookReceiptStatuses.Ignored;
            await ReplaceReceiptAsync(receipt, ct);
            return;
        }

        var normalizedEvent = ApplyWebhookLinksHandler.NormalizeProviderEvent(
            mapping,
            message.Event);
        var applied = await applyWebhookLinks.HandleAsync(
            connection,
            mapping,
            normalizedEvent,
            message.DeliveryId,
            ct);
        receipt.Status = applied > 0
            ? DevelopmentWebhookReceiptStatuses.Applied
            : DevelopmentWebhookReceiptStatuses.Ignored;
        receipt.AppliedLinks = applied;
        await ReplaceReceiptAsync(receipt, ct);
        if (applied > 0)
        {
            await audit.WriteAsync(
                "DevelopmentWebhookApplied",
                "DevelopmentConnection",
                connection.Id,
                null,
                $"{message.ProviderEvent}|{normalizedEvent.Kind}|{applied}|{receipt.PayloadSha256[..16]}",
                message.DeliveryId,
                ct);
        }
    }

    private async Task ReplaceReceiptAsync(
        DevelopmentWebhookReceiptDocument receipt,
        CancellationToken ct)
    {
        try
        {
            var result = await receipts.ReplaceByVersionAsync(
                item => item.Id == receipt.Id
                    && item.ConnectionId == receipt.ConnectionId,
                receipt,
                receipt.Version,
                ct);
            if (!result.Found)
            {
                return;
            }

            receipt.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            var current = await receipts.SelectAsync(
                item => item.Id == receipt.Id
                    && item.ConnectionId == receipt.ConnectionId,
                ct);
            if (current?.Status == receipt.Status
                && current.AppliedLinks == receipt.AppliedLinks)
            {
                return;
            }

            throw;
        }
    }
}
