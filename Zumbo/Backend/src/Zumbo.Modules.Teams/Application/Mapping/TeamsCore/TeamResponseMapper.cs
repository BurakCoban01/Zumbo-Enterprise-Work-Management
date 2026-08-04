using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

internal static class TeamResponseMapper
{
    internal static TeamResponse ToResponse(
        TeamDocument team,
        IClock clock,
        string? invitationToken = null) =>
        new(
            team.Id,
            team.OrganizationId,
            team.Name,
            team.Members.Select(member => new TeamMemberResponse(
                member.Id,
                member.UserId,
                member.Email,
                member.Role,
                EffectiveStatus(member, clock.UtcNow),
                member.InvitationExpiresAt,
                member.RespondedAt)).ToList(),
            team.Archived,
            team.Version,
            invitationToken);

    private static string EffectiveStatus(TeamMemberDocument member, DateTimeOffset now) =>
        member.Status == TeamMemberStatuses.Invited && member.InvitationExpiresAt <= now
            ? TeamMemberStatuses.Expired
            : member.Status;
}
