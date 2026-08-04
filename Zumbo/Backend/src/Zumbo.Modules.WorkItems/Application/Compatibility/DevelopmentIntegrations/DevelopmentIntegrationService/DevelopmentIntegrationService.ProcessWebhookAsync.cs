using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task ProcessWebhookAsync(
        DevelopmentWebhookEvent message,
        CancellationToken ct)
    {
        var receipt = await receipts.SelectAsync(
            item => item.Id == message.ReceiptId
                && item.ConnectionId == message.ConnectionId
                && item.OrganizationId == message.OrganizationId,
            ct);
        if (receipt is null || receipt.Status == DevelopmentWebhookReceiptStatuses.Applied)
            return;
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
        var normalizedEvent = NormalizeProviderEvent(mapping, message.Event);
        var applied = await ApplyProviderEventAsync(
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
            await WriteAuditAsync(
                "DevelopmentWebhookApplied",
                "DevelopmentConnection",
                connection.Id,
                null,
                $"{message.ProviderEvent}|{normalizedEvent.Kind}|{applied}|{receipt.PayloadSha256[..16]}",
                message.DeliveryId,
                ct);
        }
    }

}
