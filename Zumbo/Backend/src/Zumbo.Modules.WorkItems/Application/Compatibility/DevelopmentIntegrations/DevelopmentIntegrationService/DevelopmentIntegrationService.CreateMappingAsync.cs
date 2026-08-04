using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentRepositoryMappingResponse> CreateMappingAsync(
        string connectionId,
        CreateDevelopmentRepositoryMappingRequest request,
        string correlationId,
        CancellationToken ct)
        => await createMappingHandler.HandleAsync(
            new CreateMappingCommand(connectionId, request, correlationId),
            ct);

}
