using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class WorkItemAuditDurableHandler(
    AuditService audit) : IDurableEventHandler
{
    public string ConsumerName => "work-item-audit-v1";
    public string EventType => WorkItemDurableEventTypes.Audit;

    public async Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = DurablePayload.Read<WorkItemAuditEvent>(message);
        await audit.WriteAsAsync(
            payload.ActorUserId,
            payload.Action,
            payload.EntityType,
            payload.EntityId,
            payload.OldValue,
            payload.NewValue,
            payload.CorrelationId,
            new AuditRequestMetadata(payload.IpAddress, payload.UserAgent),
            payload.OccurredAtUtc,
            payload.DeduplicationKey,
            cancellationToken);
    }
}
