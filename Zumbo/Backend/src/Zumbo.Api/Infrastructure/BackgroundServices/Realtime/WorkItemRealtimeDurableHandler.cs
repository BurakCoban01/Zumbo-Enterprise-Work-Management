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

public sealed class WorkItemRealtimeDurableHandler(
    SignalRWorkItemRealtimePublisher publisher) : IDurableEventHandler
{
    public string ConsumerName => "work-item-realtime-v1";
    public string EventType => WorkItemDurableEventTypes.Realtime;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        publisher.PublishAsync(DurablePayload.Read<WorkItemRealtimeChange>(message), cancellationToken);
}
