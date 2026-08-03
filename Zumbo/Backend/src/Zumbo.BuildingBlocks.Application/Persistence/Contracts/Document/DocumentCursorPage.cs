using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public sealed record DocumentCursorPage<TDocument>(
    IReadOnlyList<TDocument> Items,
    string? NextCursor);
