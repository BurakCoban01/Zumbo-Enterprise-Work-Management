using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationExecutionContext(
    string OrganizationId,
    string ProjectId,
    string TriggerType,
    string? EventType,
    string TriggerId,
    string SourceId,
    string ActorUserId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string?> Fields,
    bool ActorAvailable = true,
    string? RootRunId = null,
    int ChainDepth = 0,
    IReadOnlyCollection<string>? VisitedRuleIds = null,
    string? RuleId = null);
