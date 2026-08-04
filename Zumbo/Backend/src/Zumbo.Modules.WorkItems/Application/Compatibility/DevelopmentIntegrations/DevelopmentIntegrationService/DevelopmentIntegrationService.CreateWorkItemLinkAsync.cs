using Zumbo.Modules.WorkItems.Application.Features.Development.Links;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<WorkItemDevelopmentLinkResponse> CreateWorkItemLinkAsync(
        string workItemId,
        CreateWorkItemDevelopmentLinkRequest request,
        string correlationId,
        CancellationToken ct)
        => await createWorkItemLinkHandler.HandleAsync(
            new CreateWorkItemLinkCommand(workItemId, request, correlationId),
            ct);

}
