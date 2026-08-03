using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

public static class TeamMemberStatuses
{
    public const string Active = "Active";
    public const string Invited = "Invited";
    public const string Declined = "Declined";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
}
