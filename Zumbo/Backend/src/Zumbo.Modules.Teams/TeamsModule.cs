using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed record CreateTeamRequest(string OrganizationId, string Name, string OwnerUserId);
public sealed record UpdateTeamRequest(string Name);
public sealed record InviteTeamMemberRequest(string Email, string Role);
public sealed record ChangeTeamMemberRoleRequest(string Role);
public sealed record TransferTeamOwnershipRequest(string NewOwnerUserId);
public sealed record TeamUserDirectoryEntry(string Id, string Email, string OrganizationId, bool IsActive);
public sealed record TeamResponse(
    string Id,
    string OrganizationId,
    string Name,
    IReadOnlyCollection<TeamMemberResponse> Members,
    bool Archived = false);
public sealed record TeamMemberResponse(
    string Id,
    string? UserId,
    string Email,
    string Role,
    string Status,
    DateTimeOffset? InvitationExpiresAt,
    DateTimeOffset? RespondedAt);

public interface ITeamUserDirectory
{
    Task<TeamUserDirectoryEntry?> FindByIdAsync(string userId, CancellationToken ct);
    Task<TeamUserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken ct);
}

public interface ITeamAuditWriter
{
    Task WriteAsync(string action, string entityId, string? oldValue, string? newValue, string correlationId, CancellationToken ct);
}

public sealed class TeamDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Archived { get; set; }
    public List<TeamMemberDocument> Members { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TeamMemberDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";
    public string Status { get; set; } = "Active";
    public DateTimeOffset? InvitationExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}

