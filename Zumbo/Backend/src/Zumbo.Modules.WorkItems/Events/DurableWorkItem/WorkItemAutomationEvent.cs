using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemAutomationEvent(
    string OrganizationId,
    string ProjectId,
    string EventType,
    string TriggerId,
    string WorkItemId,
    string ActorUserId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string?> Fields,
    string? RootRunId,
    int ChainDepth,
    IReadOnlyCollection<string> VisitedRuleIds);
