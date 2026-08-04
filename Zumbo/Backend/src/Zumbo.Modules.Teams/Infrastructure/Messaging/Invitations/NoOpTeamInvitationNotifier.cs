using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

internal sealed class NoOpTeamInvitationNotifier : ITeamInvitationNotifier
{
    internal static readonly NoOpTeamInvitationNotifier Instance = new();

    public Task NotifyAsync(
        string organizationId,
        string userId,
        string teamId,
        string inviteId,
        string teamName,
        string invitedByUserId,
        string correlationId,
        CancellationToken ct) => Task.CompletedTask;
}
