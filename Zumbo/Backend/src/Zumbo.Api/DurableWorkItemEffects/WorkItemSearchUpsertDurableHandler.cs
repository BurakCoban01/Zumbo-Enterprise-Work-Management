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

public sealed class WorkItemSearchUpsertDurableHandler(
    IWorkItemSearchIndex search) : IDurableEventHandler
{
    public string ConsumerName => "work-item-search-upsert-v1";
    public string EventType => WorkItemDurableEventTypes.SearchUpsert;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        search.IndexAsync(DurablePayload.Read<WorkItemSearchUpsertEvent>(message).Record, cancellationToken);
}
