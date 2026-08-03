namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentProviderRepository(
    string ExternalRepositoryId,
    string Name,
    string FullName,
    string Url,
    string DefaultBranch);
