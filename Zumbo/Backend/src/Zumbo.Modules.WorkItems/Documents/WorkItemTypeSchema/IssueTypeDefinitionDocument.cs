using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class IssueTypeDefinitionDocument
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string HierarchyLevel { get; set; } = IssueTypeHierarchyLevels.Standard;
    public bool Active { get; set; } = true;
    public int Position { get; set; }
}
