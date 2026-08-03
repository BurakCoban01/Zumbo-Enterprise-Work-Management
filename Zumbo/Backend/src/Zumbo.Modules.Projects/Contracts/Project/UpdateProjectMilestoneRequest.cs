namespace Zumbo.Modules.Projects;
public sealed record UpdateProjectMilestoneRequest(string Name, DateTimeOffset DueAt);
