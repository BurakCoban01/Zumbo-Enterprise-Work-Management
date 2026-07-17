using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed partial class TeamService
{
    public async Task<TeamMemberPageResponse> ListMembersAsync(
        string teamId,
        string? afterMemberId,
        int pageSize,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);
        var cursor = string.IsNullOrWhiteSpace(afterMemberId) ? null : afterMemberId.Trim();
        var candidates = team.Members
            .OrderBy(member => member.Id, StringComparer.Ordinal)
            .Where(member => cursor is null || string.CompareOrdinal(member.Id, cursor) > 0)
            .Take(boundedPageSize + 1)
            .ToList();
        var hasNext = candidates.Count > boundedPageSize;
        var page = candidates.Take(boundedPageSize).ToList();
        var items = new List<TeamMemberListItemResponse>(page.Count);
        foreach (var member in page)
        {
            var user = string.IsNullOrWhiteSpace(member.UserId)
                ? null
                : await userDirectory.FindByIdAsync(member.UserId, ct);
            if (user is not null
                && !string.Equals(user.OrganizationId, team.OrganizationId, StringComparison.Ordinal))
            {
                user = null;
            }

            items.Add(new TeamMemberListItemResponse(
                member.Id,
                member.UserId,
                member.Email,
                user?.DisplayName ?? member.Email,
                member.Role,
                EffectiveStatus(member, clock.UtcNow),
                member.InvitationExpiresAt));
        }

        return new TeamMemberPageResponse(
            items,
            hasNext ? items[^1].Id : null,
            boundedPageSize);
    }

    public async Task<TeamResponse> ChangeMemberRoleAsync(
        string teamId,
        string memberUserId,
        ChangeTeamMemberRoleRequest request,
        CancellationToken ct) => await ChangeMemberRoleAsync(teamId, memberUserId, request, "none", ct);

    public async Task<TeamResponse> ChangeMemberRoleAsync(
        string teamId,
        string memberUserId,
        ChangeTeamMemberRoleRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        EnsureOwner(team);
        var member = GetActiveMember(team, memberUserId);
        if (member.Role == TeamRoles.Owner)
        {
            throw new ConflictException("TEAM_OWNER_ROLE_LOCKED", "Transfer ownership before changing the owner role.");
        }

        var oldRole = member.Role;
        member.Role = NormalizeAssignableRole(request.Role);
        await SaveAsync(team, ct);
        await audit.WriteAsync(
            "TeamMemberRoleChanged",
            team.Id,
            $"{member.UserId}:{oldRole}",
            $"{member.UserId}:{member.Role}",
            correlationId,
            ct);
        return ToResponse(team);
    }

    public async Task<TeamResponse> TransferOwnershipAsync(
        string teamId,
        TransferTeamOwnershipRequest request,
        CancellationToken ct) => await TransferOwnershipAsync(teamId, request, "none", ct);

    public async Task<TeamResponse> TransferOwnershipAsync(
        string teamId,
        TransferTeamOwnershipRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var owner = EnsureOwner(team);
        if (string.IsNullOrWhiteSpace(request.NewOwnerUserId))
        {
            throw new ValidationException("New owner user id is required.");
        }

        var newOwner = GetActiveMember(team, request.NewOwnerUserId);
        if (newOwner.Id == owner.Id)
        {
            throw new ConflictException("TEAM_OWNER_UNCHANGED", "The selected member already owns the team.");
        }

        await RequireEligibleUserAsync(newOwner.UserId!, team.OrganizationId, ct);
        owner.Role = TeamRoles.Admin;
        newOwner.Role = TeamRoles.Owner;
        await SaveAsync(team, ct);
        await audit.WriteAsync(
            "TeamOwnershipTransferred",
            team.Id,
            owner.UserId,
            newOwner.UserId,
            correlationId,
            ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> RemoveMemberAsync(
        string teamId,
        string userIdOrEmail,
        CancellationToken ct) => RemoveMemberAsync(teamId, userIdOrEmail, "none", ct);

    public async Task<TeamResponse> RemoveMemberAsync(
        string teamId,
        string userIdOrEmail,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var target = team.Members.SingleOrDefault(member =>
            (member.UserId == userIdOrEmail
                || member.Email.Equals(userIdOrEmail, StringComparison.OrdinalIgnoreCase))
            && member.Status is TeamMemberStatuses.Active or TeamMemberStatuses.Invited)
            ?? throw new NotFoundException("TEAM_MEMBER_NOT_FOUND", "Team member or invite was not found.");
        if (target.Role == TeamRoles.Owner)
        {
            throw new ConflictException("TEAM_LAST_OWNER", "Transfer ownership before removing the last owner.");
        }

        var isSelf = target.UserId == CurrentUserId();
        if (!isSelf)
        {
            var actor = EnsureOwnerOrAdmin(team);
            if (actor.Role == TeamRoles.Admin && target.Role == TeamRoles.Admin && !IsSystemAdmin())
            {
                throw new ForbiddenException("Team admins cannot remove another team admin.");
            }
        }

        team.Members.Remove(target);
        await SaveAsync(team, ct);
        await audit.WriteAsync(
            "TeamMemberRemoved",
            team.Id,
            $"{target.UserId ?? target.Email}:{target.Role}:{target.Status}",
            null,
            correlationId,
            ct);
        return ToResponse(team);
    }

    private static TeamMemberDocument GetActiveMember(TeamDocument team, string userId) =>
        team.Members.SingleOrDefault(member =>
            member.UserId == userId.Trim() && member.Status == TeamMemberStatuses.Active)
        ?? throw new NotFoundException("TEAM_MEMBER_NOT_FOUND", "Active team member was not found.");
}
