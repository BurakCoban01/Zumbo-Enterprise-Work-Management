using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationTriggerDefinition(
    string Type,
    string? EventType,
    int? IntervalMinutes,
    DateTimeOffset? StartAtUtc);
