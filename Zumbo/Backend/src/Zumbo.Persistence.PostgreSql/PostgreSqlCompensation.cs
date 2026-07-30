using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.Persistence.PostgreSql;

internal static class PostgreSqlCompensation
{
    internal static async Task<CompensationResult> RunAsync(
        string operation,
        Func<CancellationToken, Task> action,
        ILogger? logger = null)
    {
        var result = await CompensationExecution.RunAsync(operation, action);
        if (!result.Succeeded)
        {
            logger?.LogWarning(
                "Compensation operation {Operation} ended with {Outcome}; failure type {FailureType}.",
                result.Operation,
                result.Outcome,
                result.Exception?.GetType().Name ?? "none");
        }

        return result;
    }
}
