namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    private Task PublishAutomationAsync(
        string eventType,
        WorkItemDocument workItem,
        string? previousStatus,
        string correlationId,
        string mutationId,
        CancellationToken ct)
    {
        if (automationEvents is null)
            return Task.CompletedTask;

        var chain = automationChain?.Current;
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = workItem.Status,
            ["PreviousStatus"] = previousStatus,
            ["Priority"] = workItem.Priority,
            ["Type"] = workItem.Type,
            ["AssigneeUserId"] = workItem.AssigneeUserId,
            ["Labels"] = string.Join(
                ',',
                workItem.Labels.Order(StringComparer.OrdinalIgnoreCase))
        };
        return automationEvents.PublishAsync(
            new WorkItemAutomationEvent(
                CurrentOrganizationId(workItem.ProjectId),
                workItem.ProjectId,
                eventType,
                $"{workItem.Id}:{mutationId}",
                workItem.Id,
                currentUser.UserId ?? "system",
                correlationId,
                clock.UtcNow,
                fields,
                chain?.RootRunId,
                chain?.ChainDepth ?? 0,
                chain?.VisitedRuleIds ?? []),
            ct);
    }
}
