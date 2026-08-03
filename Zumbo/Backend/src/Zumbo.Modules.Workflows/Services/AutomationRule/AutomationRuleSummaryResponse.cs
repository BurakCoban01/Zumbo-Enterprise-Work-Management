using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationRuleSummaryResponse(
    string Id,
    string ProjectId,
    string Name,
    string TriggerType,
    string? EventType,
    bool Active,
    bool Archived,
    DateTimeOffset? NextRunAtUtc,
    int PublishedVersion,
    bool HasDraft,
    long Version);
