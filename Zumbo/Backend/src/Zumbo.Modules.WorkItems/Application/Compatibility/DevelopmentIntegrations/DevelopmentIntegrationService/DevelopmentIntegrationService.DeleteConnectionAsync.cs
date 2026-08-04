using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task DeleteConnectionAsync(
        string connectionId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
        => await deleteConnectionHandler.HandleAsync(
            new DeleteConnectionCommand(
                connectionId,
                expectedVersion,
                correlationId),
            ct);

}
