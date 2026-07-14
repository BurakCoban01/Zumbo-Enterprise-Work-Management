using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record CreateProjectRequest(string OrganizationId, string Key, string Name, string OwnerUserId);
public sealed record ProjectResponse(
    string Id,
    string OrganizationId,
    string Key,
    string Name,
    string Visibility,
    IReadOnlyCollection<ProjectMemberResponse> Members,
    IReadOnlyCollection<string> TeamIds,
    bool Archived = false);
public sealed record ProjectMemberResponse(string UserId, string Role);
public sealed record AddProjectMemberRequest(string UserId, string Role);
public sealed record UpdateProjectRequest(string Name, string Visibility);
public sealed record ChangeProjectMemberRoleRequest(string Role);
public sealed record AddProjectTeamRequest(string TeamId);

public interface IProjectMemberDirectory
{
    Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct);
}

public sealed record ProjectTeamDirectoryEntry(string Id, string OrganizationId, bool IsActive);

public interface IProjectTeamDirectory
{
    Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct);
}

public interface IProjectTeamUsageChecker
{
    Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct);
}

public interface IProjectAuditWriter
{
    Task WriteAsync(string action, string entityId, string? oldValue, string? newValue, string correlationId, CancellationToken ct);
}

public sealed class ProjectDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Visibility { get; set; } = "Internal";
    public bool Archived { get; set; }
    public List<ProjectMemberDocument> Members { get; set; } = [];
    public List<string> TeamIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProjectMemberDocument
{
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "Developer";
}

