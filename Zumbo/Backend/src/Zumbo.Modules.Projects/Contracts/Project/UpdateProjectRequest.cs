namespace Zumbo.Modules.Projects;
public sealed record UpdateProjectRequest(string Name, string Visibility, string? Key = null);
