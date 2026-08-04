namespace Zumbo.Modules.Boards;

public sealed record BoardColumnResponse(
    string Id,
    string Name,
    string Category,
    int Position,
    int? WipLimit,
    IReadOnlyCollection<string>? StatusNames = null);
