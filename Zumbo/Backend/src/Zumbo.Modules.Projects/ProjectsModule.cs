using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class ProjectService
{
    private readonly IDocumentRepository<ProjectDocument> projects;
    private readonly IProjectMemberDirectory memberDirectory;
    private readonly IProjectTeamDirectory teamDirectory;
    private readonly IProjectTeamUsageChecker teamUsageChecker;
    private readonly IProjectOrganizationDirectory organizationDirectory;
    private readonly IProjectAuditWriter audit;
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ExpectedVersionState expectedVersion;
    private readonly ProjectLifecycleOptions lifecycle;

    public ProjectService(
        IDocumentRepository<ProjectDocument> projects,
        IProjectMemberDirectory memberDirectory,
        IProjectTeamDirectory teamDirectory,
        IProjectTeamUsageChecker teamUsageChecker,
        IProjectAuditWriter audit,
        IClock clock,
        ICurrentUser currentUser,
        IExpectedVersionAccessor? expectedVersions = null,
        IProjectOrganizationDirectory? organizationDirectory = null,
        IOptions<ProjectLifecycleOptions>? lifecycleOptions = null)
    {
        this.projects = projects;
        this.memberDirectory = memberDirectory;
        this.teamDirectory = teamDirectory;
        this.teamUsageChecker = teamUsageChecker;
        this.audit = audit;
        this.clock = clock;
        this.currentUser = currentUser;
        expectedVersion = new ExpectedVersionState(expectedVersions);
        this.organizationDirectory = organizationDirectory ?? AllowActiveProjectOrganizationDirectory.Instance;
        lifecycle = lifecycleOptions?.Value ?? new ProjectLifecycleOptions();
    }

    public Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<ProjectResponse> CreateAsync(
        CreateProjectRequest request,
        string correlationId,
        CancellationToken ct)
    {
        CreateProjectValidator.Validate(request);
        var organizationId = request.OrganizationId.Trim();
        EnsureOrganizationScope(organizationId);
        await organizationDirectory.EnsureActiveAsync(organizationId, ct);
        var userId = CurrentUserId();
        if (!IsSystemAdmin() && !string.Equals(request.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("A project can only be created for the authenticated owner.");
        }

        var ownerUserId = request.OwnerUserId.Trim();
        await memberDirectory.EnsureEligibleAsync(ownerUserId, organizationId, ct);
        var key = NormalizeKey(request.Key);
        if (await projects.ExistsByFilterAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Key == key,
            ct))
        {
            throw new ConflictException("PROJECT_KEY_EXISTS", "Project key must be unique inside the organization.");
        }

        var now = clock.UtcNow;
        var project = new ProjectDocument
        {
            OrganizationId = organizationId,
            Key = key,
            Name = NormalizeName(request.Name),
            Visibility = ProjectVisibilities.Normalize(request.Visibility),
            CreatedAt = now,
            UpdatedAt = now,
            Members =
            [
                new ProjectMemberDocument { UserId = ownerUserId, Role = ProjectRoles.Owner }
            ]
        };
        try
        {
            await projects.CreateAsync(project, ct);
        }
        catch (DocumentConflictException)
        {
            throw new ConflictException(
                "PROJECT_KEY_EXISTS",
                "Project key must be unique inside the organization.");
        }
        await audit.WriteAsync("ProjectCreated", project.Id, null, $"{project.Key}:{project.Name}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<IReadOnlyList<ProjectResponse>> ListAsync(
        string organizationId,
        CancellationToken ct,
        bool archived = false)
    {
        var normalizedOrganizationId = organizationId.Trim();
        EnsureOrganizationScope(normalizedOrganizationId);
        await organizationDirectory.EnsureActiveAsync(normalizedOrganizationId, ct);
        var userId = CurrentUserId();
        var result = await projects.ListByFilterAsync(
            project => project.OrganizationId == normalizedOrganizationId
                && project.Archived == archived
                && (project.Visibility == ProjectVisibilities.Internal
                    || project.Members.Any(member => member.UserId == userId)),
            project => project.Key,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public async Task<ProjectResponse> GetAsync(string projectId, CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        if (!ProjectVisibilityAccess.CanView(
            project.Visibility,
            project.Members.Select(member => member.UserId),
            CurrentUserId())
            && !IsSystemAdmin())
        {
            throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        }

        return ToResponse(project);
    }

    public Task<ProjectResponse> UpdateAsync(string projectId, UpdateProjectRequest request, CancellationToken ct) =>
        UpdateAsync(projectId, request, "none", ct);

    public async Task<ProjectResponse> UpdateAsync(
        string projectId,
        UpdateProjectRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        if (!string.IsNullOrWhiteSpace(request.Key)
            && !string.Equals(NormalizeKey(request.Key), project.Key, StringComparison.Ordinal))
        {
            throw new ConflictException("PROJECT_KEY_IMMUTABLE", "Project key cannot be changed after creation.");
        }

        var oldValue = $"{project.Name}:{project.Visibility}";
        project.Name = NormalizeName(request.Name);
        project.Visibility = ProjectVisibilities.Normalize(request.Visibility);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectUpdated", project.Id, oldValue, $"{project.Name}:{project.Visibility}", correlationId, ct);
        return ToResponse(project);
    }

    private async Task<ProjectDocument> GetProject(string projectId, CancellationToken ct)
    {
        var project = await projects.SelectAsync(candidate => candidate.Id == projectId && !candidate.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        EnsureOrganizationScope(project.OrganizationId);
        await organizationDirectory.EnsureActiveAsync(project.OrganizationId, ct);
        return project;
    }

    private async Task<ProjectDocument> GetArchivedProject(string projectId, CancellationToken ct)
    {
        var project = await projects.SelectAsync(candidate => candidate.Id == projectId && candidate.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Archived project was not found.");
        EnsureOrganizationScope(project.OrganizationId);
        await organizationDirectory.EnsureActiveAsync(project.OrganizationId, ct);
        return project;
    }

    private async Task SaveAsync(ProjectDocument project, CancellationToken ct)
    {
        EnsureExactlyOneOwner(project);
        project.UpdatedAt = clock.UtcNow;
        var result = await projects.ReplaceByVersionAsync(
            candidate => candidate.Id == project.Id,
            project,
            expectedVersion.Consume(project.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        }

        project.Version = result.Version!.Value;
    }

    private void EnsureOrganizationScope(string organizationId)
    {
        if (!IsSystemAdmin()
            && !string.Equals(currentUser.OrganizationId, organizationId.Trim(), StringComparison.Ordinal))
        {
            throw new ForbiddenException("User cannot access projects outside the current organization.");
        }
    }

    private ProjectMemberDocument EnsureOwnerOrAdmin(ProjectDocument project)
    {
        if (IsSystemAdmin())
        {
            return project.Members.Single(member => member.Role == ProjectRoles.Owner);
        }

        var membership = project.Members.SingleOrDefault(member => member.UserId == CurrentUserId())
            ?? throw new ForbiddenException("User is not a member of this project.");
        if (membership.Role is not (ProjectRoles.Owner or ProjectRoles.Admin))
        {
            throw new ForbiddenException("Project owner or admin role is required.");
        }

        return membership;
    }

    private ProjectMemberDocument EnsureOwner(ProjectDocument project)
    {
        EnsureExactlyOneOwner(project);
        var owner = project.Members.Single(member => member.Role == ProjectRoles.Owner);
        if (!IsSystemAdmin() && owner.UserId != CurrentUserId())
        {
            throw new ForbiddenException("Project owner role is required.");
        }

        return owner;
    }

    private static void EnsureExactlyOneOwner(ProjectDocument project)
    {
        if (project.Members.Count(member => member.Role == ProjectRoles.Owner) != 1)
        {
            throw new ConflictException("PROJECT_OWNER_INVARIANT", "A project must have exactly one owner.");
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
        if (normalized.Length is < 2 or > 120)
        {
            throw new ValidationException("Project name must contain 2-120 characters.");
        }

        return normalized;
    }

    private static string NormalizeKey(string? key)
    {
        var normalized = key?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!ProjectKeyPattern().IsMatch(normalized))
        {
            throw new ValidationException("Project key must contain 2-10 upper-case letters, numbers or hyphens.");
        }

        return normalized;
    }

    private static string NormalizeLabel(string? value, string fieldName, int maximumLength = 120)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 || normalized.Length > maximumLength)
        {
            throw new ValidationException($"{fieldName} must contain 2-{maximumLength} characters.");
        }

        return normalized;
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
            project.Members.Select(member => new ProjectMemberResponse(member.UserId, member.Role)).ToList(),
            project.TeamIds,
            project.Archived,
            project.Version,
            project.Templates.Select(template => new ProjectTemplateResponse(
                template.Id, template.Name, template.IsDefault, template.Archived, template.DefaultComponentNames)).ToList(),
            project.Components.Select(component => new ProjectComponentResponse(
                component.Id, component.Name, component.Description, component.Archived)).ToList(),
            project.Versions.Select(version => new ProjectVersionResponse(
                version.Id, version.Name, version.Status, version.ReleasedAt)).ToList(),
            project.Releases.Select(release => new ProjectReleaseResponse(
                release.Id, release.VersionId, release.Name, release.Status, release.ScheduledAt,
                release.ApprovedAt, release.PublishedAt)).ToList(),
            project.Milestones.Select(milestone => new ProjectMilestoneResponse(
                milestone.Id, milestone.Name, milestone.DueAt, milestone.Status, milestone.CompletedAt)).ToList(),
            project.ArchivedAt,
            project.RetainUntil);
}
