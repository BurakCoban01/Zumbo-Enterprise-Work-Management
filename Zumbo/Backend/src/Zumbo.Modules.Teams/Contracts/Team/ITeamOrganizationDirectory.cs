using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

public interface ITeamOrganizationDirectory
{
    Task EnsureActiveAsync(string organizationId, CancellationToken ct);
}
