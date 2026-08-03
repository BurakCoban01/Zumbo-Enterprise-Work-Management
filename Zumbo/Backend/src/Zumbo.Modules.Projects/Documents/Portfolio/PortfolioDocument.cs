using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class PortfolioDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> ViewerUserIds { get; set; } = [];
    public List<InitiativeDocument> Initiatives { get; set; } = [];
    public List<PortfolioProjectDependencyDocument> Dependencies { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
