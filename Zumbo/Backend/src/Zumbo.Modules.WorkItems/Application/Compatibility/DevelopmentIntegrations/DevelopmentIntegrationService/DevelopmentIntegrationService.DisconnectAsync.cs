using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentConnectionResponse> DisconnectAsync(
        string connectionId,
        DevelopmentVersionRequest request,
        string correlationId,
        CancellationToken ct)
        => await disconnectConnectionHandler.HandleAsync(
            new DisconnectConnectionCommand(connectionId, request, correlationId),
            ct);

}
