using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

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
