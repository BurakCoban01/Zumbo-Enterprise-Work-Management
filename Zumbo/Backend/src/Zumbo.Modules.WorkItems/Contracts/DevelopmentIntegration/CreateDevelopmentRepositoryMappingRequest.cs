namespace Zumbo.Modules.WorkItems;

public sealed record CreateDevelopmentRepositoryMappingRequest(
    string ProjectId,
    string ExternalRepositoryId,
    string RepositoryName,
    string RepositoryFullName,
    string RepositoryUrl,
    string DefaultBranch);
