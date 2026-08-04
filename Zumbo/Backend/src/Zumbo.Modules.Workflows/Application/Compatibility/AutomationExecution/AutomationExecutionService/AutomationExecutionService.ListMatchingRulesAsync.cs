using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private async Task<List<AutomationRuleDocument>> ListMatchingRulesAsync(
        AutomationExecutionContext context,
        CancellationToken ct)
    {
        var result = new List<AutomationRuleDocument>();
        string? cursor = null;
        do
        {
            var page = await rules.ListByCursorAsync(
                rule => rule.OrganizationId == context.OrganizationId
                    && rule.ProjectId == context.ProjectId
                    && rule.Active
                    && !rule.Archived
                    && rule.PublishedVersion > 0
                    && (context.RuleId == null || rule.Id == context.RuleId),
                cursor,
                200,
                ct);
            result.AddRange(page.Items.Where(rule =>
                TriggerMatches(CurrentPublished(rule).Trigger, context)));
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }
}
