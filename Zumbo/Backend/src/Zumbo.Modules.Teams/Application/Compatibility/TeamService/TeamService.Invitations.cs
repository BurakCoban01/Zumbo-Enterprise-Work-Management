using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed partial class TeamService
{
    public Task<TeamResponse> InviteAsync(
        string teamId,
        InviteTeamMemberRequest request,
        CancellationToken ct) => InviteAsync(teamId, request, "none", ct);

    public async Task<TeamResponse> InviteAsync(
        string teamId,
        InviteTeamMemberRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var actor = EnsureOwnerOrAdmin(team);
        var email = NormalizeEmail(request.Email);
        var role = NormalizeAssignableRole(request.Role);
        if (role == TeamRoles.Admin && actor.Role != TeamRoles.Owner && !IsSystemAdmin())
        {
            throw new ForbiddenException("Only the team owner can invite an admin.");
        }

        ExpirePendingInvites(team);
        if (team.Members.Any(member =>
            member.Email.Equals(email, StringComparison.OrdinalIgnoreCase)
            && member.Status is TeamMemberStatuses.Active or TeamMemberStatuses.Invited))
        {
            throw new ConflictException("TEAM_MEMBER_EXISTS", "Member or active invite already exists.");
        }

        var knownUser = await userDirectory.FindByEmailAsync(email, ct);
        if (knownUser is not null)
        {
            EnsureDirectoryUserEligible(knownUser, team.OrganizationId);
            if (team.Members.Any(member =>
                member.UserId == knownUser.Id && member.Status == TeamMemberStatuses.Active))
            {
                throw new ConflictException("TEAM_MEMBER_EXISTS", "User is already an active team member.");
            }
        }

        var token = TeamInviteTokenSecurity.Create();
        var invite = new TeamMemberDocument
        {
            UserId = knownUser?.Id,
            Email = email,
            Role = role,
            Status = TeamMemberStatuses.Invited,
            InvitationTokenHash = TeamInviteTokenSecurity.Hash(token),
            InvitedAt = clock.UtcNow,
            InvitationExpiresAt = clock.UtcNow.AddDays(7)
        };
        team.Members.Add(invite);
        await SaveAsync(team, ct);
        await audit.WriteAsync(
            "TeamMemberInvited",
            team.Id,
            null,
            $"{invite.UserId ?? invite.Email}:{invite.Role}",
            correlationId,
            ct);
        if (knownUser is not null)
        {
            await invitationNotifier.NotifyAsync(
                team.OrganizationId,
                knownUser.Id,
                team.Id,
                invite.Id,
                team.Name,
                CurrentUserId(),
                correlationId,
                ct);
        }

        return ToResponse(team, token);
    }

    public async Task<TeamResponse> AcceptInviteAsync(
        string teamId,
        TeamInviteTokenRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var invite = FindInviteByToken(team, request.Token);
        var user = await RequireEligibleUserAsync(CurrentUserId(), team.OrganizationId, ct);
        EnsureInviteRecipient(invite, user);
        EnsurePendingAndUnexpired(invite);
        if (team.Members.Any(member =>
            member.Id != invite.Id
            && member.UserId == user.Id
            && member.Status == TeamMemberStatuses.Active))
        {
            throw new ConflictException("TEAM_MEMBER_EXISTS", "User is already an active team member.");
        }

        invite.UserId = user.Id;
        invite.Status = TeamMemberStatuses.Active;
        invite.InvitationExpiresAt = null;
        invite.RespondedAt = clock.UtcNow;
        await SaveAsync(team, ct);
        await audit.WriteAsync(
            "TeamInviteAccepted",
            team.Id,
            TeamMemberStatuses.Invited,
            $"{invite.UserId}:{TeamMemberStatuses.Active}",
            correlationId,
            ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> AcceptInviteAsync(
        string teamId,
        TeamInviteTokenRequest request,
        CancellationToken ct) => AcceptInviteAsync(teamId, request, "none", ct);

    public async Task<TeamResponse> DeclineInviteAsync(
        string teamId,
        TeamInviteTokenRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var invite = FindInviteByToken(team, request.Token);
        var user = await RequireEligibleUserAsync(CurrentUserId(), team.OrganizationId, ct);
        EnsureInviteRecipient(invite, user);
        EnsurePendingAndUnexpired(invite);
        invite.UserId = user.Id;
        invite.Status = TeamMemberStatuses.Declined;
        invite.InvitationExpiresAt = null;
        invite.RespondedAt = clock.UtcNow;
        await SaveAsync(team, ct);
        await audit.WriteAsync(
            "TeamInviteDeclined",
            team.Id,
            TeamMemberStatuses.Invited,
            $"{invite.UserId}:{TeamMemberStatuses.Declined}",
            correlationId,
            ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> DeclineInviteAsync(
        string teamId,
        TeamInviteTokenRequest request,
        CancellationToken ct) => DeclineInviteAsync(teamId, request, "none", ct);

    public async Task<TeamResponse> RevokeInviteAsync(
        string teamId,
        string inviteId,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var actor = EnsureOwnerOrAdmin(team);
        var invite = team.Members.SingleOrDefault(member => member.Id == inviteId)
            ?? throw new NotFoundException("TEAM_INVITE_NOT_FOUND", "Team invite was not found.");
        if (invite.Status != TeamMemberStatuses.Invited)
        {
            throw new ConflictException("TEAM_INVITE_NOT_PENDING", "Only a pending team invite can be revoked.");
        }

        if (actor.Role == TeamRoles.Admin && invite.Role == TeamRoles.Admin && !IsSystemAdmin())
        {
            throw new ForbiddenException("Team admins cannot revoke an admin invitation.");
        }

        invite.Status = invite.InvitationExpiresAt <= clock.UtcNow
            ? TeamMemberStatuses.Expired
            : TeamMemberStatuses.Revoked;
        invite.InvitationExpiresAt = null;
        invite.RespondedAt = clock.UtcNow;
        await SaveAsync(team, ct);
        await audit.WriteAsync(
            invite.Status == TeamMemberStatuses.Expired ? "TeamInviteExpired" : "TeamInviteRevoked",
            team.Id,
            TeamMemberStatuses.Invited,
            $"{invite.UserId ?? invite.Email}:{invite.Status}",
            correlationId,
            ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> RevokeInviteAsync(
        string teamId,
        string inviteId,
        CancellationToken ct) => RevokeInviteAsync(teamId, inviteId, "none", ct);

    private void ExpirePendingInvites(TeamDocument team)
    {
        foreach (var invite in team.Members.Where(member =>
            member.Status == TeamMemberStatuses.Invited
            && member.InvitationExpiresAt <= clock.UtcNow))
        {
            invite.Status = TeamMemberStatuses.Expired;
            invite.InvitationExpiresAt = null;
            invite.RespondedAt = clock.UtcNow;
        }
    }

    private static TeamMemberDocument FindInviteByToken(TeamDocument team, string token)
    {
        var invite = team.Members.SingleOrDefault(member =>
            TeamInviteTokenSecurity.Matches(member.InvitationTokenHash, token));
        return invite
            ?? throw new NotFoundException("TEAM_INVITE_NOT_FOUND", "Team invite token was not found.");
    }

    private void EnsurePendingAndUnexpired(TeamMemberDocument invite)
    {
        if (invite.Status != TeamMemberStatuses.Invited)
        {
            throw new ConflictException("TEAM_INVITE_NOT_PENDING", "Team invite is no longer pending.");
        }

        if (invite.InvitationExpiresAt <= clock.UtcNow)
        {
            throw new ConflictException("TEAM_INVITE_EXPIRED", "Team invite has expired.");
        }
    }

    private static void EnsureInviteRecipient(TeamMemberDocument invite, TeamUserDirectoryEntry user)
    {
        if (!invite.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Only the invited user can respond to this invite.");
        }
    }

    private static string NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length > 254 || !normalized.Contains('@'))
        {
            throw new ValidationException("A valid team member email is required.");
        }

        return normalized;
    }

    private static string NormalizeAssignableRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)
            || string.Equals(role, TeamRoles.Member, StringComparison.OrdinalIgnoreCase))
        {
            return TeamRoles.Member;
        }

        if (string.Equals(role, TeamRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return TeamRoles.Admin;
        }

        throw new ValidationException("Team role must be Admin or Member.");
    }
}
