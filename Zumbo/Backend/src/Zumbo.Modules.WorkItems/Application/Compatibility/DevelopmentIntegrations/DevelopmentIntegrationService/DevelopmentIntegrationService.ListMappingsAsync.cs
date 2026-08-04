using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>> ListMappingsAsync(
        string connectionId,
        CancellationToken ct)
        => await listConnectionMappingsHandler.HandleAsync(
            new ListConnectionMappingsQuery(connectionId),
            ct);

}
