using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private TimeSpan RetryDelay(int attempt)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Clamp(options.Value.BaseRetrySeconds, 1, 3600));
        var maximumDelay = TimeSpan.FromSeconds(Math.Clamp(options.Value.MaximumRetrySeconds, 1, 86_400));
        if (retryJitter is not null)
        {
            return new DurableMessageRetryPolicy(
                baseDelay,
                maximumDelay,
                Math.Clamp(options.Value.RetryJitterRatio, 0, 1),
                retryJitter).DelayForAttempt(attempt);
        }

        var exponent = Math.Min(attempt - 1, 20);
        return TimeSpan.FromSeconds(Math.Min(
            baseDelay.TotalSeconds * Math.Pow(2, exponent),
            maximumDelay.TotalSeconds));
    }
}
