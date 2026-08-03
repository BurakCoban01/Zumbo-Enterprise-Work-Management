namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentRepositoryPage(
    IReadOnlyCollection<DevelopmentRepositoryResponse> Items,
    string SourceStatus);
