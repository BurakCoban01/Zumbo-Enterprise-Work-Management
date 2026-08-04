using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationRunStepResponse(
    int Index,
    string ActionType,
    string Status,
    int Attempt,
    string? FailureCategory,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);
