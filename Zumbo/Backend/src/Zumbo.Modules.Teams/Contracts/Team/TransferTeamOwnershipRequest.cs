using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;
public sealed record TransferTeamOwnershipRequest(string NewOwnerUserId);
