using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Reports;

internal static class GetDurableMessagingMetricsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/durable-messaging/metrics", async (
            IDurableEventOutbox outbox,
            IClock clock,
            CancellationToken ct) =>
            Results.Ok(await outbox.GetMetricsAsync(clock.UtcNow, ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);
    }
}
