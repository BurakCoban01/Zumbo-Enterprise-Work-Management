namespace Zumbo.Modules.Teams;

public sealed partial class TeamService
{
    public Task ArchiveAsync(string teamId, CancellationToken ct) => ArchiveAsync(teamId, "none", ct);

    public async Task ArchiveAsync(string teamId, string correlationId, CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        EnsureOwner(team);
        team.Archived = true;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamArchived", team.Id, "active", "archived", correlationId, ct);
    }

    public async Task<TeamResponse> RestoreAsync(
        string teamId,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetArchivedTeam(teamId, ct);
        EnsureOwner(team);
        team.Archived = false;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamRestored", team.Id, "archived", "active", correlationId, ct);
        return ToResponse(team);
    }
}
