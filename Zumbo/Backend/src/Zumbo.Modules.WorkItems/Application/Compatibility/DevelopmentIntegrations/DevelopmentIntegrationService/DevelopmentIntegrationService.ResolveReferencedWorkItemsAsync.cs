using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private async Task<IReadOnlyCollection<WorkItemDocument>> ResolveReferencedWorkItemsAsync(
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent providerEvent,
        CancellationToken ct)
    {
        var prefixes = DevelopmentWebhookReferencePolicy
            .ExtractWithinLimit(providerEvent.ReferenceTexts)
            .Where(reference => string.Equals(
                reference.ProjectKey,
                mapping.ProjectKey,
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.WorkItemIdPrefix)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var result = new List<WorkItemDocument>();
        foreach (var prefix in prefixes)
        {
            var matches = await workItems.ListByFilterAsync(
                item => item.ProjectId == mapping.ProjectId
                    && item.Id.StartsWith(prefix)
                    && !item.Archived,
                item => item.Id,
                pageSize: 2,
                cancellationToken: ct);
            if (matches.Count == 1) result.Add(matches[0]);
        }
        return result;
    }

}
