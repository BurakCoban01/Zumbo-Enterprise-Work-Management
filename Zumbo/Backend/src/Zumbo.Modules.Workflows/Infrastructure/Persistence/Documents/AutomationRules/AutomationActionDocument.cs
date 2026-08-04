using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class AutomationActionDocument
{
    public string Type { get; set; } = string.Empty;
    public string? Value { get; set; }
}
