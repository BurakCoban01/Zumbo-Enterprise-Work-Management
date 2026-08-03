namespace Zumbo.Modules.Projects;

internal static class ProjectResponseMapper
{
    internal static ProjectResponse ToResponse(ProjectDocument project) =>
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
