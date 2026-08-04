using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationActionExecution(
    string RunId,
    string RuleId,
    int RuleVersion,
    string ProjectId,
    string SourceId,
    string ActorUserId,
    string RootRunId,
    int ChainDepth,
    IReadOnlyCollection<string> VisitedRuleIds,
    int ActionIndex,
    AutomationActionDefinition Action,
    string CorrelationId);
