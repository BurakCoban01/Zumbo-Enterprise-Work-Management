namespace Zumbo.Modules.Teams;

public sealed record TeamInvitationNotificationEvent(
    string UserId,
    string TeamId,
    string InviteId,
    string TeamName,
    string InvitedByUserId,
    string DeduplicationKey);
