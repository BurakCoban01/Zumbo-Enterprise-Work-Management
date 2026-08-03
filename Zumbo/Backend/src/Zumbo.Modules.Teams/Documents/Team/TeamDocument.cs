using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Teams;

public sealed class TeamDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Archived { get; set; }
    public long Version { get; set; }
    public List<TeamMemberDocument> Members { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
