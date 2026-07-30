using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;

public sealed class AutomationWorkItemActionExecutor(
    WorkItemService workItems,
    IWorkItemAutomationChainContextAccessor chainContext) : IAutomationActionExecutor
{
    public async Task ExecuteAsync(AutomationActionExecution execution, CancellationToken ct)
    {
        var current = await workItems.GetAsync(execution.SourceId, ct);
        using var scope = chainContext.Push(new WorkItemAutomationChainContext(
            execution.RootRunId,
            execution.ChainDepth,
            execution.VisitedRuleIds));

        switch (execution.Action.Type)
        {
            case "AssignToActor":
                if (current.AssigneeUserId != execution.ActorUserId)
                {
                    await workItems.AssignAsync(
                        execution.SourceId,
                        new AssignWorkItemRequest(execution.ActorUserId),
                        execution.CorrelationId,
                        ct);
                }
                break;
            case "AssignUser":
                if (current.AssigneeUserId != execution.Action.Value)
                {
                    await workItems.AssignAsync(
                        execution.SourceId,
                        new AssignWorkItemRequest(execution.Action.Value!),
                        execution.CorrelationId,
                        ct);
                }
                break;
            case "ClearAssignee":
                if (current.AssigneeUserId is not null)
                {
                    await workItems.ClearAssigneeAsync(
                        execution.SourceId,
                        execution.CorrelationId,
                        ct);
                }
                break;
            case "AddLabel":
                if (!current.Labels.Contains(execution.Action.Value!, StringComparer.OrdinalIgnoreCase))
                {
                    await workItems.AddLabelAsync(
                        execution.SourceId,
                        new AddLabelRequest(execution.Action.Value!),
                        ct);
                }
                break;
            case "RemoveLabel":
                if (current.Labels.Contains(execution.Action.Value!, StringComparer.OrdinalIgnoreCase))
                {
                    await workItems.RemoveLabelAsync(
                        execution.SourceId,
                        execution.Action.Value!,
                        ct);
                }
                break;
            case "SetPriority":
                if (!current.Priority.Equals(execution.Action.Value, StringComparison.OrdinalIgnoreCase))
                {
                    await workItems.UpdateAsync(
                        execution.SourceId,
                        new UpdateWorkItemRequest(null, null, execution.Action.Value, current.DueDate),
                        execution.CorrelationId,
                        ct);
                }
                break;
            case "AddComment":
                await workItems.AddCommentAsync(
                    execution.SourceId,
                    new AddCommentRequest(
                        execution.Action.Value!,
                        [],
                        $"automation:{execution.RunId}:{execution.ActionIndex}"),
                    execution.CorrelationId,
                    ct);
                break;
            default:
                throw new InvalidOperationException(
                    $"Automation action '{execution.Action.Type}' is not supported by the work-item adapter.");
        }
    }
}

public sealed class WorkItemAutomationDurableHandler(
    AutomationExecutionService automation,
    AutomationActorContextRunner actors) : IDurableEventHandler
{
    public string ConsumerName => "work-item-automation-v1";
    public string EventType => WorkItemDurableEventTypes.Automation;

    public async Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = DurablePayload.Read<WorkItemAutomationEvent>(message);
        _ = await actors.RunAsync(
            payload.ActorUserId,
            payload.OrganizationId,
            payload.CorrelationId,
            actorAvailable => automation.ExecuteAsync(
                new AutomationExecutionContext(
                    payload.OrganizationId,
                    payload.ProjectId,
                    "Event",
                    payload.EventType,
                    payload.TriggerId,
                    payload.WorkItemId,
                    payload.ActorUserId,
                    payload.CorrelationId,
                    payload.OccurredAtUtc,
                    payload.Fields,
                    actorAvailable,
                    payload.RootRunId,
                    payload.ChainDepth,
                    payload.VisitedRuleIds),
                cancellationToken),
            cancellationToken);
    }
}
