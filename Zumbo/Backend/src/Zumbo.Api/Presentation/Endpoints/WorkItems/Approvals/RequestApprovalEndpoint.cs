using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Approvals;

internal static class RequestApprovalEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/approvals", async (string id, RequestWorkItemApprovalRequest request, RequestApprovalHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new RequestApprovalCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemApprove);
    }
}
