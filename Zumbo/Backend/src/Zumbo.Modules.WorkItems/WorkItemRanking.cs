using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemRankOptions
{
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerRun { get; set; } = 1_000;
}

public sealed record WorkItemRankRebalanceResult(int ItemCount, int Changed, int Batches);

public sealed class WorkItemRankService(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    IOptions<WorkItemRankOptions> configuredOptions)
{
    public const long RankStep = 1_000_000;

    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerRun, 4, 10_000);

    public async Task<long> NextRankAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        var rank = await TryNextRankAsync(boardId, columnId, ignoredWorkItemId, ct);
        if (rank is not null)
        {
            return rank.Value;
        }

        await RebalanceAsync(boardId, columnId, ignoredWorkItemId, ct);
        return await TryNextRankAsync(boardId, columnId, ignoredWorkItemId, ct)
            ?? throw RankExhausted();
    }

    public async Task<long> ResolveReorderRankAsync(
        WorkItemDocument workItem,
        ReorderWorkItemRequest request,
        CancellationToken ct)
    {
        var rank = await TryResolveReorderRankAsync(workItem, request, ct);
        if (rank is not null)
        {
            return rank.Value;
        }

        await RebalanceAsync(workItem.BoardId, workItem.ColumnId, workItem.Id, ct);
        return await TryResolveReorderRankAsync(workItem, request, ct)
            ?? throw RankExhausted();
    }

    public async Task<WorkItemRankRebalanceResult> RebalanceAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        var batches = 0;
        var ordinal = 0L;
        var canonical = true;
        for (var pageNumber = 1; canonical; pageNumber++)
        {
            EnsureBatchLimit(++batches);
            var page = await workItems.ListByFilterAsync(
                item => !item.Archived
                    && item.BoardId == boardId
                    && item.ColumnId == columnId
                    && (ignoredWorkItemId == null || item.Id != ignoredWorkItemId),
                item => item.Rank,
                page: pageNumber,
                pageSize: BatchSize,
                cancellationToken: ct);
            foreach (var item in page)
            {
                ordinal++;
                canonical = item.Rank == CanonicalRank(ordinal) && item.RankRebalanceToken is null;
                if (!canonical)
                {
                    break;
                }
            }

            if (canonical && page.Count < BatchSize)
            {
                return new WorkItemRankRebalanceResult((int)ordinal, 0, batches);
            }
        }

        if (await workItems.ExistsByFilterAsync(
                item => !item.Archived
                    && item.BoardId == boardId
                    && item.ColumnId == columnId
                    && item.RankRebalanceToken != null,
                ct))
        {
            throw new ConflictException(
                "WORK_ITEM_RANK_REBALANCE_INCOMPLETE",
                "The target column contains an incomplete rank rebalance; retry after transaction recovery.");
        }

        var token = Guid.NewGuid().ToString("N");
        ordinal = 0;
        while (true)
        {
            EnsureBatchLimit(++batches);
            var page = await workItems.ListByFilterAsync(
                item => !item.Archived
                    && item.BoardId == boardId
                    && item.ColumnId == columnId
                    && (ignoredWorkItemId == null || item.Id != ignoredWorkItemId)
                    && item.RankRebalanceToken == null,
                item => item.Rank,
                pageSize: BatchSize,
                cancellationToken: ct);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var item in page)
            {
                ordinal++;
                item.Rank = checked(long.MinValue + ordinal);
                item.RankRebalanceToken = token;
                await SaveAsync(item, ct);
            }
        }

        string? cursor = null;
        do
        {
            EnsureBatchLimit(++batches);
            var page = await workItems.ListByCursorAsync(
                item => item.RankRebalanceToken == token,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                var itemOrdinal = checked(item.Rank - long.MinValue);
                item.Rank = CanonicalRank(itemOrdinal);
                item.RankRebalanceToken = null;
                await SaveAsync(item, ct);
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return new WorkItemRankRebalanceResult((int)ordinal, (int)ordinal, batches);
    }

    private async Task<long?> TryNextRankAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        var last = await workItems.ListByFilterAsync(
            item => !item.Archived
                && item.BoardId == boardId
                && item.ColumnId == columnId
                && (ignoredWorkItemId == null || item.Id != ignoredWorkItemId),
            item => item.Rank,
            orderDescending: true,
            pageSize: 1,
            cancellationToken: ct);
        if (last.Count == 0)
        {
            return RankStep;
        }

        var candidate = (decimal)last[0].Rank + RankStep;
        return candidate <= long.MaxValue ? (long)candidate : null;
    }

    private async Task<long?> TryResolveReorderRankAsync(
        WorkItemDocument workItem,
        ReorderWorkItemRequest request,
        CancellationToken ct)
    {
        var beforeId = NormalizeOptionalId(request.BeforeWorkItemId);
        var afterId = NormalizeOptionalId(request.AfterWorkItemId);
        if ((beforeId is null) == (afterId is null))
        {
            throw new ValidationException("Exactly one before or after work item id is required.");
        }

        var anchorId = beforeId ?? afterId!;
        if (anchorId == workItem.Id)
        {
            throw new ValidationException("A work item cannot be ordered relative to itself.");
        }

        var anchor = await workItems.SelectAsync(item => item.Id == anchorId && !item.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Rank anchor was not found.");
        if (anchor.ProjectId != workItem.ProjectId
            || anchor.BoardId != workItem.BoardId
            || anchor.ColumnId != workItem.ColumnId
            || anchor.Status != workItem.Status)
        {
            throw new ValidationException("The rank anchor must be in the same board column.");
        }

        if (beforeId is not null)
        {
            var predecessors = await workItems.ListByFilterAsync(
                item => !item.Archived
                    && item.BoardId == workItem.BoardId
                    && item.ColumnId == workItem.ColumnId
                    && item.Id != workItem.Id
                    && item.Id != anchor.Id
                    && item.Rank < anchor.Rank,
                item => item.Rank,
                orderDescending: true,
                pageSize: 1,
                cancellationToken: ct);
            var lower = predecessors.Count == 0 ? (decimal)anchor.Rank - RankStep : predecessors[0].Rank;
            return RankBetween(lower, anchor.Rank);
        }

        var successors = await workItems.ListByFilterAsync(
            item => !item.Archived
                && item.BoardId == workItem.BoardId
                && item.ColumnId == workItem.ColumnId
                && item.Id != workItem.Id
                && item.Id != anchor.Id
                && item.Rank > anchor.Rank,
            item => item.Rank,
            pageSize: 1,
            cancellationToken: ct);
        var upper = successors.Count == 0 ? (decimal)anchor.Rank + RankStep : successors[0].Rank;
        return RankBetween(anchor.Rank, upper);
    }

    private async Task SaveAsync(WorkItemDocument item, CancellationToken ct)
    {
        item.UpdatedAt = clock.UtcNow;
        var result = await workItems.ReplaceByVersionAsync(x => x.Id == item.Id, item, item.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException(
                "WORK_ITEM_RANK_CONFLICT",
                "The work item rank changed concurrently; retry the operation.");
        }

        item.Version = result.Version!.Value;
    }

    private static long? RankBetween(decimal lower, decimal upper)
    {
        if (upper > long.MaxValue || lower < long.MinValue || upper - lower <= 1)
        {
            return null;
        }

        return (long)((lower + upper) / 2);
    }

    private static long CanonicalRank(long ordinal) => checked(ordinal * RankStep);

    private void EnsureBatchLimit(int batches)
    {
        if (batches > MaxBatches)
        {
            throw new ConflictException(
                "WORK_ITEM_RANK_REBALANCE_LIMIT",
                "The rank rebalance exceeded its bounded batch limit.");
        }
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ConflictException RankExhausted() =>
        new(
            "WORK_ITEM_RANK_EXHAUSTED",
            "The target column rank range remains exhausted after rebalance.");
}
