using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

internal sealed class AllowActiveTeamOrganizationDirectory : ITeamOrganizationDirectory
{
    internal static readonly AllowActiveTeamOrganizationDirectory Instance = new();
    public Task EnsureActiveAsync(string organizationId, CancellationToken ct) => Task.CompletedTask;
}
