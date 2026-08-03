namespace Zumbo.BuildingBlocks.Application.Messaging;

public sealed class DurableMessageRetryPolicy(
    TimeSpan baseDelay,
    TimeSpan maximumDelay,
    double jitterRatio,
    IDurableMessageJitter jitter)
{
    public TimeSpan DelayForAttempt(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        if (baseDelay <= TimeSpan.Zero || maximumDelay < baseDelay || jitterRatio is < 0 or > 1)
        {
            throw new InvalidOperationException("Durable message retry settings are invalid.");
        }

        var exponent = Math.Min(attempt - 1, 30);
        var exponentialMilliseconds = Math.Min(
            baseDelay.TotalMilliseconds * Math.Pow(2, exponent),
            maximumDelay.TotalMilliseconds);
        var centeredJitter = ((jitter.NextUnit() * 2) - 1) * jitterRatio;
        var milliseconds = Math.Clamp(
            exponentialMilliseconds * (1 + centeredJitter),
            baseDelay.TotalMilliseconds * (1 - jitterRatio),
            maximumDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
