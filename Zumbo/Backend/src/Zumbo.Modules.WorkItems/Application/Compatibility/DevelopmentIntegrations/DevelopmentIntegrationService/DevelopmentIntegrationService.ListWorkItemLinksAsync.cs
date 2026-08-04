using Zumbo.Modules.WorkItems.Application.Features.Development.Links;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<IReadOnlyCollection<WorkItemDevelopmentLinkResponse>> ListWorkItemLinksAsync(
        string workItemId,
        CancellationToken ct)
        => await listWorkItemLinksHandler.HandleAsync(
            new ListWorkItemLinksQuery(workItemId),
            ct);

}