public sealed partial class ProjectService(
    IDocumentRepository<ProjectDocument> projects,
    IProjectMemberDirectory memberDirectory,
    IProjectTeamDirectory teamDirectory,
    IProjectTeamUsageChecker teamUsageChecker,
    IProjectAuditWriter audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.Key)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            throw new ValidationException("Organization id, project key and name are required.");
        }

        var organizationId = request.OrganizationId.Trim();
        EnsureOrganizationScope(organizationId);
        var userId = CurrentUserId();
        if (!IsSystemAdmin() && !string.Equals(request.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("A project can only be created for the authenticated owner.");
        }

        var key = request.Key.Trim().ToUpperInvariant();
        if (!ProjectKeyPattern().IsMatch(key))
        {
            throw new ValidationException("Project key must contain 2-10 upper-case letters, numbers or hyphens.");
        }

        var name = NormalizeName(request.Name);
        var duplicate = await projects.SelectAsync(x => x.OrganizationId == organizationId && x.Key == key, ct);
        if (duplicate is not null)
        {
            throw new ConflictException("PROJECT_KEY_EXISTS", "Project key must be unique inside the organization.");
        }

        var project = new ProjectDocument
        {
            OrganizationId = organizationId,
            Key = key,
            Name = name,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            Members =
            [
                new ProjectMemberDocument { UserId = request.OwnerUserId, Role = "ProjectOwner" }
            ]
        };

        await projects.CreateAsync(project, ct);
        await audit.WriteAsync("ProjectCreated", project.Id, null, $"{project.Key}:{project.Name}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<IReadOnlyList<ProjectResponse>> ListAsync(
        string organizationId,
        CancellationToken ct,
        bool archived = false)
    {
        EnsureOrganizationScope(organizationId);
        var userId = CurrentUserId();
        var result = await projects.ListByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.Archived == archived
                && (x.Visibility == "Internal" || x.Members.Any(member => member.UserId == userId)),
            x => x.Key,
            pageSize: 100,
            cancellationToken: ct);

        return result.Select(ToResponse).ToList();
    }

    public Task<ProjectResponse> AddMemberAsync(string projectId, AddProjectMemberRequest request, CancellationToken ct) =>
        AddMemberAsync(projectId, request, "none", ct);

    public async Task<ProjectResponse> AddMemberAsync(string projectId, AddProjectMemberRequest request, string correlationId, CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ValidationException("Project member user id is required.");
        }

        var memberUserId = request.UserId.Trim();
        if (project.Members.Any(x => x.UserId == memberUserId))
        {
            throw new ConflictException("PROJECT_MEMBER_EXISTS", "Project member already exists.");
        }

        var role = NormalizeAssignableRole(request.Role);
        if (role == "ProjectAdmin" && !IsOwner(project) && !IsSystemAdmin())
        {
            throw new ForbiddenException("Only the project owner can grant the ProjectAdmin role.");
        }

        await memberDirectory.EnsureEligibleAsync(memberUserId, project.OrganizationId, ct);
        project.Members.Add(new ProjectMemberDocument
        {
            UserId = memberUserId,
            Role = role
        });

        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        await audit.WriteAsync("ProjectMemberAdded", project.Id, null, $"{memberUserId}:{role}", correlationId, ct);
        return ToResponse(project);
    }

    public Task<ProjectResponse> UpdateAsync(string projectId, UpdateProjectRequest request, CancellationToken ct) =>
        UpdateAsync(projectId, request, "none", ct);

    public async Task<ProjectResponse> UpdateAsync(string projectId, UpdateProjectRequest request, string correlationId, CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var oldValue = $"{project.Name}:{project.Visibility}";
        project.Name = NormalizeName(request.Name);
        project.Visibility = NormalizeVisibility(request.Visibility);
        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        await audit.WriteAsync("ProjectUpdated", project.Id, oldValue, $"{project.Name}:{project.Visibility}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> AddTeamAsync(
        string projectId,
        AddProjectTeamRequest request,
        CancellationToken ct)
        => await AddTeamAsync(projectId, request, "none", ct);

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
            throw new ConflictException("PROJECT_TEAM_ORGANIZATION_MISMATCH", "Project teams must be active and belong to the project organization.");
        }

        project.TeamIds.Add(teamId);
        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        await audit.WriteAsync("ProjectTeamLinked", project.Id, null, teamId, correlationId, ct);
        return ToResponse(project);
    }

    public Task<ProjectResponse> RemoveTeamAsync(string projectId, string teamId, CancellationToken ct) =>
        RemoveTeamAsync(projectId, teamId, "none", ct);

    public async Task<ProjectResponse> RemoveTeamAsync(string projectId, string teamId, string correlationId, CancellationToken ct)
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
            throw new ConflictException("PROJECT_TEAM_IN_USE", "A team assigned to work items cannot be unlinked from the project.");
        }

        project.TeamIds.Remove(normalizedTeamId);
        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        await audit.WriteAsync("ProjectTeamUnlinked", project.Id, normalizedTeamId, null, correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> ChangeMemberRoleAsync(
        string projectId,
        string memberUserId,
        ChangeProjectMemberRoleRequest request,
        CancellationToken ct)
        => await ChangeMemberRoleAsync(projectId, memberUserId, request, "none", ct);

    public async Task<ProjectResponse> ChangeMemberRoleAsync(
        string projectId,
        string memberUserId,
        ChangeProjectMemberRoleRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwner(project);
        var normalizedMemberUserId = memberUserId.Trim();
        var member = project.Members.SingleOrDefault(x => x.UserId == normalizedMemberUserId)
            ?? throw new NotFoundException("PROJECT_MEMBER_NOT_FOUND", "Project member was not found.");
        if (member.Role.Equals("ProjectOwner", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("PROJECT_OWNER_ROLE_LOCKED", "Project owner role cannot be changed through member role update.");
        }

        var oldRole = member.Role;
        member.Role = NormalizeAssignableRole(request.Role);
        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        await audit.WriteAsync("ProjectMemberRoleChanged", project.Id, $"{member.UserId}:{oldRole}", $"{member.UserId}:{member.Role}", correlationId, ct);
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
        var actor = project.Members.SingleOrDefault(x => x.UserId == CurrentUserId());
        var normalizedMemberUserId = memberUserId.Trim();
        var member = project.Members.SingleOrDefault(x => x.UserId == normalizedMemberUserId)
            ?? throw new NotFoundException("PROJECT_MEMBER_NOT_FOUND", "Project member was not found.");

        if (member.Role.Equals("ProjectOwner", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("PROJECT_OWNER_CANNOT_BE_REMOVED", "Project owner cannot be removed.");
        }

        if (actor?.Role.Equals("ProjectAdmin", StringComparison.OrdinalIgnoreCase) == true
            && member.Role.Equals("ProjectAdmin", StringComparison.OrdinalIgnoreCase)
            && member.UserId != actor.UserId)
        {
            throw new ForbiddenException("Project admins cannot remove another project admin.");
        }

        project.Members.Remove(member);
        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        await audit.WriteAsync(
            "ProjectMemberRemoved",
            project.Id,
            $"{member.UserId}:{member.Role}",
            null,
            correlationId,
            ct);
        return ToResponse(project);
    }

    public Task ArchiveAsync(string projectId, CancellationToken ct) => ArchiveAsync(projectId, "none", ct);

    public async Task ArchiveAsync(string projectId, string correlationId, CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwner(project);
        project.Archived = true;
        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        await audit.WriteAsync("ProjectArchived", project.Id, "active", "archived", correlationId, ct);
    }

    public async Task<ProjectResponse> RestoreAsync(string projectId, string correlationId, CancellationToken ct)
    {
        var project = await GetArchivedProject(projectId, ct);
        EnsureOwner(project);
        project.Archived = false;
        project.UpdatedAt = clock.UtcNow;
        await projects.ReplaceByFilterAsync(x => x.Id == project.Id && x.Archived, project, ct);
        await audit.WriteAsync("ProjectRestored", project.Id, "archived", "active", correlationId, ct);
        return ToResponse(project);
    }

    private async Task<ProjectDocument> GetProject(string projectId, CancellationToken ct) =>
        await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
        ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");

    private async Task<ProjectDocument> GetArchivedProject(string projectId, CancellationToken ct) =>
        await projects.SelectAsync(x => x.Id == projectId && x.Archived, ct)
        ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Archived project was not found.");

    private void EnsureOrganizationScope(string organizationId)
    {
        if (!IsSystemAdmin()
            && !string.Equals(currentUser.OrganizationId, organizationId.Trim(), StringComparison.Ordinal))
        {
            throw new ForbiddenException("User cannot access projects outside the current organization.");
        }
    }

    private void EnsureOwnerOrAdmin(ProjectDocument project)
    {
        if (IsSystemAdmin())
        {
            return;
        }

        var membership = project.Members.SingleOrDefault(x => x.UserId == CurrentUserId())
            ?? throw new ForbiddenException("User is not a member of this project.");
        if (!membership.Role.Equals("ProjectOwner", StringComparison.OrdinalIgnoreCase)
            && !membership.Role.Equals("ProjectAdmin", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Project owner or admin role is required.");
        }
    }

    private void EnsureOwner(ProjectDocument project)
    {
        if (!IsOwner(project) && !IsSystemAdmin())
        {
            throw new ForbiddenException("Project owner role is required.");
        }
    }

    private bool IsOwner(ProjectDocument project) =>
        project.Members.Any(x =>
            x.UserId == CurrentUserId() && x.Role.Equals("ProjectOwner", StringComparison.OrdinalIgnoreCase));

    private string CurrentUserId() =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");

    private bool IsSystemAdmin() =>
        currentUser.Roles.Any(x => x.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 120)
        {
            throw new ValidationException("Project name must contain 2-120 characters.");
        }

        return normalized;
    }

    private static string NormalizeVisibility(string visibility)
    {
        if (string.Equals(visibility, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return "Internal";
        }

        if (string.Equals(visibility, "Private", StringComparison.OrdinalIgnoreCase))
        {
            return "Private";
        }

        throw new ValidationException("Project visibility must be Internal or Private.");
    }

    private static string NormalizeAssignableRole(string role)
    {
        if (string.Equals(role, "ProjectAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "ProjectAdmin";
        }

        if (string.IsNullOrWhiteSpace(role) || string.Equals(role, "Developer", StringComparison.OrdinalIgnoreCase))
        {
            return "Developer";
        }

        if (string.Equals(role, "Viewer", StringComparison.OrdinalIgnoreCase))
        {
            return "Viewer";
        }

        throw new ValidationException("Project member role must be ProjectAdmin, Developer or Viewer.");
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,8}[A-Z0-9]$")]
    private static partial Regex ProjectKeyPattern();

    private static ProjectResponse ToResponse(ProjectDocument project) =>
        new(
            project.Id,
            project.OrganizationId,
            project.Key,
            project.Name,
            project.Visibility,
            project.Members.Select(x => new ProjectMemberResponse(x.UserId, x.Role)).ToList(),
            project.TeamIds,
            project.Archived);
}
