using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DevelopmentWebhookReceiptRetentionService(
    IDocumentRepository<DevelopmentWebhookReceiptDocument> receipts,
    IClock clock)
{
    public Task<long> PurgeExpiredAsync(CancellationToken ct) =>
        receipts.DeleteByFilterAsync(
            item => item.ExpiresAtUtc <= clock.UtcNow.UtcDateTime,
            ct);
}
