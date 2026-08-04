namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemDevelopmentLinkResponse(
    string Id,
    string ConnectionId,
    string MappingId,
    string ProjectId,
    string WorkItemId,
    string Provider,
    string RepositoryFullName,
    string Kind,
    string ExternalId,
    string Title,
    string Url,
    string? Branch,
    string? CommitSha,
    string Status,
    string Source,
    bool ConnectionActive,
    DateTimeOffset? LastEventAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);
