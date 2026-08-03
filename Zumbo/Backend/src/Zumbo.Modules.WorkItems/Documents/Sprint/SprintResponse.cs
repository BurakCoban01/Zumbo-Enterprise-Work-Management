using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

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
