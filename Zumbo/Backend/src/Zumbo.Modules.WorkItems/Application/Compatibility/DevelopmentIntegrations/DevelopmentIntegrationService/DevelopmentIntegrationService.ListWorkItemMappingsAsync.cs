using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>>
        ListWorkItemMappingsAsync(
            string workItemId,
            CancellationToken ct)
        => await listWorkItemMappingsHandler.HandleAsync(
            new ListWorkItemMappingsQuery(workItemId),
            ct);

}
