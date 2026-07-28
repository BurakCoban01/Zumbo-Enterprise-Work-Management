using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public static class AutomationRunStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Skipped = "Skipped";
    public const string RetryScheduled = "RetryScheduled";
    public const string DeadLetter = "DeadLetter";
}

public static class AutomationStepStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public sealed class AutomationRunDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public int RuleVersion { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string? EventType { get; set; }
    public string TriggerId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public string RootRunId { get; set; } = string.Empty;
    public int ChainDepth { get; set; }
    public List<string> VisitedRuleIds { get; set; } = [];
    public Dictionary<string, string?> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Status { get; set; } = AutomationRunStates.Pending;
    public string Outcome { get; set; } = AutomationRunStates.Pending;
    public int Attempt { get; set; }
    public int MaximumAttempts { get; set; } = 3;
    public List<AutomationRunStepDocument> Steps { get; set; } = [];
    public string? FailureCategory { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class AutomationRunStepDocument
{
    public int Index { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Status { get; set; } = AutomationStepStates.Pending;
    public int Attempt { get; set; }
    public string? FailureCategory { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
