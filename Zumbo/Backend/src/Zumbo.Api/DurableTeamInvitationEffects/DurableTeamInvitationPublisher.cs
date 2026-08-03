using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

public sealed class DurableTeamInvitationPublisher(
    IDurableEventOutbox outbox,
    IClock clock) : ITeamInvitationNotifier
{
    public Task NotifyAsync(
        string organizationId,
        string userId,
        string teamId,
        string inviteId,
        string teamName,
        string invitedByUserId,
        string correlationId,
        CancellationToken ct)
    {
        var deduplicationKey = Hash("team-invite", teamId, inviteId, userId);
        var payload = new TeamInvitationNotificationEvent(
            userId,
            teamId,
            inviteId,
            teamName,
            invitedByUserId,
            deduplicationKey);
        return outbox.EnqueueAsync(
            DurableEventEnvelope.Create(
                "Teams",
                TeamDurableEventTypes.InvitationNotification,
                1,
                organizationId,
                correlationId,
                JsonSerializer.Serialize(payload),
                clock.UtcNow,
                deduplicationKey),
            ct);
    }

    private static string Hash(params string[] values) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', values)))).ToLowerInvariant();
}
