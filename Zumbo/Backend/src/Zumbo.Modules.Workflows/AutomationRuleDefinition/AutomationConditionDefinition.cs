using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationConditionDefinition(
    string Kind,
    string? Field,
    string? Operator,
    string? Value,
    IReadOnlyCollection<AutomationConditionDefinition> Children);
