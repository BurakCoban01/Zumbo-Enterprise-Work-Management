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

public sealed class TeamMemberDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";
    public string Status { get; set; } = "Active";
    public string? InvitationTokenHash { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? InvitationExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
