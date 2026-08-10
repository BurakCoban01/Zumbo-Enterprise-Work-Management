using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

public sealed class TeamInvitationNotificationHandler(
    NotificationService notifications) : IDurableEventHandler
{
    public string ConsumerName => "team-invitation-notification-v1";
    public string EventType => TeamDurableEventTypes.InvitationNotification;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<TeamInvitationNotificationEvent>(message.Payload)
            ?? throw new InvalidOperationException(
                $"Durable event '{message.Id}' contains an invalid team invitation payload.");
        return notifications.NotifyAsync(
            payload.UserId,
            "TeamInvitation",
            $"You were invited to join team '{payload.TeamName}'.",
            cancellationToken,
            payload.DeduplicationKey,
            "Team",
            payload.TeamId);
    }
}
