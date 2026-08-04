using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationDryRunResponse(
    string RuleId,
    int RuleVersion,
    bool TriggerMatched,
    bool ConditionMatched,
    IReadOnlyCollection<AutomationActionDefinition> PlannedActions,
    string Outcome);
