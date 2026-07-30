using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public static class KnowledgeScopeTypes
{
    public const string Project = "Project";
    public const string Initiative = "Initiative";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Project, Initiative],
        StringComparer.Ordinal);
}

public static class KnowledgeSourceStatuses
{
    public const string Ready = "Ready";
    public const string Partial = "Partial";
}

public static class KnowledgeLimits
{
    public const int MaximumVersions = 50;
    public const int MaximumComments = 200;
    public const int MaximumContentCharacters = 40_000;
    public const int MaximumTags = 20;
    public const int MaximumWorkItemLinks = 50;
    public const int MaximumUserLinks = 30;
    public const int MaximumSearchDocuments = 1_000;
}

public sealed class KnowledgeDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ScopeType { get; set; } = KnowledgeScopeTypes.Project;
    public string ScopeId { get; set; } = string.Empty;
    public string ScopeName { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentMarkdown { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> WorkItemIds { get; set; } = [];
    public List<string> UserIds { get; set; } = [];
    public int CurrentContentVersion { get; set; }
    public List<KnowledgeVersionDocument> Versions { get; set; } = [];
    public List<KnowledgeCommentDocument> Comments { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class KnowledgeVersionDocument
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentMarkdown { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> WorkItemIds { get; set; } = [];
    public List<string> UserIds { get; set; } = [];
    public string ChangeSummary { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class KnowledgeCommentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Body { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public string? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
