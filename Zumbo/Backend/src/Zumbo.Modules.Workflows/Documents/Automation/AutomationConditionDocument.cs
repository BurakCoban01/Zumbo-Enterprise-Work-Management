using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class AutomationConditionDocument
{
    public string Kind { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public List<AutomationConditionDocument> Children { get; set; } = [];
}
