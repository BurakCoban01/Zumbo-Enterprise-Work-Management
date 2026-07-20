using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class SprintStatuses
{
    public const string Planned = "Planned";
    public const string Active = "Active";
    public const string Completed = "Completed";
}

public sealed class SprintDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public DateTimeOffset StartAtUtc { get; set; }
    public DateTimeOffset EndAtUtc { get; set; }
    public string Status { get; set; } = SprintStatuses.Planned;
    public int CommittedItems { get; set; }
    public decimal CommittedPoints { get; set; }
    public int CompletedItems { get; set; }
    public decimal CompletedPoints { get; set; }
    public int CarryoverItems { get; set; }
    public decimal CarryoverPoints { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class SprintScopeSnapshotDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string SprintId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal EstimatePoints { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public long Version { get; set; }
}

public sealed class SprintCompletionSnapshotDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string SprintId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public decimal CommittedPoints { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CarryoverSprintId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public long Version { get; set; }
}

public sealed record CreateSprintRequest(
    string ProjectId,
    string Name,
    string? Goal,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record PlanSprintWorkItemRequest(decimal? EstimatePoints);
public sealed record CompleteSprintRequest(string? CarryoverSprintId);

public sealed record SprintResponse(
    string Id,
    string ProjectId,
    string Name,
    string Goal,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    int CommittedItems,
    decimal CommittedPoints,
    int CompletedItems,
    decimal CompletedPoints,
    int CarryoverItems,
    decimal CarryoverPoints,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long Version) : IVersionedResource;

public sealed record SprintPlannedItemResponse(
    string WorkItemId,
    string? SprintId,
    decimal EstimatePoints,
    long Version) : IVersionedResource;

public sealed record SprintCursorPageResponse(
    IReadOnlyList<SprintResponse> Items,
    string? NextCursor);

public sealed record SprintBacklogItemResponse(
    string Id,
    string Title,
    string Type,
    string Priority,
    decimal EstimatePoints,
    long Rank,
    long Version);

public sealed record SprintBacklogPageResponse(
    IReadOnlyList<SprintBacklogItemResponse> Items,
    string? NextCursor);
