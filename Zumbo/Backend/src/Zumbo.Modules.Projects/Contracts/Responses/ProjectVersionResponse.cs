namespace Zumbo.Modules.Projects;

public sealed record ProjectVersionResponse(string Id, string Name, string Status, DateTimeOffset? ReleasedAt);
