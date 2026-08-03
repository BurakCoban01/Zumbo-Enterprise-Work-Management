namespace Zumbo.Modules.Projects;

public sealed record ProjectMilestoneResponse(
    string Id,
    string Name,
    DateTimeOffset DueAt,
    string Status,
    DateTimeOffset? CompletedAt);
