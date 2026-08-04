namespace Zumbo.Modules.WorkItems;

public sealed record NormalizedDevelopmentEvent(
    string RepositoryExternalId,
    string Kind,
    string ExternalId,
    string Title,
    string Url,
    string? Branch,
    string? CommitSha,
    string Status,
    DateTimeOffset? OccurredAtUtc,
    IReadOnlyCollection<string> ReferenceTexts);
