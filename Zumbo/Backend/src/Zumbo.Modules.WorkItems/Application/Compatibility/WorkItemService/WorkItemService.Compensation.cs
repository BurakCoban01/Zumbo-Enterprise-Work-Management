using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    private void ObserveCompensation(CompensationResult result)
    {
        if (!result.Succeeded)
        {
            compensationLogger?.LogWarning(
                "Compensation operation {Operation} ended with {Outcome}; failure type {FailureType}.",
                result.Operation,
                result.Outcome,
                result.Exception?.GetType().Name ?? "none");
        }
    }
}
