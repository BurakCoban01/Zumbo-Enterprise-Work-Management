namespace Zumbo.Modules.Projects;
public sealed record CreateProjectMilestoneRequest(string Name, DateTimeOffset DueAt);
