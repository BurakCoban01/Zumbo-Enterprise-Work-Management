using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentWebhookResult> ReceiveWebhookAsync(
        string connectionId,
        DevelopmentWebhookRequest request,
        CancellationToken ct)
        => await receiveWebhookHandler.HandleAsync(
            new ReceiveWebhookCommand(connectionId, request),
            ct);

}
