using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class ProjectDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Visibility { get; set; } = ProjectVisibilities.Internal;
    public bool Archived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? RetainUntil { get; set; }
    public long Version { get; set; }
    public List<ProjectMemberDocument> Members { get; set; } = [];
    public List<string> TeamIds { get; set; } = [];
    public List<ProjectTemplateDocument> Templates { get; set; } = [];
    public List<ProjectComponentDocument> Components { get; set; } = [];
    public List<ProjectVersionDocument> Versions { get; set; } = [];
    public List<ProjectReleaseDocument> Releases { get; set; } = [];
    public List<ProjectMilestoneDocument> Milestones { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
