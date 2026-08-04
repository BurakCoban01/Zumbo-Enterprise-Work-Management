namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentProviderRepositoryResult(
    IReadOnlyCollection<DevelopmentProviderRepository> Items,
    bool Partial);
