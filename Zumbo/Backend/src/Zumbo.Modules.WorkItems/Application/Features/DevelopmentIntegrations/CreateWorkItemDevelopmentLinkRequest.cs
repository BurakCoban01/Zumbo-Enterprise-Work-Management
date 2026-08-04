namespace Zumbo.Modules.WorkItems;

public sealed record CreateWorkItemDevelopmentLinkRequest(
    string MappingId,
    string Kind,
    string ExternalId,
    string Title,
    string Url,
    string? Branch,
    string? CommitSha,
    string Status);
