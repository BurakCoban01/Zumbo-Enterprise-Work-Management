using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Approvals;

internal static class DecideApprovalEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/approvals/{approvalId}/decision", async (
            string id,
            string approvalId,
            DecideWorkItemApprovalRequest request,
            DecideApprovalHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new DecideApprovalCommand(id, approvalId, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemApprove);
    }
}
