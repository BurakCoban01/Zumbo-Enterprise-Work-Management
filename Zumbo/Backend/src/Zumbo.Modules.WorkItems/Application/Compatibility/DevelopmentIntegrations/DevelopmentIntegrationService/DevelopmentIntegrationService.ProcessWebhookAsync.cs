using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task ProcessWebhookAsync(
        DevelopmentWebhookEvent message,
        CancellationToken ct)
        => await processWebhookHandler.HandleAsync(
            new ProcessWebhookCommand(message),
            ct);

}