public sealed class TeamService(
    IDocumentRepository<TeamDocument> teams,
    ITeamUserDirectory userDirectory,
    ITeamAuditWriter audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public Task<TeamResponse> CreateAsync(CreateTeamRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<TeamResponse> CreateAsync(CreateTeamRequest request, string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            throw new ValidationException("Organization id, team name and owner user id are required.");
        }

        var organizationId = request.OrganizationId.Trim();
        EnsureOrganizationScope(organizationId);
        var userId = CurrentUserId();
        if (!IsSystemAdmin() && !string.Equals(request.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("A team can only be created for the authenticated owner.");
        }

        var owner = await RequireEligibleUserAsync(request.OwnerUserId.Trim(), organizationId, ct);
        var name = NormalizeName(request.Name);
        var normalizedName = name.ToLowerInvariant();
        var duplicate = await teams.SelectAsync(
            x => x.OrganizationId == organizationId && x.Name.ToLower() == normalizedName,
            ct);
        if (duplicate is not null)
        {
            throw new ConflictException("TEAM_NAME_EXISTS", "Team name must be unique inside the organization.");
        }

        var now = clock.UtcNow;
        var team = new TeamDocument
        {
            OrganizationId = organizationId,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            Members =
            [
                new TeamMemberDocument
                {
                    UserId = owner.Id,
                    Email = owner.Email,
                    Role = "Owner",
                    Status = "Active"
                }
            ]
        };

        await teams.CreateAsync(team, ct);
        await audit.WriteAsync("TeamCreated", team.Id, null, team.Name, correlationId, ct);
        return ToResponse(team);
    }

    public async Task<IReadOnlyList<TeamResponse>> ListAsync(
        string organizationId,
        CancellationToken ct,
        bool archived = false)
    {
        EnsureOrganizationScope(organizationId);
        var result = await teams.ListByFilterAsync(
            x => x.OrganizationId == organizationId.Trim() && x.Archived == archived,
            x => x.Name,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public Task<TeamResponse> UpdateAsync(string teamId, UpdateTeamRequest request, CancellationToken ct) =>
        UpdateAsync(teamId, request, "none", ct);

    public async Task<TeamResponse> UpdateAsync(string teamId, UpdateTeamRequest request, string correlationId, CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        EnsureOwnerOrAdmin(team);
        var name = NormalizeName(request.Name);
        var normalizedName = name.ToLowerInvariant();
        var duplicate = await teams.SelectAsync(
            x => x.Id != team.Id && x.OrganizationId == team.OrganizationId && x.Name.ToLower() == normalizedName,
            ct);
        if (duplicate is not null)
        {
            throw new ConflictException("TEAM_NAME_EXISTS", "Team name must be unique inside the organization.");
        }

        var oldName = team.Name;
        team.Name = name;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamUpdated", team.Id, oldName, team.Name, correlationId, ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> InviteAsync(string teamId, InviteTeamMemberRequest request, CancellationToken ct) =>
        InviteAsync(teamId, request, "none", ct);

    public async Task<TeamResponse> InviteAsync(string teamId, InviteTeamMemberRequest request, string correlationId, CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var actor = EnsureOwnerOrAdmin(team);
        var email = NormalizeEmail(request.Email);
        var role = NormalizeAssignableRole(request.Role);
        if (role == "Admin" && actor.Role != "Owner" && !IsSystemAdmin())
        {
            throw new ForbiddenException("Only the team owner can invite an admin.");
        }

        foreach (var expiredInvite in team.Members.Where(x =>
            x.Status == "Invited" && x.InvitationExpiresAt <= clock.UtcNow))
        {
            expiredInvite.Status = "Expired";
            expiredInvite.RespondedAt = clock.UtcNow;
        }

        if (team.Members.Any(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase)
            && x.Status is "Active" or "Invited"))
        {
            throw new ConflictException("TEAM_MEMBER_EXISTS", "Member or active invite already exists.");
        }

        var knownUser = await userDirectory.FindByEmailAsync(email, ct);
        if (knownUser is not null)
        {
            EnsureDirectoryUserEligible(knownUser, team.OrganizationId);
            if (team.Members.Any(x => x.UserId == knownUser.Id && x.Status == "Active"))
            {
                throw new ConflictException("TEAM_MEMBER_EXISTS", "User is already an active team member.");
            }
        }

        var invite = new TeamMemberDocument
        {
            UserId = knownUser?.Id,
            Email = email,
            Role = role,
            Status = "Invited",
            InvitationExpiresAt = clock.UtcNow.AddDays(7)
        };
        team.Members.Add(invite);
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamMemberInvited", team.Id, null, $"{invite.UserId ?? invite.Email}:{invite.Role}", correlationId, ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> AcceptInviteAsync(string teamId, string inviteId, CancellationToken ct) =>
        AcceptInviteAsync(teamId, inviteId, "none", ct);

    public async Task<TeamResponse> AcceptInviteAsync(string teamId, string inviteId, string correlationId, CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var invite = GetPendingInvite(team, inviteId);
        var user = await RequireEligibleUserAsync(CurrentUserId(), team.OrganizationId, ct);
        EnsureInviteRecipient(invite, user);
        await EnsureInviteIsNotExpiredAsync(invite, team, ct);

        invite.UserId = user.Id;
        invite.Status = "Active";
        invite.InvitationExpiresAt = null;
        invite.RespondedAt = clock.UtcNow;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamInviteAccepted", team.Id, "Invited", $"{invite.UserId}:Active", correlationId, ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> RejectInviteAsync(string teamId, string inviteId, CancellationToken ct) =>
        RejectInviteAsync(teamId, inviteId, "none", ct);

    public async Task<TeamResponse> RejectInviteAsync(string teamId, string inviteId, string correlationId, CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var invite = GetPendingInvite(team, inviteId);
        var user = await RequireEligibleUserAsync(CurrentUserId(), team.OrganizationId, ct);
        EnsureInviteRecipient(invite, user);
        await EnsureInviteIsNotExpiredAsync(invite, team, ct);

        invite.UserId = user.Id;
        invite.Status = "Rejected";
        invite.InvitationExpiresAt = null;
        invite.RespondedAt = clock.UtcNow;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamInviteRejected", team.Id, "Invited", $"{invite.UserId}:Rejected", correlationId, ct);
        return ToResponse(team);
    }

    public async Task<TeamResponse> ChangeMemberRoleAsync(
        string teamId,
        string memberUserId,
        ChangeTeamMemberRoleRequest request,
        CancellationToken ct)
        => await ChangeMemberRoleAsync(teamId, memberUserId, request, "none", ct);

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
        if (member.Role == "Owner")
        {
            throw new ConflictException("TEAM_OWNER_ROLE_LOCKED", "Transfer ownership before changing the owner role.");
        }

        var oldRole = member.Role;
        member.Role = NormalizeAssignableRole(request.Role);
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamMemberRoleChanged", team.Id, $"{member.UserId}:{oldRole}", $"{member.UserId}:{member.Role}", correlationId, ct);
        return ToResponse(team);
    }

    public async Task<TeamResponse> TransferOwnershipAsync(
        string teamId,
        TransferTeamOwnershipRequest request,
        CancellationToken ct)
        => await TransferOwnershipAsync(teamId, request, "none", ct);

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

        owner.Role = "Admin";
        newOwner.Role = "Owner";
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamOwnershipTransferred", team.Id, owner.UserId, newOwner.UserId, correlationId, ct);
        return ToResponse(team);
    }

    public Task<TeamResponse> RemoveMemberAsync(string teamId, string userIdOrEmail, CancellationToken ct) =>
        RemoveMemberAsync(teamId, userIdOrEmail, "none", ct);

    public async Task<TeamResponse> RemoveMemberAsync(
        string teamId,
        string userIdOrEmail,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        var target = team.Members.SingleOrDefault(x =>
            (x.UserId == userIdOrEmail || x.Email.Equals(userIdOrEmail, StringComparison.OrdinalIgnoreCase))
            && x.Status is "Active" or "Invited")
            ?? throw new NotFoundException("TEAM_MEMBER_NOT_FOUND", "Team member or invite was not found.");
        if (target.Role == "Owner")
        {
            throw new ConflictException("TEAM_OWNER_REMOVE_FORBIDDEN", "Transfer ownership before removing the owner.");
        }

        var isSelf = target.UserId == CurrentUserId();
        if (!isSelf)
        {
            var actor = EnsureOwnerOrAdmin(team);
            if (actor.Role == "Admin" && target.Role == "Admin" && !IsSystemAdmin())
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

    public Task ArchiveAsync(string teamId, CancellationToken ct) => ArchiveAsync(teamId, "none", ct);

    public async Task ArchiveAsync(string teamId, string correlationId, CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        EnsureOwner(team);
        team.Archived = true;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamArchived", team.Id, "active", "archived", correlationId, ct);
    }

    public async Task<TeamResponse> RestoreAsync(string teamId, string correlationId, CancellationToken ct)
    {
        var team = await GetArchivedTeam(teamId, ct);
        EnsureOwner(team);
        team.Archived = false;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamRestored", team.Id, "archived", "active", correlationId, ct);
        return ToResponse(team);
    }

    private async Task<TeamDocument> GetTeam(string teamId, CancellationToken ct) =>
        await teams.SelectAsync(x => x.Id == teamId && !x.Archived, ct)
        ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");

    private async Task<TeamDocument> GetArchivedTeam(string teamId, CancellationToken ct) =>
        await teams.SelectAsync(x => x.Id == teamId && x.Archived, ct)
        ?? throw new NotFoundException("TEAM_NOT_FOUND", "Archived team was not found.");

    private async Task<TeamUserDirectoryEntry> RequireEligibleUserAsync(
        string userId,
        string organizationId,
        CancellationToken ct)
    {
        var user = await userDirectory.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "Team user was not found.");
        EnsureDirectoryUserEligible(user, organizationId);
        return user;
    }

    private static void EnsureDirectoryUserEligible(TeamUserDirectoryEntry user, string organizationId)
    {
        if (!user.IsActive)
        {
            throw new ConflictException("USER_INACTIVE", "Inactive users cannot join teams.");
        }

        if (!string.Equals(user.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new ConflictException("TEAM_MEMBER_ORGANIZATION_MISMATCH", "Team members must belong to the team organization.");
        }
    }

    private static TeamMemberDocument GetPendingInvite(TeamDocument team, string inviteId) =>
        team.Members.SingleOrDefault(x => x.Id == inviteId && x.Status == "Invited")
        ?? throw new NotFoundException("TEAM_INVITE_NOT_FOUND", "Pending team invite was not found.");

    private static TeamMemberDocument GetActiveMember(TeamDocument team, string userId) =>
        team.Members.SingleOrDefault(x => x.UserId == userId.Trim() && x.Status == "Active")
        ?? throw new NotFoundException("TEAM_MEMBER_NOT_FOUND", "Active team member was not found.");

    private static void EnsureInviteRecipient(TeamMemberDocument invite, TeamUserDirectoryEntry user)
    {
        if (!invite.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Only the invited user can respond to this invite.");
        }
    }

    private async Task EnsureInviteIsNotExpiredAsync(
        TeamMemberDocument invite,
        TeamDocument team,
        CancellationToken ct)
    {
        if (invite.InvitationExpiresAt > clock.UtcNow)
        {
            return;
        }

        invite.Status = "Expired";
        invite.RespondedAt = clock.UtcNow;
        await SaveAsync(team, ct);
        throw new ConflictException("TEAM_INVITE_EXPIRED", "Team invite has expired.");
    }

    private TeamMemberDocument EnsureOwnerOrAdmin(TeamDocument team)
    {
        if (IsSystemAdmin())
        {
            return new TeamMemberDocument { UserId = CurrentUserId(), Role = "Owner", Status = "Active" };
        }

        var actor = team.Members.SingleOrDefault(x => x.UserId == CurrentUserId() && x.Status == "Active")
            ?? throw new ForbiddenException("User is not an active team member.");
        if (actor.Role is not ("Owner" or "Admin"))
        {
            throw new ForbiddenException("Team owner or admin role is required.");
        }

        return actor;
    }

    private TeamMemberDocument EnsureOwner(TeamDocument team)
    {
        if (IsSystemAdmin())
        {
            return team.Members.Single(x => x.Role == "Owner" && x.Status == "Active");
        }

        return team.Members.SingleOrDefault(x =>
            x.UserId == CurrentUserId() && x.Role == "Owner" && x.Status == "Active")
            ?? throw new ForbiddenException("Team owner role is required.");
    }

    private async Task SaveAsync(TeamDocument team, CancellationToken ct)
    {
        team.UpdatedAt = clock.UtcNow;
        await teams.ReplaceByFilterAsync(x => x.Id == team.Id, team, ct);
    }

    private void EnsureOrganizationScope(string organizationId)
    {
        if (!IsSystemAdmin()
            && !string.Equals(currentUser.OrganizationId, organizationId.Trim(), StringComparison.Ordinal))
        {
            throw new ForbiddenException("User cannot access teams outside the current organization.");
        }
    }

    private string CurrentUserId() =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");

    private bool IsSystemAdmin() =>
        currentUser.Roles.Any(x => x.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 100)
        {
            throw new ValidationException("Team name must contain 2-100 characters.");
        }

        return normalized;
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length > 254 || !normalized.Contains('@'))
        {
            throw new ValidationException("A valid team member email is required.");
        }

        return normalized;
    }

    private static string NormalizeAssignableRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role) || string.Equals(role, "Member", StringComparison.OrdinalIgnoreCase))
        {
            return "Member";
        }

        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return "Admin";
        }

        throw new ValidationException("Team role must be Admin or Member.");
    }

    private static TeamResponse ToResponse(TeamDocument team) =>
        new(
            team.Id,
            team.OrganizationId,
            team.Name,
            team.Members.Select(x => new TeamMemberResponse(
                x.Id,
                x.UserId,
                x.Email,
                x.Role,
                x.Status,
                x.InvitationExpiresAt,
                x.RespondedAt)).ToList(),
            team.Archived);
}
