using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task DeleteMappingAsync(
        string mappingId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
        => await deleteMappingHandler.HandleAsync(
            new DeleteMappingCommand(mappingId, expectedVersion, correlationId),
            ct);

}
