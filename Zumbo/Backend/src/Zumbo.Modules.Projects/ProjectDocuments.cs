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

public sealed class ProjectMemberDocument
{
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = ProjectRoles.Developer;
}

public sealed class ProjectTemplateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool Archived { get; set; }
    public List<string> DefaultComponentNames { get; set; } = [];
}

public sealed class ProjectComponentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Archived { get; set; }
}

public sealed class ProjectVersionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = ProjectVersionStatuses.Planned;
    public DateTimeOffset? ReleasedAt { get; set; }
}

public sealed class ProjectReleaseDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string VersionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = ProjectReleaseStatuses.Draft;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class ProjectMilestoneDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public string Status { get; set; } = ProjectMilestoneStatuses.Open;
    public DateTimeOffset? CompletedAt { get; set; }
}
