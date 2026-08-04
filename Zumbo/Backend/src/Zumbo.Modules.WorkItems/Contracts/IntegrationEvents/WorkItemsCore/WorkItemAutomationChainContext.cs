using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemAutomationChainContext(
    string RootRunId,
    int ChainDepth,
    IReadOnlyCollection<string> VisitedRuleIds);
