using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;

public sealed class AutomationWorkItemActionExecutor(
    WorkItemService workItems,
    IWorkItemAutomationChainContextAccessor chainContext) : IAutomationActionExecutor
{
    private readonly GetWorkItemHandler? getWorkItemHandler;
    private readonly AssignWorkItemHandler? assignWorkItemHandler;
    private readonly ClearAssigneeHandler? clearAssigneeHandler;
    private readonly AddLabelHandler? addLabelHandler;
    private readonly RemoveLabelHandler? removeLabelHandler;
    private readonly UpdateWorkItemHandler? updateWorkItemHandler;
    private readonly AddCommentHandler? addCommentHandler;

    public AutomationWorkItemActionExecutor(
        GetWorkItemHandler getWorkItemHandler,
        AssignWorkItemHandler assignWorkItemHandler,
        ClearAssigneeHandler clearAssigneeHandler,
        AddLabelHandler addLabelHandler,
        RemoveLabelHandler removeLabelHandler,
        UpdateWorkItemHandler updateWorkItemHandler,
        AddCommentHandler addCommentHandler,
        IWorkItemAutomationChainContextAccessor chainContext)
        : this(null!, chainContext)
    {
        this.getWorkItemHandler = getWorkItemHandler;
        this.assignWorkItemHandler = assignWorkItemHandler;
        this.clearAssigneeHandler = clearAssigneeHandler;
        this.addLabelHandler = addLabelHandler;
        this.removeLabelHandler = removeLabelHandler;
        this.updateWorkItemHandler = updateWorkItemHandler;
        this.addCommentHandler = addCommentHandler;
    }

    public async Task ExecuteAsync(AutomationActionExecution execution, CancellationToken ct)
    {
        var current = getWorkItemHandler is null
            ? await workItems.GetAsync(execution.SourceId, ct)
            : await getWorkItemHandler.HandleAsync(new GetWorkItemQuery(execution.SourceId), ct);
        using var scope = chainContext.Push(new WorkItemAutomationChainContext(
            execution.RootRunId,
            execution.ChainDepth,
            execution.VisitedRuleIds));

        switch (execution.Action.Type)
        {
            case "AssignToActor":
                if (current.AssigneeUserId != execution.ActorUserId)
                {
                    var command = new AssignWorkItemCommand(
                        execution.SourceId,
                        new AssignWorkItemRequest(execution.ActorUserId),
                        execution.CorrelationId);
                    if (assignWorkItemHandler is null)
                        await workItems.AssignAsync(command.Id, command.Request, command.CorrelationId, ct);
                    else
                        await assignWorkItemHandler.HandleAsync(command, ct);
                }
                break;
            case "AssignUser":
                if (current.AssigneeUserId != execution.Action.Value)
                {
                    var command = new AssignWorkItemCommand(
                        execution.SourceId,
                        new AssignWorkItemRequest(execution.Action.Value!),
                        execution.CorrelationId);
                    if (assignWorkItemHandler is null)
                        await workItems.AssignAsync(command.Id, command.Request, command.CorrelationId, ct);
                    else
                        await assignWorkItemHandler.HandleAsync(command, ct);
                }
                break;
            case "ClearAssignee":
                if (current.AssigneeUserId is not null)
                {
                    var command = new ClearAssigneeCommand(execution.SourceId, execution.CorrelationId);
                    if (clearAssigneeHandler is null)
                        await workItems.ClearAssigneeAsync(command.Id, command.CorrelationId, ct);
                    else
                        await clearAssigneeHandler.HandleAsync(command, ct);
                }
                break;
            case "AddLabel":
                if (!current.Labels.Contains(execution.Action.Value!, StringComparer.OrdinalIgnoreCase))
                {
                    var command = new AddLabelCommand(
                        execution.SourceId,
                        new AddLabelRequest(execution.Action.Value!));
                    if (addLabelHandler is null)
                        await workItems.AddLabelAsync(command.Id, command.Request, ct);
                    else
                        await addLabelHandler.HandleAsync(command, ct);
                }
                break;
            case "RemoveLabel":
                if (current.Labels.Contains(execution.Action.Value!, StringComparer.OrdinalIgnoreCase))
                {
                    var command = new RemoveLabelCommand(execution.SourceId, execution.Action.Value!);
                    if (removeLabelHandler is null)
                        await workItems.RemoveLabelAsync(command.Id, command.Label, ct);
                    else
                        await removeLabelHandler.HandleAsync(command, ct);
                }
                break;
            case "SetPriority":
                if (!current.Priority.Equals(execution.Action.Value, StringComparison.OrdinalIgnoreCase))
                {
                    var command = new UpdateWorkItemCommand(
                        execution.SourceId,
                        new UpdateWorkItemRequest(null, null, execution.Action.Value, current.DueDate),
                        execution.CorrelationId);
                    if (updateWorkItemHandler is null)
                        await workItems.UpdateAsync(command.Id, command.Request, command.CorrelationId, ct);
                    else
                        await updateWorkItemHandler.HandleAsync(command, ct);
                }
                break;
            case "AddComment":
                var addCommentCommand = new AddCommentCommand(
                    execution.SourceId,
                    new AddCommentRequest(
                        execution.Action.Value!,
                        [],
                        $"automation:{execution.RunId}:{execution.ActionIndex}"),
                    execution.CorrelationId);
                if (addCommentHandler is null)
                    await workItems.AddCommentAsync(
                        addCommentCommand.Id,
                        addCommentCommand.Request,
                        addCommentCommand.CorrelationId,
                        ct);
                else
                    await addCommentHandler.HandleAsync(addCommentCommand, ct);
                break;
            default:
                throw new InvalidOperationException(
                    $"Automation action '{execution.Action.Type}' is not supported by the work-item adapter.");
        }
    }
}
