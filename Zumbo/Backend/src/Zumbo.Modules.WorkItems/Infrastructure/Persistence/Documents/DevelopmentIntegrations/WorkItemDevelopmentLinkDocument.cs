using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemDevelopmentLinkDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string MappingId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string Provider { get; set; } = DevelopmentProviders.GitHub;
    public string RepositoryFullName { get; set; } = string.Empty;
    public string Kind { get; set; } = DevelopmentLinkKinds.PullRequest;
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public string? CommitSha { get; set; }
    public string Status { get; set; } = "Unknown";
    public string Source { get; set; } = "Manual";
    public DateTimeOffset? LastEventAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
