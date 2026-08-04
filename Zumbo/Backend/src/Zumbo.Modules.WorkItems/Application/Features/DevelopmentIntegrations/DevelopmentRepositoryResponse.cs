namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentRepositoryResponse(
    string ExternalRepositoryId,
    string Name,
    string FullName,
    string Url,
    string DefaultBranch);
