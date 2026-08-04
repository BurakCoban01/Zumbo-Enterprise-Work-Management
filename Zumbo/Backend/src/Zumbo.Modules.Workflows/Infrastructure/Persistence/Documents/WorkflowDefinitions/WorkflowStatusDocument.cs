using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class WorkflowStatusDocument
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
