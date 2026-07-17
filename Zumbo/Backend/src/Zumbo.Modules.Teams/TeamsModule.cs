using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed partial class TeamService
{
    private readonly IDocumentRepository<TeamDocument> teams;
    private readonly ITeamUserDirectory userDirectory;
    private readonly ITeamOrganizationDirectory organizationDirectory;
    private readonly ITeamAuditWriter audit;
    private readonly ITeamInvitationNotifier invitationNotifier;
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ExpectedVersionState expectedVersion;

    public TeamService(
        IDocumentRepository<TeamDocument> teams,
        ITeamUserDirectory userDirectory,
        ITeamAuditWriter audit,
        IClock clock,
        ICurrentUser currentUser,
        IExpectedVersionAccessor? expectedVersions = null,
        ITeamOrganizationDirectory? organizationDirectory = null,
        ITeamInvitationNotifier? invitationNotifier = null)
    {
        this.teams = teams;
        this.userDirectory = userDirectory;
        this.audit = audit;
        this.clock = clock;
        this.currentUser = currentUser;
        expectedVersion = new ExpectedVersionState(expectedVersions);
        this.organizationDirectory = organizationDirectory ?? AllowActiveTeamOrganizationDirectory.Instance;
        this.invitationNotifier = invitationNotifier ?? NoOpTeamInvitationNotifier.Instance;
    }

    public Task<TeamResponse> CreateAsync(CreateTeamRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<TeamResponse> CreateAsync(
        CreateTeamRequest request,
        string correlationId,
        CancellationToken ct)
    {
        CreateTeamValidator.Validate(request);
        var organizationId = request.OrganizationId.Trim();
        EnsureOrganizationScope(organizationId);
        await organizationDirectory.EnsureActiveAsync(organizationId, ct);
        var userId = CurrentUserId();
        if (!IsSystemAdmin() && !string.Equals(request.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("A team can only be created for the authenticated owner.");
        }

        var owner = await RequireEligibleUserAsync(request.OwnerUserId.Trim(), organizationId, ct);
        var name = NormalizeName(request.Name);
        var normalizedName = name.ToLowerInvariant();
        if (await teams.ExistsByFilterAsync(
            team => team.OrganizationId == organizationId && team.Name.ToLower() == normalizedName,
            ct))
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
                    Role = TeamRoles.Owner,
                    Status = TeamMemberStatuses.Active
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
        var normalizedOrganizationId = organizationId.Trim();
        await organizationDirectory.EnsureActiveAsync(normalizedOrganizationId, ct);
        var result = await teams.ListByFilterAsync(
            team => team.OrganizationId == normalizedOrganizationId && team.Archived == archived,
            team => team.Name,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(team => ToResponse(team)).ToList();
    }

    public Task<TeamResponse> UpdateAsync(string teamId, UpdateTeamRequest request, CancellationToken ct) =>
        UpdateAsync(teamId, request, "none", ct);

    public async Task<TeamResponse> UpdateAsync(
        string teamId,
        UpdateTeamRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var team = await GetTeam(teamId, ct);
        EnsureOwnerOrAdmin(team);
        var name = NormalizeName(request.Name);
        var normalizedName = name.ToLowerInvariant();
        if (await teams.ExistsByFilterAsync(
            candidate => candidate.Id != team.Id
                && candidate.OrganizationId == team.OrganizationId
                && candidate.Name.ToLower() == normalizedName,
            ct))
        {
            throw new ConflictException("TEAM_NAME_EXISTS", "Team name must be unique inside the organization.");
        }

        var oldName = team.Name;
        team.Name = name;
        await SaveAsync(team, ct);
        await audit.WriteAsync("TeamUpdated", team.Id, oldName, team.Name, correlationId, ct);
        return ToResponse(team);
    }

    private async Task<TeamDocument> GetTeam(string teamId, CancellationToken ct)
    {
        var team = await teams.SelectAsync(candidate => candidate.Id == teamId && !candidate.Archived, ct)
            ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
        EnsureOrganizationScope(team.OrganizationId);
        await organizationDirectory.EnsureActiveAsync(team.OrganizationId, ct);
        return team;
    }

    private async Task<TeamDocument> GetArchivedTeam(string teamId, CancellationToken ct)
    {
        var team = await teams.SelectAsync(candidate => candidate.Id == teamId && candidate.Archived, ct)
            ?? throw new NotFoundException("TEAM_NOT_FOUND", "Archived team was not found.");
        EnsureOrganizationScope(team.OrganizationId);
        await organizationDirectory.EnsureActiveAsync(team.OrganizationId, ct);
        return team;
    }

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
            throw new ConflictException("USER_INACTIVE", "Inactive users cannot join or own teams.");
        }

        if (!string.Equals(user.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "TEAM_MEMBER_ORGANIZATION_MISMATCH",
                "Team members must belong to the team organization.");
        }
    }

    private TeamMemberDocument EnsureOwnerOrAdmin(TeamDocument team)
    {
        if (IsSystemAdmin())
        {
            return new TeamMemberDocument
            {
                UserId = CurrentUserId(),
                Role = TeamRoles.Owner,
                Status = TeamMemberStatuses.Active
            };
        }

        var actor = team.Members.SingleOrDefault(member =>
            member.UserId == CurrentUserId() && member.Status == TeamMemberStatuses.Active)
            ?? throw new ForbiddenException("User is not an active team member.");
        if (actor.Role is not (TeamRoles.Owner or TeamRoles.Admin))
        {
            throw new ForbiddenException("Team owner or admin role is required.");
        }

        return actor;
    }

    private TeamMemberDocument EnsureOwner(TeamDocument team)
    {
        var owners = team.Members.Where(member =>
            member.Role == TeamRoles.Owner && member.Status == TeamMemberStatuses.Active).ToList();
        if (owners.Count != 1)
        {
            throw new ConflictException("TEAM_OWNER_INVARIANT", "A team must have exactly one active owner.");
        }

        if (IsSystemAdmin())
        {
            return owners[0];
        }

        return owners.SingleOrDefault(owner => owner.UserId == CurrentUserId())
            ?? throw new ForbiddenException("Team owner role is required.");
    }

    private async Task SaveAsync(TeamDocument team, CancellationToken ct)
    {
        EnsureExactlyOneOwner(team);
        team.UpdatedAt = clock.UtcNow;
        var result = await teams.ReplaceByVersionAsync(
            candidate => candidate.Id == team.Id,
            team,
            expectedVersion.Consume(team.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
        }

        team.Version = result.Version!.Value;
    }

    private static void EnsureExactlyOneOwner(TeamDocument team)
    {
        if (team.Members.Count(member =>
            member.Status == TeamMemberStatuses.Active && member.Role == TeamRoles.Owner) != 1)
        {
            throw new ConflictException("TEAM_OWNER_INVARIANT", "A team must have exactly one active owner.");
        }
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

    private bool IsSystemAdmin() => PermissionCatalog.IsSystemAdministrator(currentUser.Roles);

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 100)
        {
            throw new ValidationException("Team name must contain 2-100 characters.");
        }

        return normalized;
    }

    private TeamResponse ToResponse(TeamDocument team, string? invitationToken = null) =>
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
