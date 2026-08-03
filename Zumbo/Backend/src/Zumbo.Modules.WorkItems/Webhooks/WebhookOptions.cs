using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WebhookOptions
{
    public bool Enabled { get; init; } = true;
    public bool AllowHttpLoopback { get; init; }
    public int MaximumAttempts { get; init; } = 5;
    public int BaseRetrySeconds { get; init; } = 1;
    public int MaximumRetrySeconds { get; init; } = 60;
    public double RetryJitterRatio { get; init; } = 0.2;
    public int LeaseSeconds { get; init; } = 60;
    public int RequestTimeoutSeconds { get; init; } = 5;
    public int DispatchBatchSize { get; init; } = 50;
    public int DispatcherIntervalSeconds { get; init; } = 1;
    public int RotationOverlapMinutes { get; init; } = 15;
}
