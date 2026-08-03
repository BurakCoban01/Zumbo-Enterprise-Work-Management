
public sealed record DistributedRateLimitPolicy(
    string Name,
    int PermitLimit,
    int WindowSeconds,
    bool FailClosed);
