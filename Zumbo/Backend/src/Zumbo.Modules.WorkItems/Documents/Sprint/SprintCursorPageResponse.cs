using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed record SprintCursorPageResponse(
    IReadOnlyList<SprintResponse> Items,
    string? NextCursor);
