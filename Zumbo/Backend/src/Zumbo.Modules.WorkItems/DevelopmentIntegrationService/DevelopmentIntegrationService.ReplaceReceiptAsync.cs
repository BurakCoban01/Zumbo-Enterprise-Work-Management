using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

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
            if (!result.Found) return;
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
