using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdWorklogs(RouteGroupBuilder group){group.MapPost("/{id}/worklogs", async (string id, AddWorkLogRequest request, AddWorkLogHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new AddWorkLogCommand(id, request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkLogCreate);
}}
