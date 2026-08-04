using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetById(RouteGroupBuilder group){group.MapGet("/{id}", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetAsync(id, ct), http));
}}
