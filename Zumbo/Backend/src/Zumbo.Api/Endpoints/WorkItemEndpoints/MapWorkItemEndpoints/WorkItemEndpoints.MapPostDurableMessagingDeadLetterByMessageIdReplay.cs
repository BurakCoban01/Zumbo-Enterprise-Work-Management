using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostDurableMessagingDeadLetterByMessageIdReplay(RouteGroupBuilder group){group.MapPost("/durable-messaging/dead-letter/{messageId}/replay", async (
            string messageId,
            IDurableEventOutbox outbox,
            IWorkItemOperationsAuditWriter audit,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var replayed = await outbox.ReplayDeadLetterAsync(messageId, clock.UtcNow, ct);
            if (replayed)
            {
                await audit.WriteAsync(
                    "DurableMessageReplayed",
                    "DurableMessage",
                    messageId,
                    "DeadLetter",
                    "Pending",
                    CorrelationId(http),
                    ct);
            }

            return Results.Ok(new { replayed });
        })
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);
}}
