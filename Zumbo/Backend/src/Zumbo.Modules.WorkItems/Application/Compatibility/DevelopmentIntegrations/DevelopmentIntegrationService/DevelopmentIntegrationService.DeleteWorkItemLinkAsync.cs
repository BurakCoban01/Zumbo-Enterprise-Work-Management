using Zumbo.Modules.WorkItems.Application.Features.Development.Links;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task DeleteWorkItemLinkAsync(
        string workItemId,
        string linkId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
        => await deleteWorkItemLinkHandler.HandleAsync(
            new DeleteWorkItemLinkCommand(
                workItemId,
                linkId,
                expectedVersion,
                correlationId),
            ct);

}
