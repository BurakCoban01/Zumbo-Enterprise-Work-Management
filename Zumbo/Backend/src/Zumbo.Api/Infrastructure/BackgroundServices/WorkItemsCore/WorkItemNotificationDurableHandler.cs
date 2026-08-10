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

public sealed class WorkItemNotificationDurableHandler(
    NotificationService notifications) : IDurableEventHandler
{
    public string ConsumerName => "work-item-notification-v1";
    public string EventType => WorkItemDurableEventTypes.Notification;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = DurablePayload.Read<WorkItemNotificationEvent>(message);
        return notifications.NotifyAsync(
            payload.UserId,
            payload.Type,
            payload.Message,
            cancellationToken,
            payload.DeduplicationKey,
            payload.SourceKind,
            payload.SourceId,
            payload.ProjectId);
    }
}
