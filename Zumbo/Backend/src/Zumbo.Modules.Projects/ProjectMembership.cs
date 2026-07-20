using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class ProjectService
{
    public Task<ProjectResponse> AddMemberAsync(string projectId, AddProjectMemberRequest request, CancellationToken ct) =>
        AddMemberAsync(projectId, request, "none", ct);

    public async Task<ProjectResponse> AddMemberAsync(
        string projectId,
        AddProjectMemberRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var aggregate = ProjectMembershipAggregate.Rehydrate(project);
        var memberUserId = aggregate.EnsureCanAddMember(request.UserId);
        var role = ProjectMemberRole.Create(request.Role);
        if (role.IsProjectAdmin && EnsureOwnerOrAdmin(project).Role != ProjectRoles.Owner && !IsSystemAdmin())
        {
            throw new ForbiddenException("Only the project owner can grant the ProjectAdmin role.");
        }

        await memberDirectory.EnsureEligibleAsync(memberUserId, project.OrganizationId, ct);
        var domainEvent = aggregate.AddMember(memberUserId, role, clock.UtcNow);
        await SaveAsync(project, ct);
        await audit.WriteAsync(
            "ProjectMemberAdded",
            project.Id,
            null,
            $"{domainEvent.UserId}:{domainEvent.Role}",
            correlationId,
            ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> ChangeMemberRoleAsync(
        string projectId,
        string memberUserId,
        ChangeProjectMemberRoleRequest request,
        CancellationToken ct) =>
        await ChangeMemberRoleAsync(projectId, memberUserId, request, "none", ct);

    public async Task<ProjectResponse> ChangeMemberRoleAsync(
        string projectId,
        string memberUserId,
        ChangeProjectMemberRoleRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwner(project);
        var aggregate = ProjectMembershipAggregate.Rehydrate(project);
        var domainEvent = aggregate.ChangeMemberRole(memberUserId, request.Role, clock.UtcNow);
        await SaveAsync(project, ct);
        await audit.WriteAsync(
            "ProjectMemberRoleChanged",
            project.Id,
            $"{domainEvent.UserId}:{domainEvent.PreviousRole}",
            $"{domainEvent.UserId}:{domainEvent.Role}",
            correlationId,
            ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> TransferOwnershipAsync(
        string projectId,
        TransferProjectOwnershipRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        var owner = EnsureOwner(project);
        var newOwnerUserId = request.NewOwnerUserId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newOwnerUserId))
        {
            throw new ValidationException("New owner user id is required.");
        }

        var newOwner = project.Members.SingleOrDefault(member => member.UserId == newOwnerUserId)
            ?? throw new NotFoundException("PROJECT_MEMBER_NOT_FOUND", "New owner must already be a project member.");
        if (newOwner.UserId == owner.UserId)
        {
            throw new ConflictException("PROJECT_OWNER_UNCHANGED", "The selected member already owns the project.");
        }

        await memberDirectory.EnsureEligibleAsync(newOwner.UserId, project.OrganizationId, ct);
        owner.Role = ProjectRoles.Admin;
        newOwner.Role = ProjectRoles.Owner;
        await SaveAsync(project, ct);
        await audit.WriteAsync(
            "ProjectOwnershipTransferred",
            project.Id,
            owner.UserId,
            newOwner.UserId,
            correlationId,
            ct);
        return ToResponse(project);
    }

    public Task<ProjectResponse> RemoveMemberAsync(string projectId, string memberUserId, CancellationToken ct) =>
        RemoveMemberAsync(projectId, memberUserId, "none", ct);

    public async Task<ProjectResponse> RemoveMemberAsync(
        string projectId,
        string memberUserId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var aggregate = ProjectMembershipAggregate.Rehydrate(project);
        var domainEvent = aggregate.RemoveMember(memberUserId, CurrentUserId(), clock.UtcNow);
        await SaveAsync(project, ct);
        await audit.WriteAsync(
            "ProjectMemberRemoved",
            project.Id,
            $"{domainEvent.UserId}:{domainEvent.Role}",
            null,
            correlationId,
            ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> AddTeamAsync(
        string projectId,
        AddProjectTeamRequest request,
        CancellationToken ct) =>
        await AddTeamAsync(projectId, request, "none", ct);

    public async Task<ProjectResponse> AddTeamAsync(
        string projectId,
        AddProjectTeamRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        if (string.IsNullOrWhiteSpace(request.TeamId))
        {
            throw new ValidationException("Team id is required.");
        }

        var teamId = request.TeamId.Trim();
        if (project.TeamIds.Contains(teamId, StringComparer.Ordinal))
        {
            throw new ConflictException("PROJECT_TEAM_EXISTS", "Team is already linked to the project.");
        }

        var team = await teamDirectory.FindAsync(teamId, ct)
            ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
        if (!team.IsActive || !string.Equals(team.OrganizationId, project.OrganizationId, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "PROJECT_TEAM_ORGANIZATION_MISMATCH",
                "Project teams must be active and belong to the project organization.");
        }

        project.TeamIds.Add(teamId);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectTeamLinked", project.Id, null, teamId, correlationId, ct);
        return ToResponse(project);
    }

    public Task<ProjectResponse> RemoveTeamAsync(string projectId, string teamId, CancellationToken ct) =>
        RemoveTeamAsync(projectId, teamId, "none", ct);

    public async Task<ProjectResponse> RemoveTeamAsync(
        string projectId,
        string teamId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var normalizedTeamId = teamId.Trim();
        if (!project.TeamIds.Contains(normalizedTeamId))
        {
            throw new NotFoundException("PROJECT_TEAM_NOT_FOUND", "Project team link was not found.");
        }

        if (await teamUsageChecker.HasWorkItemsAsync(project.Id, normalizedTeamId, ct))
        {
            throw new ConflictException(
                "PROJECT_TEAM_IN_USE",
                "A team assigned to work items cannot be unlinked from the project.");
        }

        project.TeamIds.Remove(normalizedTeamId);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectTeamUnlinked", project.Id, normalizedTeamId, null, correlationId, ct);
        return ToResponse(project);
    }
}
