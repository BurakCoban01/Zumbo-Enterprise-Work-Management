using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationDryRunContext(
    string TriggerType,
    string? EventType,
    string? SourceId,
    IReadOnlyDictionary<string, string?> Fields);
