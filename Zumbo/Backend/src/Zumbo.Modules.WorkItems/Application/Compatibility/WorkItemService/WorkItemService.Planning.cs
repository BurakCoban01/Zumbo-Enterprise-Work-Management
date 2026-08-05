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
    public async Task<WorkItemResponse> MoveAsync(string id, MoveWorkItemRequest request, string correlationId, CancellationToken ct)
        => await moveWorkItemHandler.HandleAsync(
            new MoveWorkItemCommand(id, request, correlationId),
            ct);

    public async Task<WorkItemResponse> ReorderAsync(
        string id,
        ReorderWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);

        var rank = await ranks.ResolveReorderRankAsync(workItem, request, ct);
        var oldRank = workItem.Rank;
        workItem.Rank = rank;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync(
            "WorkItemReordered",
            "WorkItem",
            workItem.Id,
            oldRank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemReordered", "Rank changed", correlationId, ct);
        await PublishRealtimeAsync("reordered", workItem, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetPlanningAsync(string id, SetWorkItemPlanningRequest request, CancellationToken ct)
        => await setPlanningHandler.HandleAsync(new SetPlanningCommand(id, request), ct);

    public async Task<WorkItemResponse> AddChecklistItemAsync(string id, AddChecklistItemRequest request, CancellationToken ct)
        => await addChecklistItemHandler.HandleAsync(new AddChecklistItemCommand(id, request), ct);

    public async Task<WorkItemResponse> CompleteChecklistItemAsync(string id, string itemId, CompleteChecklistItemRequest request, CancellationToken ct)
        => await completeChecklistItemHandler.HandleAsync(
            new CompleteChecklistItemCommand(id, itemId, request),
            ct);

    public async Task<WorkItemResponse> AddLabelAsync(string id, AddLabelRequest request, CancellationToken ct)
        => await addLabelHandler.HandleAsync(new AddLabelCommand(id, request), ct);

    public async Task<WorkItemResponse> RemoveLabelAsync(string id, string label, CancellationToken ct)
        => await removeLabelHandler.HandleAsync(new RemoveLabelCommand(id, label), ct);
}
