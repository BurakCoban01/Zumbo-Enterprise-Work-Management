using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdApprovalsByApprovalIdDecision(RouteGroupBuilder group){group.MapPost("/{id}/approvals/{approvalId}/decision", async (
            string id,
            string approvalId,
            DecideWorkItemApprovalRequest request,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.DecideApprovalAsync(id, approvalId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemApprove);
}}
