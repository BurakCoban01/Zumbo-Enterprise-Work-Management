namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentHealthResponse(
    string Status,
    string? ErrorCode,
    DateTimeOffset CheckedAtUtc);
