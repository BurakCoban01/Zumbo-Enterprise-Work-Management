using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class BoardColumnWipProjectionDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string ColumnId { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemWipProjection(
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<BoardColumnWipProjectionDocument> projections,
    IClock clock)
{
    public async Task ReserveCreateAsync(
        string projectId,
        string boardId,
        BoardPlacement target,
        CancellationToken ct)
    {
        var projection = await GetOrCreateAsync(projectId, boardId, target.ColumnId, ct);
        EnsureCapacity(projection.ActiveCount, target);
        projection.ActiveCount++;
        await SaveAsync(projection, ct);
    }

    public async Task ReserveMoveAsync(
        WorkItemDocument workItem,
        BoardPlacement target,
        CancellationToken ct)
    {
        if (workItem.ColumnId == target.ColumnId)
        {
            return;
        }

        var targetProjection = await GetOrCreateAsync(workItem.ProjectId, workItem.BoardId, target.ColumnId, ct);
        EnsureCapacity(targetProjection.ActiveCount, target);
        var sourceProjection = await GetOrCreateAsync(workItem.ProjectId, workItem.BoardId, workItem.ColumnId, ct);
        targetProjection.ActiveCount++;
        sourceProjection.ActiveCount = Math.Max(0, sourceProjection.ActiveCount - 1);
        await SaveAsync(targetProjection, ct);
        await SaveAsync(sourceProjection, ct);
    }

    public async Task ReleaseAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        var projection = await GetOrCreateAsync(workItem.ProjectId, workItem.BoardId, workItem.ColumnId, ct);
        projection.ActiveCount = Math.Max(0, projection.ActiveCount - 1);
        await SaveAsync(projection, ct);
    }

    private async Task<BoardColumnWipProjectionDocument> GetOrCreateAsync(
        string projectId,
        string boardId,
        string columnId,
        CancellationToken ct)
    {
        var id = $"{boardId}:{columnId}";
        var current = await projections.SelectAsync(x => x.Id == id, ct);
        if (current is not null)
        {
            return current;
        }

        var count = await workItems.CountByFilterAsync(x =>
            x.BoardId == boardId && x.ColumnId == columnId && !x.Archived, ct);
        var created = new BoardColumnWipProjectionDocument
        {
            Id = id,
            ProjectId = projectId,
            BoardId = boardId,
            ColumnId = columnId,
            ActiveCount = checked((int)count),
            UpdatedAt = clock.UtcNow
        };
        return await projections.CreateAsync(created, ct);
    }

    private async Task SaveAsync(BoardColumnWipProjectionDocument projection, CancellationToken ct)
    {
        projection.UpdatedAt = clock.UtcNow;
        var result = await projections.ReplaceByVersionAsync(
            x => x.Id == projection.Id,
            projection,
            projection.Version,
            ct);
        if (!result.Found)
        {
            throw new ConflictException("BOARD_WIP_PROJECTION_MISSING", "WIP projection changed while reserving capacity.");
        }

        projection.Version = result.Version!.Value;
    }

    private static void EnsureCapacity(int activeCount, BoardPlacement target)
    {
        if (target.WipLimit.HasValue && activeCount >= target.WipLimit.Value)
        {
            throw new ConflictException(
                "BOARD_WIP_LIMIT_EXCEEDED",
                $"Target column has reached its WIP limit of {target.WipLimit.Value}.");
        }
    }
}
