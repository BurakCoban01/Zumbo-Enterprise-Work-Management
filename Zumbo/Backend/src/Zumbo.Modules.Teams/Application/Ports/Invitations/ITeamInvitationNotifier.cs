using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

public interface ITeamInvitationNotifier
{
    Task NotifyAsync(
        string organizationId,
        string userId,
        string teamId,
        string inviteId,
        string teamName,
        string invitedByUserId,
        string correlationId,
        CancellationToken ct);
}
