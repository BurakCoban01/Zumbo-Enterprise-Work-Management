using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

public sealed class TeamUserDirectoryAdapter(IUserRepository users) : ITeamUserDirectory
{
    public async Task<TeamUserDirectoryEntry?> FindByIdAsync(string userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        return user is null
            ? null
            : new TeamUserDirectoryEntry(user.Id, user.Email, user.OrganizationId, user.IsActive, user.Username);
    }

    public async Task<TeamUserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var user = await users.GetByUsernameOrEmailAsync(email, ct);
        return user is null || !user.Email.Equals(email, StringComparison.OrdinalIgnoreCase)
            ? null
            : new TeamUserDirectoryEntry(user.Id, user.Email, user.OrganizationId, user.IsActive, user.Username);
    }
}
