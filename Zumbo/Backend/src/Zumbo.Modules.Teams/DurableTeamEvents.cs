namespace Zumbo.Modules.Teams;

public static class TeamDurableEventTypes
{
    public const string InvitationNotification = "team.invitation-notification.v1";
}

public sealed record TeamInvitationNotificationEvent(
    string UserId,
    string TeamId,
    string InviteId,
    string TeamName,
    string InvitedByUserId,
    string DeduplicationKey);
