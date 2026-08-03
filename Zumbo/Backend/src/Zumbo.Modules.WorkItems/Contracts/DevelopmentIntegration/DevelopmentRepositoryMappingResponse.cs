namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentRepositoryMappingResponse(
    string Id,
    string ConnectionId,
    string ProjectId,
    string ProjectKey,
    string ProjectName,
    string ExternalRepositoryId,
    string RepositoryName,
    string RepositoryFullName,
    string RepositoryUrl,
    string DefaultBranch,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);
