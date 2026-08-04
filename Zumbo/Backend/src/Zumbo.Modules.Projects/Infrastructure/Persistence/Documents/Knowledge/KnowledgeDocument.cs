using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

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
