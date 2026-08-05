using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task ArchiveAsync(string id, string correlationId, CancellationToken ct)
        => await archiveWorkItemHandler.HandleAsync(new ArchiveWorkItemCommand(id, correlationId), ct);

    public async Task<WorkItemResponse> RestoreAsync(string id, string correlationId, CancellationToken ct)
        => await restoreWorkItemHandler.HandleAsync(new RestoreWorkItemCommand(id, correlationId), ct);

    public Task<BulkWorkItemResponse> BulkMoveAsync(BulkMoveWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            (id, itemCorrelationId, token) => MoveAsync(id, new MoveWorkItemRequest(request.Status), itemCorrelationId, token),
            correlationId,
            ct);

    public Task<BulkWorkItemResponse> BulkAssignAsync(BulkAssignWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            (id, itemCorrelationId, token) => AssignAsync(id, new AssignWorkItemRequest(request.AssigneeUserId), itemCorrelationId, token),
            correlationId,
            ct);

    public Task<BulkWorkItemResponse> BulkArchiveAsync(BulkArchiveWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            async (id, itemCorrelationId, token) =>
            {
                await ArchiveAsync(id, itemCorrelationId, token);
                return true;
            },
            correlationId,
            ct);

    private async Task RecordActivityAndNotifyWatchersAsync(
        WorkItemDocument workItem,
        string activityType,
        string detail,
        string eventId,
        CancellationToken ct)
    {
        if (collaborationService is null)
        {
            return;
        }

        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        await collaborationService.RecordActivityAsync(
            workItem,
            organizationId,
            activityType,
            detail,
            eventId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: {detail}",
            eventId,
            null,
            ct);
    }

    private static string MutationEventId(WorkItemDocument workItem, string discriminator) =>
        $"{discriminator}:{workItem.UpdatedAt.ToUniversalTime().Ticks}";

    private Task PublishRealtimeAsync(
        string eventType,
        WorkItemDocument workItem,
        string correlationId,
        CancellationToken ct) =>
        realtimePublisher.PublishAsync(
            new WorkItemRealtimeChange(
                eventType,
                workItem.Id,
                workItem.ProjectId,
                workItem.BoardId,
                ToRealtimeItem(workItem),
                correlationId,
                clock.UtcNow,
                WorkItemRealtimeProtocol.CurrentSchemaVersion,
                workItem.Version),
            ct);
}
