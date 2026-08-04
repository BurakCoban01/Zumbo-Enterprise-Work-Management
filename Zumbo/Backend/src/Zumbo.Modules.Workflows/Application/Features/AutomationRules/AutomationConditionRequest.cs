using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationConditionRequest(
    string Kind,
    string? Field = null,
    string? Operator = null,
    string? Value = null,
    IReadOnlyCollection<AutomationConditionRequest>? Children = null);
