using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationTriggerRequest(
    string Type,
    string? EventType = null,
    int? IntervalMinutes = null,
    DateTimeOffset? StartAtUtc = null);
