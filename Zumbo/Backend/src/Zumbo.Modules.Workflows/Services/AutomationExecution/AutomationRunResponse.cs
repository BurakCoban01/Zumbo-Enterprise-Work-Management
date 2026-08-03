using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationRunResponse(
    string Id,
    string ProjectId,
    string RuleId,
    int RuleVersion,
    string RuleName,
    string TriggerType,
    string? EventType,
    string SourceId,
    string ActorUserId,
    string RootRunId,
    int ChainDepth,
    string Status,
    string Outcome,
    int Attempt,
    int MaximumAttempts,
    string? FailureCategory,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    IReadOnlyCollection<AutomationRunStepResponse> Steps,
    long Version) : IVersionedResource;
