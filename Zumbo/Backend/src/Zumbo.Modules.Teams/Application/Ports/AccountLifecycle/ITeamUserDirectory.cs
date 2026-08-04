using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

public interface ITeamUserDirectory
{
    Task<TeamUserDirectoryEntry?> FindByIdAsync(string userId, CancellationToken ct);
    Task<TeamUserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken ct);
}
