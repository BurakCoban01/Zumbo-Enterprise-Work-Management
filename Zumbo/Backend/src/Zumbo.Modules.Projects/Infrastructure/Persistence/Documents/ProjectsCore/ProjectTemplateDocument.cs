using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class ProjectTemplateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool Archived { get; set; }
    public List<string> DefaultComponentNames { get; set; } = [];
}
