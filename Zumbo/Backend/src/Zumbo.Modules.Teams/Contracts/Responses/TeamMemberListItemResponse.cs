namespace Zumbo.Modules.Teams;

public sealed record TeamMemberListItemResponse(
    string Id,
    string? UserId,
    string Email,
    string DisplayName,
    string Role,
    string Status,
    DateTimeOffset? InvitationExpiresAt);
