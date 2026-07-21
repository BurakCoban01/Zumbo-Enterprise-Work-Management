public sealed class RateLimitingOptions
{
    public string Provider { get; init; } = "InMemory";
    public int LoginPermitLimit { get; init; } = 10;
    public int PasswordResetPermitLimit { get; init; } = 5;
    public int ApiPermitLimit { get; init; } = 300;
    public int SearchPermitLimit { get; init; } = 60;
    public int UploadPermitLimit { get; init; } = 10;
    public int ReportPermitLimit { get; init; } = 30;
    public int BulkPermitLimit { get; init; } = 10;
    public int RealtimeConnectPermitLimit { get; init; } = 500;
    public int StandardWindowSeconds { get; init; } = 60;
    public int PasswordResetWindowSeconds { get; init; } = 900;
    public RedisRateLimitingOptions Redis { get; init; } = new();

    public DistributedRateLimitPolicy ResolvePolicy(string name) => name switch
    {
        "login" => new(name, LoginPermitLimit, StandardWindowSeconds, true),
        "password-reset" => new(name, PasswordResetPermitLimit, PasswordResetWindowSeconds, true),
        "api" => new(name, ApiPermitLimit, StandardWindowSeconds, false),
        "search" => new(name, SearchPermitLimit, StandardWindowSeconds, false),
        "upload" => new(name, UploadPermitLimit, StandardWindowSeconds, true),
        "report" => new(name, ReportPermitLimit, StandardWindowSeconds, false),
        "bulk" => new(name, BulkPermitLimit, StandardWindowSeconds, true),
        "realtime-connect" => new(name, RealtimeConnectPermitLimit, StandardWindowSeconds, true),
        _ => throw new InvalidOperationException($"Unknown rate-limit policy '{name}'.")
    };
}

public sealed class RedisRateLimitingOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = "zumbo:rate:";
    public int OperationTimeoutMilliseconds { get; init; } = 750;
}

public sealed record DistributedRateLimitPolicy(
    string Name,
    int PermitLimit,
    int WindowSeconds,
    bool FailClosed);
