using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    public async Task<IReadOnlyCollection<AutomationRunResponse>> ExecuteAsync(
        AutomationExecutionContext context,
        CancellationToken ct)
    {
        ValidateContext(context);
        var matchingRules = await ListMatchingRulesAsync(context, ct);
        var responses = new List<AutomationRunResponse>(matchingRules.Count);
        foreach (var rule in matchingRules)
        {
            responses.Add(await ExecuteRuleAsync(rule, context, ct));
        }

        return responses;
    }
}
