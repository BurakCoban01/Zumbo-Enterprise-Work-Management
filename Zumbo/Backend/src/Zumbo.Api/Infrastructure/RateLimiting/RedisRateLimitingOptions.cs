
public sealed class RedisRateLimitingOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = "zumbo:rate:";
    public int OperationTimeoutMilliseconds { get; init; } = 750;
}
