using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Boards;

public sealed record BoardResponse(
    string Id,
    string ProjectId,
    string Name,
    string Type,
    string SwimlaneMode,
    IReadOnlyCollection<BoardColumnResponse> Columns,
    IReadOnlyCollection<BoardViewResponse> Views,
    bool Archived = false,
    long Version = 0) : IVersionedResource;
