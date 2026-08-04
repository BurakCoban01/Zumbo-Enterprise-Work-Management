using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed class EmailNotificationOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 25;
    public bool UseSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string FromAddress { get; init; } = "noreply@zumbo.local";
    public string FromName { get; init; } = "Zumbo";
    public int MaxAttempts { get; init; } = 5;
    public int BaseRetrySeconds { get; init; } = 60;
    public int MaximumRetrySeconds { get; init; } = 3600;
    public double RetryJitterRatio { get; init; } = 0.2;
    public int LeaseSeconds { get; init; } = 60;
    public int DispatchBatchSize { get; init; } = 50;
    public int DispatcherIntervalSeconds { get; init; } = 30;
}
