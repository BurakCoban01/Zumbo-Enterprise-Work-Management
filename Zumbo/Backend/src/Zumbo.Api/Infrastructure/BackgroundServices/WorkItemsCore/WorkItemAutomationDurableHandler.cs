using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;

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
