using Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentConnectionReceipt> RotateWebhookSecretAsync(
        string connectionId,
        DevelopmentVersionRequest request,
        string correlationId,
        CancellationToken ct)
        => await rotateWebhookSecretHandler.HandleAsync(
            new RotateWebhookSecretCommand(connectionId, request, correlationId),
            ct);

}
