using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class ProjectMemberDocument
{
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = ProjectRoles.Developer;
}
