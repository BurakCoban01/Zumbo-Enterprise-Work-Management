using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

internal sealed class CreateTeamSlice(
    IDocumentRepository<TeamDocument> teams,
    ITeamUserDirectory userDirectory,
    ITeamOrganizationDirectory organizationDirectory,
    ITeamAuditWriter audit,
    IClock clock,
    ICurrentUser currentUser)
{
    internal async Task<TeamResponse> HandleAsync(
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
        return TeamResponseMapper.ToResponse(team, clock);
    }

    private async Task<TeamUserDirectoryEntry> RequireEligibleUserAsync(
        string userId,
        string organizationId,
        CancellationToken ct)
    {
        var user = await userDirectory.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "Team user was not found.");
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

        return user;
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
}
