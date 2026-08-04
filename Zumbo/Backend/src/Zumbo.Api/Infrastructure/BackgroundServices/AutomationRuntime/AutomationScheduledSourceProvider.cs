using System.Security.Claims;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;

public sealed class AutomationScheduledSourceProvider(
    IDocumentRepository<WorkItemDocument> workItems) : IAutomationScheduledSourceProvider
{
    public async Task<IReadOnlyCollection<AutomationScheduledSource>> ListAsync(
        string projectId,
        int maximumSources,
        CancellationToken ct)
    {
        var safeMaximum = Math.Clamp(maximumSources, 1, 5000);
        var result = new List<AutomationScheduledSource>(Math.Min(safeMaximum, 200));
        string? cursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == projectId && !item.Archived,
                cursor,
                Math.Min(200, safeMaximum - result.Count),
                ct);
            result.AddRange(page.Items.Select(item => new AutomationScheduledSource(
                item.Id,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Status"] = item.Status,
                    ["PreviousStatus"] = item.Status,
                    ["Priority"] = item.Priority,
                    ["Type"] = item.Type,
                    ["AssigneeUserId"] = item.AssigneeUserId,
                    ["Labels"] = string.Join(
                        ',',
                        item.Labels.Order(StringComparer.OrdinalIgnoreCase))
                })));
            cursor = result.Count >= safeMaximum ? null : page.NextCursor;
        } while (cursor is not null);
        return result;
    }
}
