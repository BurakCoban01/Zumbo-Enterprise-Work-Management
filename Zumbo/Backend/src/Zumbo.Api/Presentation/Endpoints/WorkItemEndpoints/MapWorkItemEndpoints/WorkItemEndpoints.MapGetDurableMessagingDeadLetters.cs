using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetDurableMessagingDeadLetters(RouteGroupBuilder group){group.MapGet("/durable-messaging/dead-letters", async (
            int? pageSize,
            IDurableEventOutbox outbox,
            CancellationToken ct) =>
            Results.Ok(await outbox.ListDeadLettersAsync(
                Math.Clamp(pageSize ?? 20, 1, 50),
                ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("report");
}}
