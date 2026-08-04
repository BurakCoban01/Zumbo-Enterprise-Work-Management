using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Teams;

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
