using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed record SprintBacklogPageResponse(
    IReadOnlyList<SprintBacklogItemResponse> Items,
    string? NextCursor);
