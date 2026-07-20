using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class ProjectService
{
    public async Task<ProjectResponse> UpsertTemplateAsync(
        string projectId,
        string? templateId,
        UpsertProjectTemplateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var name = NormalizeLabel(request.Name, "Template name");
        var duplicate = project.Templates.Any(template =>
            template.Id != templateId
            && !template.Archived
            && template.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            throw new ConflictException("PROJECT_TEMPLATE_EXISTS", "An active project template with this name already exists.");
        }

        var defaults = (request.DefaultComponentNames ?? [])
            .Select(component => NormalizeLabel(component, "Default component name", 80))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        ProjectTemplateDocument template;
        string action;
        string? oldValue;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            template = new ProjectTemplateDocument { Name = name };
            project.Templates.Add(template);
            action = "ProjectTemplateCreated";
            oldValue = null;
        }
        else
        {
            template = project.Templates.SingleOrDefault(candidate => candidate.Id == templateId)
                ?? throw new NotFoundException("PROJECT_TEMPLATE_NOT_FOUND", "Project template was not found.");
            if (template.Archived)
            {
                throw new ConflictException("PROJECT_TEMPLATE_ARCHIVED", "Archived project templates cannot be updated.");
            }

            action = "ProjectTemplateUpdated";
            oldValue = template.Name;
        }

        if (request.IsDefault)
        {
            foreach (var candidate in project.Templates)
            {
                candidate.IsDefault = candidate.Id == template.Id;
            }
        }
        else if (template.IsDefault)
        {
            throw new ConflictException(
                "PROJECT_DEFAULT_TEMPLATE_REQUIRED",
                "Select another default template before clearing the current default.");
        }

        template.Name = name;
        template.DefaultComponentNames = defaults;
        template.IsDefault = request.IsDefault;
        await SaveAsync(project, ct);
        await audit.WriteAsync(action, project.Id, oldValue, $"{template.Id}:{template.Name}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> ArchiveTemplateAsync(
        string projectId,
        string templateId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var template = project.Templates.SingleOrDefault(candidate => candidate.Id == templateId && !candidate.Archived)
            ?? throw new NotFoundException("PROJECT_TEMPLATE_NOT_FOUND", "Active project template was not found.");
        if (template.IsDefault)
        {
            throw new ConflictException(
                "PROJECT_DEFAULT_TEMPLATE_REQUIRED",
                "Select another default template before archiving the current default.");
        }

        template.Archived = true;
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectTemplateArchived", project.Id, template.Name, null, correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> CreateComponentAsync(
        string projectId,
        CreateProjectComponentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var name = NormalizeLabel(request.Name, "Component name", 80);
        EnsureUniqueComponent(project, name);
        var component = new ProjectComponentDocument
        {
            Name = name,
            Description = NormalizeOptional(request.Description, "Component description", 500)
        };
        project.Components.Add(component);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectComponentCreated", project.Id, null, $"{component.Id}:{name}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> UpdateComponentAsync(
        string projectId,
        string componentId,
        UpdateProjectComponentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var component = project.Components.SingleOrDefault(candidate => candidate.Id == componentId && !candidate.Archived)
            ?? throw new NotFoundException("PROJECT_COMPONENT_NOT_FOUND", "Active project component was not found.");
        var name = NormalizeLabel(request.Name, "Component name", 80);
        EnsureUniqueComponent(project, name, component.Id);
        var oldValue = component.Name;
        component.Name = name;
        component.Description = NormalizeOptional(request.Description, "Component description", 500);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectComponentUpdated", project.Id, oldValue, name, correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> ArchiveComponentAsync(
        string projectId,
        string componentId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var component = project.Components.SingleOrDefault(candidate => candidate.Id == componentId && !candidate.Archived)
            ?? throw new NotFoundException("PROJECT_COMPONENT_NOT_FOUND", "Active project component was not found.");
        component.Archived = true;
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectComponentArchived", project.Id, component.Name, null, correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> CreateVersionAsync(
        string projectId,
        CreateProjectVersionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var name = NormalizeLabel(request.Name, "Version name", 80);
        if (project.Versions.Any(version =>
            version.Status != ProjectVersionStatuses.Archived
            && version.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("PROJECT_VERSION_EXISTS", "An active project version with this name already exists.");
        }

        var version = new ProjectVersionDocument { Name = name };
        project.Versions.Add(version);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectVersionCreated", project.Id, null, $"{version.Id}:{name}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> ArchiveVersionAsync(
        string projectId,
        string versionId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var version = project.Versions.SingleOrDefault(candidate => candidate.Id == versionId)
            ?? throw new NotFoundException("PROJECT_VERSION_NOT_FOUND", "Project version was not found.");
        if (version.Status == ProjectVersionStatuses.Released)
        {
            throw new ConflictException("PROJECT_VERSION_RELEASED", "Released project versions cannot be archived.");
        }

        if (project.Releases.Any(release =>
            release.VersionId == version.Id && release.Status != ProjectReleaseStatuses.Published))
        {
            throw new ConflictException("PROJECT_VERSION_HAS_RELEASE", "A version with an active release cannot be archived.");
        }

        version.Status = ProjectVersionStatuses.Archived;
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectVersionArchived", project.Id, version.Name, null, correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> CreateReleaseAsync(
        string projectId,
        CreateProjectReleaseRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var version = project.Versions.SingleOrDefault(candidate => candidate.Id == request.VersionId)
            ?? throw new NotFoundException("PROJECT_VERSION_NOT_FOUND", "Project version was not found.");
        if (version.Status != ProjectVersionStatuses.Planned)
        {
            throw new ConflictException("PROJECT_VERSION_NOT_PLANNED", "Only planned versions can receive a release.");
        }

        if (project.Releases.Any(release => release.VersionId == version.Id))
        {
            throw new ConflictException("PROJECT_RELEASE_EXISTS", "The project version already has a release.");
        }

        var release = new ProjectReleaseDocument
        {
            VersionId = version.Id,
            Name = NormalizeLabel(request.Name, "Release name", 100),
            ScheduledAt = request.ScheduledAt
        };
        project.Releases.Add(release);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectReleaseCreated", project.Id, null, $"{release.Id}:{release.Name}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> ApproveReleaseAsync(
        string projectId,
        string releaseId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwner(project);
        var release = FindRelease(project, releaseId);
        if (release.Status != ProjectReleaseStatuses.Draft)
        {
            throw new ConflictException("PROJECT_RELEASE_NOT_DRAFT", "Only a draft release can be approved.");
        }

        release.Status = ProjectReleaseStatuses.Approved;
        release.ApprovedAt = clock.UtcNow;
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectReleaseApproved", project.Id, ProjectReleaseStatuses.Draft, release.Id, correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> PublishReleaseAsync(
        string projectId,
        string releaseId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwner(project);
        var release = FindRelease(project, releaseId);
        if (release.Status != ProjectReleaseStatuses.Approved)
        {
            throw new ConflictException("PROJECT_RELEASE_NOT_APPROVED", "Only an approved release can be published.");
        }

        var version = project.Versions.Single(candidate => candidate.Id == release.VersionId);
        release.Status = ProjectReleaseStatuses.Published;
        release.PublishedAt = clock.UtcNow;
        version.Status = ProjectVersionStatuses.Released;
        version.ReleasedAt = clock.UtcNow;
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectReleasePublished", project.Id, ProjectReleaseStatuses.Approved, release.Id, correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> CreateMilestoneAsync(
        string projectId,
        CreateProjectMilestoneRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var name = NormalizeLabel(request.Name, "Milestone name", 100);
        EnsureMilestoneDueAt(request.DueAt);
        if (project.Milestones.Any(milestone =>
            milestone.Status == ProjectMilestoneStatuses.Open
            && milestone.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("PROJECT_MILESTONE_EXISTS", "An open milestone with this name already exists.");
        }

        var milestone = new ProjectMilestoneDocument { Name = name, DueAt = request.DueAt };
        project.Milestones.Add(milestone);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectMilestoneCreated", project.Id, null, $"{milestone.Id}:{name}", correlationId, ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> UpdateMilestoneAsync(
        string projectId,
        string milestoneId,
        UpdateProjectMilestoneRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var milestone = FindOpenMilestone(project, milestoneId);
        EnsureMilestoneDueAt(request.DueAt);
        var oldValue = $"{milestone.Name}:{milestone.DueAt:O}";
        milestone.Name = NormalizeLabel(request.Name, "Milestone name", 100);
        milestone.DueAt = request.DueAt;
        await SaveAsync(project, ct);
        await audit.WriteAsync(
            "ProjectMilestoneUpdated",
            project.Id,
            oldValue,
            $"{milestone.Name}:{milestone.DueAt:O}",
            correlationId,
            ct);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> CompleteMilestoneAsync(
        string projectId,
        string milestoneId,
        string correlationId,
        CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwnerOrAdmin(project);
        var milestone = FindOpenMilestone(project, milestoneId);
        milestone.Status = ProjectMilestoneStatuses.Completed;
        milestone.CompletedAt = clock.UtcNow;
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectMilestoneCompleted", project.Id, milestone.Name, milestone.Id, correlationId, ct);
        return ToResponse(project);
    }

    private static ProjectReleaseDocument FindRelease(ProjectDocument project, string releaseId) =>
        project.Releases.SingleOrDefault(release => release.Id == releaseId)
        ?? throw new NotFoundException("PROJECT_RELEASE_NOT_FOUND", "Project release was not found.");

    private static ProjectMilestoneDocument FindOpenMilestone(ProjectDocument project, string milestoneId)
    {
        var milestone = project.Milestones.SingleOrDefault(candidate => candidate.Id == milestoneId)
            ?? throw new NotFoundException("PROJECT_MILESTONE_NOT_FOUND", "Project milestone was not found.");
        if (milestone.Status != ProjectMilestoneStatuses.Open)
        {
            throw new ConflictException("PROJECT_MILESTONE_COMPLETED", "Completed milestones cannot be changed.");
        }

        return milestone;
    }

    private static void EnsureUniqueComponent(ProjectDocument project, string name, string? excludedId = null)
    {
        if (project.Components.Any(component =>
            component.Id != excludedId
            && !component.Archived
            && component.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("PROJECT_COMPONENT_EXISTS", "An active project component with this name already exists.");
        }
    }

    private void EnsureMilestoneDueAt(DateTimeOffset dueAt)
    {
        if (dueAt <= clock.UtcNow)
        {
            throw new ValidationException("Milestone due date must be in the future.");
        }
    }

    private static string? NormalizeOptional(string? value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ValidationException($"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
