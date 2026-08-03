using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationRuleVersionResponse(
    int Number,
    string State,
    string Name,
    string? Description,
    AutomationTriggerDefinition Trigger,
    AutomationConditionDefinition? Condition,
    IReadOnlyCollection<AutomationActionDefinition> Actions,
    int MaximumExecutionsPerHour,
    int MaximumChainDepth,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt);
